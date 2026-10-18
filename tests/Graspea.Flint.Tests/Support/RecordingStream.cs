namespace Graspea.Flint.Tests.Support;

/// <summary>A write-only stream that keeps what it receives and counts the calls. With <c>asyncOnly</c> it throws on every
/// synchronous Write and Flush, as an ASP.NET Core response body does with AllowSynchronousIO = false.</summary>
public sealed class RecordingStream(bool asyncOnly = false, bool keep = true) : Stream
{
    private readonly MemoryStream received = new();

    public int Writes { get; private set; }

    public int Flushes { get; private set; }

    public long Received { get; private set; }

    public byte[] Bytes => received.ToArray();

    public string Text => System.Text.Encoding.UTF8.GetString(Bytes);

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (asyncOnly)
        {
            throw new InvalidOperationException("Synchronous operations are disallowed.");
        }

        Keep(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        Keep(buffer.Span);
    }

    public override void Flush()
    {
        if (asyncOnly)
        {
            throw new InvalidOperationException("Synchronous operations are disallowed.");
        }

        Flushes++;
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        Flushes++;
        return Task.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    private void Keep(ReadOnlySpan<byte> bytes)
    {
        Writes++;
        Received += bytes.Length;
        if (keep)
        {
            received.Write(bytes);
        }
    }
}
