using System.Text.Json;

namespace Graspea.Flint.Serialization;

/// <summary>
/// The library-owned output of a streamed write (design §13.5 steps 3-6): a pooled buffer and a
/// <see cref="Utf8JsonWriter"/> over it, never over the stream, with the options of the compact
/// <see cref="FlintJson.Serialize(ChartAssemblyInput, bool)"/>, so the bytes equal the materialised output. The buffer
/// gets <c>{"data":</c> raw, then the data object through the writer, then the rest of the document, serialised once,
/// after <c>,</c>. The writer only ever writes the data object, so its state and comma tracking are never bypassed.
/// </summary>
internal sealed class StreamedDocument : IDisposable
{
    /// <summary>The buffer is drained to the stream once it holds this much (design §13.15: internal, not configurable).</summary>
    internal const int DrainThreshold = 16 * 1024;

    /// <summary>The depth limit of the materialised write, which counts from the document; the writer starts one level down,
    /// at the data object, after the raw <c>{</c>.</summary>
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        MaxDepth = (FlintJson.Options.MaxDepth == 0 ? DefaultMaxDepth : FlintJson.Options.MaxDepth) - 1,
    };

    /// <summary>The depth System.Text.Json applies when the options set none.</summary>
    private const int DefaultMaxDepth = 64;

    private readonly PooledBufferWriter buffer = new(2 * DrainThreshold);
    private readonly byte[] remainder;

    internal StreamedDocument(ChartAssemblyInput input)
    {
        remainder = JsonSerializer.SerializeToUtf8Bytes(input.WithoutData(), FlintJsonContext.Default.ChartAssemblyInput);
        Writer = new Utf8JsonWriter(buffer, WriterOptions);
        buffer.Write("{\"data\":"u8);
        ChartDataConverter.WriteStreamedStart(Writer);
    }

    internal Utf8JsonWriter Writer { get; }

    /// <summary>Commits the writer's pending bytes to the buffer and tells whether the buffer should be drained. A flushed
    /// writer holds no buffer memory, so draining keeps its state.</summary>
    internal bool IsFull
    {
        get
        {
            Writer.Flush();
            return buffer.WrittenCount >= DrainThreshold;
        }
    }

    internal ReadOnlyMemory<byte> Written => buffer.WrittenMemory;

    /// <summary>The written bytes went to the stream.</summary>
    internal void Drained() => buffer.Clear();

    /// <summary>Closes the data object and appends the rest of the document: <c>}</c> when it has no other member,
    /// otherwise <c>,</c> and its members.</summary>
    internal void End(ChartData data)
    {
        ChartDataConverter.WriteStreamedEnd(Writer, data);
        Writer.Flush();
        if (remainder.Length == 2)
        {
            buffer.Write("}"u8);
            return;
        }

        buffer.Write(","u8);
        buffer.Write(remainder.AsSpan(1));
    }

    public void Dispose()
    {
        Writer.Dispose();
        buffer.Dispose();
    }
}
