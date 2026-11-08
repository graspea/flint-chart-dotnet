using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Graspea.Flint.Serialization;

namespace Graspea.Flint.Delivery;

/// <summary>One page of rows for a group: <c>{"contract":1,"group":...,"charts":{...},"next":...}</c>. Charts are written in
/// the group's order, whatever the order of the parts. A materialised page is complete before any byte is written, so every
/// row, limit and invariant failure happens before the first byte; transport failures (the stream failing) can come at any
/// time, and the SDK writes nothing after a failure.</summary>
public sealed class DataPage
{
    private const string StreamedPart = "The page holds a streamed part; write it with DataPage.WriteAsync.";
    private const string PageWritten = "The page holds a streamed part and was already written; build a new page with new parts for each write.";
    private const string PartWritten = "The streamed part was already written; create a new part with ChartHandle<TRow>.Stream for each write.";

    private readonly bool hasStreamed;
    private int claimed;

    /// <summary>Creates a page.</summary>
    /// <param name="group">The group.</param>
    /// <param name="charts">One part for each chart of the group, in any order; the list is copied.</param>
    /// <param name="next">The cursor of the following page, or null on the last page; never empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="group"/> or <paramref name="charts"/> is null.</exception>
    /// <exception cref="ArgumentException">A part is null or belongs to a chart outside the group, a chart of the group has no
    /// part or two, or <paramref name="next"/> is empty.</exception>
    public DataPage(ChartGroup group, IReadOnlyList<ChartPart> charts, string? next = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(charts);
        if (next is { Length: 0 })
        {
            throw new ArgumentException("A cursor must not be empty; pass null on the last page.", nameof(next));
        }

        var byChart = new Dictionary<ChartHandle, ChartPart>(ReferenceEqualityComparer.Instance);
        foreach (var part in charts.ToArray())
        {
            if (part is null)
            {
                throw new ArgumentException("A page must not hold a null part.", nameof(charts));
            }

            if (!group.Charts.Contains(part.Chart))
            {
                throw new ArgumentException($"The chart '{part.Chart.Id}' is not in the group '{group.Name}'.", nameof(charts));
            }

            if (!byChart.TryAdd(part.Chart, part))
            {
                throw new ArgumentException($"The chart '{part.Chart.Id}' has two parts.", nameof(charts));
            }
        }

        var ordered = new ChartPart[group.Charts.Count];
        for (var i = 0; i < ordered.Length; i++)
        {
            ordered[i] = byChart.TryGetValue(group.Charts[i], out var part)
                ? part
                : throw new ArgumentException($"The chart '{group.Charts[i].Id}' of the group has no part.", nameof(charts));
        }

        hasStreamed = ordered.Any(p => p.IsStreamed);
        Group = group;
        Charts = new ReadOnlyCollection<ChartPart>(ordered);
        Next = next;
    }

    /// <summary>The group.</summary>
    public ChartGroup Group { get; }

    /// <summary>The parts in the group's order; a read-only snapshot.</summary>
    public IReadOnlyList<ChartPart> Charts { get; }

    /// <summary>The cursor of the following page, or null on the last page.</summary>
    public string? Next { get; }

    /// <summary>Writes the page to <paramref name="writer"/>.</summary>
    /// <param name="writer">The writer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The page holds a streamed part; write it with <see cref="WriteAsync"/>.</exception>
    public void WriteTo(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ThrowIfStreamed();
        WriteStart(writer);
        foreach (var part in Charts)
        {
            WritePartStart(writer, part);
            if (part.WithheldCode is null)
            {
                writer.WritePropertyName("rows");
                writer.WriteRawValue(part.Utf8Rows!, skipInputValidation: true);
            }

            writer.WriteEndObject();
        }

        WriteEnd(writer);
    }

    private void WriteStart(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteNumber("contract", DeliveryContract.Version);
        writer.WriteString("group", Group.Name);
        writer.WriteStartObject("charts");
    }

    private static void WritePartStart(Utf8JsonWriter writer, ChartPart part)
    {
        writer.WriteStartObject(part.Chart.Id);
        if (part.WithheldCode is { } code)
        {
            writer.WriteString("withheld", code);
        }
    }

