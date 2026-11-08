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
    internal const int DrainThreshold = DrainingJsonWriter.DrainThreshold;

    /// <summary>The writer starts one level below the document's depth limit, at the data object, after the raw <c>{</c>.</summary>
    private readonly DrainingJsonWriter drain = new(new JsonWriterOptions { MaxDepth = JsonDepth.Effective - 1 });
    private readonly byte[] remainder;

    internal StreamedDocument(ChartAssemblyInput input)
    {
        remainder = JsonSerializer.SerializeToUtf8Bytes(input.WithoutData(), FlintJsonContext.Default.ChartAssemblyInput);
        drain.WriteRaw("{\"data\":"u8);
        ChartDataConverter.WriteStreamedStart(Writer);
    }

    internal Utf8JsonWriter Writer => drain.Writer;

    internal bool IsFull => drain.IsFull;

    internal ReadOnlyMemory<byte> Written => drain.Written;

    /// <summary>The written bytes went to the stream.</summary>
    internal void Drained() => drain.Drained();

    /// <summary>Closes the data object and appends the rest of the document: <c>}</c> when it has no other member,
    /// otherwise <c>,</c> and its members.</summary>
    internal void End(ChartData data)
    {
        ChartDataConverter.WriteStreamedEnd(Writer, data);
        if (remainder.Length == 2)
        {
            drain.WriteRaw("}"u8);
            return;
        }

        drain.WriteRaw(","u8);
        drain.WriteRaw(remainder.AsSpan(1));
    }

    public void Dispose() => drain.Dispose();
}
