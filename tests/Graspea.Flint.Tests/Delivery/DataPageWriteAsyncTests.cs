using Graspea.Flint.Delivery;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Delivery;

public sealed class DataPageWriteAsyncTests
{
    private static DataPage Page()
    {
        var handle = new ChartHandle<Visit>("traffic", Chart.Line(DeliveryJson.Default.Visit).X(r => r.Source).Y(r => r.Visits).ToTemplate());
        return new DataPage(new ChartGroup("g", handle), [handle.Rows([new Visit("web", 10), new Visit("mail", 4)])], "next-1");
    }

    // source: §14.8.5 step 8; Support/RecordingStream.cs (sync Write and Flush throw, as Kestrel's do)
    [Test]
    public async Task WriteAsync_UsesAsyncWritesOnly()
    {
        var page = Page();
        var stream = new RecordingStream(asyncOnly: true);
        await page.WriteAsync(stream);
        Assert.That(stream.Bytes, Is.EqualTo(page.ToUtf8Bytes()));
        Assert.That(stream.Writes, Is.EqualTo(1));
        Assert.That(stream.Flushes, Is.EqualTo(1));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var untouched = new RecordingStream(asyncOnly: true);
        Assert.That(async () => await page.WriteAsync(untouched, cts.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(untouched.Received, Is.EqualTo(0));

        Assert.That(() => page.WriteAsync(null!), Throws.TypeOf<ArgumentNullException>());
    }

    // source: §14.4.6 Failure model (the stream failing propagates unchanged; nothing more is written)
    [Test]
    public void WriteAsync_StreamFailures_Propagate()
    {
        var page = Page();
        var writeFails = new FailingStream(failWrite: true);
        var boom = Assert.ThrowsAsync<IOException>(async () => await page.WriteAsync(writeFails))!;
        Assert.That(boom, Is.SameAs(writeFails.Failure));
        Assert.That(writeFails.Flushes, Is.EqualTo(0));
        Assert.That(writeFails.WriteCalls, Is.EqualTo(1));

        var flushFails = new FailingStream(failWrite: false);
        var flushBoom = Assert.ThrowsAsync<IOException>(async () => await page.WriteAsync(flushFails))!;
        Assert.That(flushBoom, Is.SameAs(flushFails.Failure));
        Assert.That(flushFails.WriteCalls, Is.EqualTo(1));
        Assert.That(flushFails.Flushes, Is.EqualTo(1));
        Assert.That(flushFails.Received, Is.EqualTo(page.ToUtf8Bytes().Length));
    }


    // source: §14.4.6 Failure model; §14.8.5 step 8
    [Test]
    public async Task WriteStreamedAsync_StreamFailures_Propagate()
    {
        var rows = Enumerable.Range(0, 3000).Select(i => new Visit("source " + i, i)).ToList();
        var handle = new ChartHandle<Visit>("traffic", Chart.Line(DeliveryJson.Default.Visit).X(r => r.Source).Y(r => r.Visits).ToTemplate());
        var group = new ChartGroup("g", handle);
        var expected = (await WrittenBytes(new DataPage(group, [await handle.RowsAsync(AsyncRows.Of(rows))]))).Length;

        var writeFails = new FailingStream(failWrite: true);
        var boom = Assert.ThrowsAsync<IOException>(async () => await new DataPage(group, [handle.Stream(AsyncRows.Of(rows))]).WriteAsync(writeFails))!;
        Assert.That(boom, Is.SameAs(writeFails.Failure));
        Assert.That(writeFails.WriteCalls, Is.EqualTo(1));
        Assert.That(writeFails.Flushes, Is.EqualTo(0));

        var flushFails = new FailingStream(failWrite: false);
        var flushBoom = Assert.ThrowsAsync<IOException>(async () => await new DataPage(group, [handle.Stream(AsyncRows.Of(rows))]).WriteAsync(flushFails))!;
        Assert.That(flushBoom, Is.SameAs(flushFails.Failure));
        Assert.That(flushFails.Flushes, Is.EqualTo(1));
        Assert.That(flushFails.Received, Is.EqualTo(expected));
    }

    private static async Task<byte[]> WrittenBytes(DataPage page)
    {
        var stream = new RecordingStream(asyncOnly: true);
        await page.WriteAsync(stream);
        return stream.Bytes;
    }

    private sealed class FailingStream(bool failWrite) : Stream

    {
        public IOException Failure { get; } = new("transport failed");

        public int WriteCalls { get; private set; }

        public int Flushes { get; private set; }

        public long Received { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("sync");

        public override void Flush() => throw new InvalidOperationException("sync");

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WriteCalls++;
            if (failWrite)
            {
                // Some bytes arrive, then the transport fails.
                Received += buffer.Length / 2;
                throw Failure;
            }

            Received += buffer.Length;
            return ValueTask.CompletedTask;
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            Flushes++;
            return failWrite ? Task.CompletedTask : Task.FromException(Failure);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