    private void WriteEnd(Utf8JsonWriter writer)
    {
        writer.WriteEndObject();
        if (Next is not null)
        {
            writer.WriteString("next", Next);
        }

        writer.WriteEndObject();
    }

    private void ThrowIfStreamed()
    {
        if (hasStreamed)
        {
            throw new InvalidOperationException(StreamedPart);
        }
    }

    /// <summary>The page as compact UTF-8 JSON.</summary>
    /// <returns>The bytes.</returns>
    /// <exception cref="InvalidOperationException">The page holds a streamed part; write it with <see cref="WriteAsync"/>.</exception>
    public byte[] ToUtf8Bytes()
    {
        ThrowIfStreamed();
        using var buffer = new PooledBufferWriter(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteTo(writer);
        }

        return buffer.WrittenMemory.ToArray();
    }

    /// <summary>Writes the page to a stream with asynchronous writes only: no synchronous Write or Flush is called, so it is
    /// safe on a response body that disallows synchronous I/O. Failures of the stream propagate unchanged and nothing is
    /// written after one. A page with a streamed part is single-use: it claims its streamed parts before the first byte, reads
    /// each source once, and drains its buffer to the stream whenever it holds 16 KiB, so memory stays bounded by the largest
    /// single row or materialised part plus about 32 KiB. A failure while a streamed part is written (a row, limit or source
    /// failure, or cancellation) propagates unchanged; when it comes after a drain the stream holds a truncated prefix of the
    /// page, which is not valid JSON, and the host must not append to it. A token cancelled before the call throws <see cref="OperationCanceledException"/> before the first byte, after the claims, whether or not the stream or the sources observe it. A claim cannot be undone: when a later streamed part was already written elsewhere, the page and the parts claimed before it are spent, although nothing reached the stream; build a new page with new parts.</summary>
    /// <param name="utf8Json">The stream.</param>
    /// <param name="cancellationToken">Cancels the write and reaches the sources.</param>
    /// <returns>A task that completes when the page is written and flushed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Json"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The page, or a streamed part of it, was already written.</exception>
    public Task WriteAsync(Stream utf8Json, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);
        if (!hasStreamed)
        {
            return WriteBytesAsync(utf8Json, ToUtf8Bytes(), cancellationToken);
        }

        if (Interlocked.Exchange(ref claimed, 1) != 0)
        {
            throw new InvalidOperationException(PageWritten);
        }

        foreach (var part in Charts)
        {
            part.Streamed?.Claim(PartWritten);
        }

        return WriteStreamedAsync(utf8Json, cancellationToken);
    }

    private async Task WriteStreamedAsync(Stream stream, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var output = new DrainingJsonWriter(new JsonWriterOptions { MaxDepth = JsonDepth.Effective + 1 });
        var writer = output.Writer;

        async ValueTask DrainAsync(CancellationToken token)
        {
            if (output.IsFull)
            {
                await stream.WriteAsync(output.Written, token).ConfigureAwait(false);
                output.Drained();
            }
        }

        WriteStart(writer);
        foreach (var part in Charts)
        {
            WritePartStart(writer, part);
            if (part.Streamed is { } streamed)
            {
                writer.WritePropertyName("rows");
                writer.WriteStartArray();
                await streamed.WriteRowsAsync(writer, DrainAsync, cancellationToken).ConfigureAwait(false);
                writer.WriteEndArray();
            }
            else if (part.WithheldCode is null)
            {
                writer.WritePropertyName("rows");
                writer.WriteRawValue(part.Utf8Rows!, skipInputValidation: true);
            }

            writer.WriteEndObject();
            await DrainAsync(cancellationToken).ConfigureAwait(false);
        }

        WriteEnd(writer);
        writer.Flush();
        await stream.WriteAsync(output.Written, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteBytesAsync(Stream stream, byte[] bytes, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The page as compact JSON text.</summary>
    /// <returns>The JSON text.</returns>
    /// <exception cref="InvalidOperationException">The page holds a streamed part; write it with <see cref="WriteAsync"/>.</exception>
    public string ToJson() => Encoding.UTF8.GetString(ToUtf8Bytes());
}
