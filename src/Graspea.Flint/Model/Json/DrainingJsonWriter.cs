using System.Text.Json;

namespace Graspea.Flint.Serialization;

/// <summary>
/// A <see cref="Utf8JsonWriter"/> over a pooled buffer that is drained to a stream once it holds
/// <see cref="DrainThreshold"/> bytes. A flushed writer holds no buffer memory, so draining keeps its state.
/// </summary>
internal sealed class DrainingJsonWriter : IDisposable
{
    /// <summary>The buffer is drained to the stream once it holds this much (design §13.15: internal, not configurable).</summary>
    internal const int DrainThreshold = 16 * 1024;

    private readonly PooledBufferWriter buffer = new(2 * DrainThreshold);

    internal DrainingJsonWriter(JsonWriterOptions options)
    {
        Writer = new Utf8JsonWriter(buffer, options);
    }

    internal Utf8JsonWriter Writer { get; }

    /// <summary>Commits the writer's pending bytes to the buffer and tells whether the buffer should be drained.</summary>
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

    /// <summary>Appends raw bytes after the writer's pending output. They bypass the writer's state, so this is only for
    /// the envelope around a document the writer does not track.</summary>
    internal void WriteRaw(ReadOnlySpan<byte> bytes)
    {
        Writer.Flush();
        buffer.Write(bytes);
    }

    public void Dispose()
    {
        // The writer first: it still references the buffer it writes to.
        Writer.Dispose();
        buffer.Dispose();
    }
}
