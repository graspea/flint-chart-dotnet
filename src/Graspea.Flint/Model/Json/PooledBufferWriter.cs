using System.Buffers;

namespace Graspea.Flint.Serialization;

/// <summary>An <see cref="IBufferWriter{T}"/> over arrays rented from <see cref="ArrayPool{T}.Shared"/>; the streamed writer's
/// buffer (design §13.5 step 4). <see cref="Clear"/> keeps the array, so a drained buffer is reused.</summary>
internal sealed class PooledBufferWriter(int initialCapacity) : IBufferWriter<byte>, IDisposable
{
    private byte[] buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
    private int index;

    internal int WrittenCount => index;

    internal ReadOnlyMemory<byte> WrittenMemory => buffer.AsMemory(0, index);

    internal void Clear() => index = 0;

    internal void Write(ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(GetSpan(bytes.Length));
        index += bytes.Length;
    }

    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, buffer.Length - index);
        index += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return buffer.AsMemory(index);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return buffer.AsSpan(index);
    }

    public void Dispose()
    {
        var rented = buffer;
        buffer = [];
        index = 0;
        if (rented.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private void Ensure(int sizeHint)
    {
        var needed = Math.Max(sizeHint, 1);
        if (buffer.Length - index >= needed)
        {
            return;
        }

        var larger = ArrayPool<byte>.Shared.Rent(Math.Max(checked(index + needed), buffer.Length * 2));
        buffer.AsSpan(0, index).CopyTo(larger);
        ArrayPool<byte>.Shared.Return(buffer);
        buffer = larger;
    }
}
