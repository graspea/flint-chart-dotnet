using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Graspea.Flint.Delivery;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Delivery;

public sealed class DataPageStreamedTests
{
    private static readonly Sale[] Edge =
    [
        new(new DateOnly(2025, 1, 1), "North", 1200.50m),
        new(new DateOnly(2025, 2, 1), "Coast <é+>\"\\ \n", 0m),
        new(new DateOnly(2025, 3, 1), "", 0.000001m),
        new(new DateOnly(2025, 4, 1), "East", -79228162514264337593543950335m),
    ];

    private static ChartHandle<Sale> SalesHandle(string id = "revenue", int? maxRows = null) =>
        new(id, Chart.Line(DeliveryJson.Default.Sale).X(r => r.Month).Y(r => r.Revenue).ToTemplate(), maxRows);

    private static Sale Row(int i) => new(new DateOnly(2025, 1, 1).AddDays(i % 365), "North " + i, i * 1.25m);

    private static IEnumerable<Sale> Many(int count) => Enumerable.Range(0, count).Select(Row);

    private static async Task<byte[]> Written(DataPage page)
    {
        var stream = new RecordingStream(asyncOnly: true);
        await page.WriteAsync(stream);
        return stream.Bytes;
    }

    // source: §14.8.5 step 8a; §14.4.2 Byte identity
    [Test]
    public async Task Streamed_EqualsRowsAsync()
    {
        var a = SalesHandle("a");
        var b = SalesHandle("b");
        var c = SalesHandle("c");
        var group = new ChartGroup("g", a, b, c);
        var big = Many(300).ToArray();
        Sale?[] withNull = [Edge[0], null, Edge[1]];
        foreach (var nullable in new Sale?[][] { [.. Edge], [], big, withNull })
        {
            Sale[] rows = [.. nullable!];
            var solo = new ChartGroup("solo", a);
            Assert.That(await Written(new DataPage(solo, [a.Stream(AsyncRows.Of(rows))], "n")), Is.EqualTo(await Written(new DataPage(solo, [await a.RowsAsync(AsyncRows.Of(rows))], "n"))));
            var expected = await Written(new DataPage(group, [await a.RowsAsync(AsyncRows.Of(rows)), await b.RowsAsync(AsyncRows.Of(rows)), await c.RowsAsync(AsyncRows.Of(rows))], "n"));

            Assert.That(await Written(new DataPage(group, [a.Stream(AsyncRows.Of(rows)), await b.RowsAsync(AsyncRows.Of(rows)), await c.RowsAsync(AsyncRows.Of(rows))], "n")), Is.EqualTo(expected));
            Assert.That(await Written(new DataPage(group, [await a.RowsAsync(AsyncRows.Of(rows)), b.Stream(AsyncRows.Of(rows)), await c.RowsAsync(AsyncRows.Of(rows))], "n")), Is.EqualTo(expected));
            Assert.That(await Written(new DataPage(group, [a.Stream(AsyncRows.Of(rows)), await b.RowsAsync(AsyncRows.Of(rows)), c.Stream(AsyncRows.Of(rows))], "n")), Is.EqualTo(expected));
        }
    }

    // source: §14.4.6 drain (a page with several large materialised parts around a streamed part drains in several writes)
    [Test]
    public async Task Streamed_AroundLargeMaterialisedParts_EqualsRowsAsync()
    {
        var a = SalesHandle("a");
        var b = SalesHandle("b");
        var c = SalesHandle("c");
        var group = new ChartGroup("g", a, b, c);
        var rows = Many(400).ToArray();
        var expected = await Written(new DataPage(group, [await a.RowsAsync(AsyncRows.Of(rows)), await b.RowsAsync(AsyncRows.Of(rows)), await c.RowsAsync(AsyncRows.Of(rows))]));
        var stream = new RecordingStream(asyncOnly: true);
        await new DataPage(group, [await a.RowsAsync(AsyncRows.Of(rows)), b.Stream(AsyncRows.Of(rows)), await c.RowsAsync(AsyncRows.Of(rows))]).WriteAsync(stream);
        Assert.That(stream.Bytes, Is.EqualTo(expected));
        Assert.That(stream.Writes, Is.GreaterThan(3));
        Assert.That(stream.Flushes, Is.EqualTo(1));
    }

    // source: §14.4.1 WriteAsync summary (drained every 16 KiB); FlintJsonStreamingTests.cs:113-123
    [Test]
    public async Task Streamed_DrainsInSeveralWrites()
    {
        var handle = SalesHandle();
        var stream = new RecordingStream(asyncOnly: true);
        await new DataPage(new ChartGroup("g", handle), [handle.Stream(AsyncRows.Of(Many(5000)))]).WriteAsync(stream);
        Assert.That(stream.Received, Is.GreaterThan(4 * 16384));
        Assert.That(stream.Writes, Is.GreaterThan(4));
        Assert.That(stream.Flushes, Is.EqualTo(1));
    }

    // source: §14.4.2 Errors of streamed parts; §14.4.6 claims
    [Test]
    public async Task Streamed_SyncFinishersThrow()
    {
        var handle = SalesHandle();
        var page = new DataPage(new ChartGroup("g", handle), [handle.Stream(AsyncRows.Of(Edge))]);
        Assert.That(page.Charts[0].IsStreamed, Is.True);
        Assert.That(() => page.ToUtf8Bytes(), Throws.InvalidOperationException.With.Message.Contains("DataPage.WriteAsync"));
        Assert.That(() => page.ToJson(), Throws.InvalidOperationException.With.Message.Contains("DataPage.WriteAsync"));
        using var writer = new Utf8JsonWriter(new MemoryStream());
        Assert.That(() => page.WriteTo(writer), Throws.InvalidOperationException.With.Message.Contains("DataPage.WriteAsync"));
        Assert.That(writer.BytesPending == 0 && writer.BytesCommitted == 0, Is.True);
        Assert.That(System.Text.Encoding.UTF8.GetString(await Written(page)), Does.StartWith("{\"contract\":1,\"group\":\"g\""));
    }

    // source: §14.4.6 Write bullet (claims before writing); StreamedRows.cs:26-33
    [Test]
    public async Task Streamed_SingleUse()
    {
        var handle = SalesHandle();
        var group = new ChartGroup("g", handle);
        var part = handle.Stream(AsyncRows.Of(Edge));
        var page = new DataPage(group, [part]);
        await Written(page);

        var second = new RecordingStream(asyncOnly: true);
        Assert.That(() => page.WriteAsync(second), Throws.InvalidOperationException.With.Message.Contains("already written"));
        Assert.That(second.Received, Is.EqualTo(0));

        var other = new RecordingStream(asyncOnly: true);
        Assert.That(() => new DataPage(group, [part]).WriteAsync(other), Throws.InvalidOperationException.With.Message.Contains("already written"));
        Assert.That(other.Received, Is.EqualTo(0));

        var fresh = new DataPage(group, [handle.Stream(AsyncRows.Of(Many(2000)))]);
        var firstStream = new RecordingStream(asyncOnly: true);
        var secondStream = new RecordingStream(asyncOnly: true);
        var tasks = new[] { Task.Run(() => fresh.WriteAsync(firstStream)), Task.Run(() => fresh.WriteAsync(secondStream)) };
        var failures = 0;
        foreach (var task in tasks)
        {
            try
            {
                await task;
            }
            catch (InvalidOperationException)
            {
                failures++;
            }
        }

        Assert.That(failures, Is.EqualTo(1));
        Assert.That(new[] { firstStream, secondStream }.Count(s => s.Received == 0), Is.EqualTo(1));
    }

    private static JsonTypeInfo<Note> PredicateTypeInfo()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            foreach (var property in info.Properties.Where(p => p.Name == "Text"))
            {
                property.ShouldSerialize = (_, value) => value is null;
            }
        });
        return (JsonTypeInfo<Note>)new JsonSerializerOptions { TypeInfoResolver = resolver }.GetTypeInfo(typeof(Note));
    }

    /// <summary>A page whose streamed part fails at row <paramref name="failAt"/> with the given kind.</summary>
    private static (DataPage Page, Action<Exception> Check) Failing(string kind, int failAt, int total)
    {
        var boom = new InvalidOperationException("boom");
        switch (kind)
        {
            case "predicate":
            {
                var handle = new ChartHandle<Note>("n", Chart.Line(PredicateTypeInfo()).X(r => r.City).ToTemplate());
                var rows = Enumerable.Range(0, total).Select(i => new Note("City " + i, i == failAt ? "x" : null));
                return (new DataPage(new ChartGroup("g", handle), [handle.Stream(AsyncRows.Of(rows))]), e => Assert.That(e, Is.InstanceOf<ArgumentException>()));
            }

            case "nan":
            {
                var handle = new ChartHandle<Reading>("r", Chart.Line(DeliveryJson.Default.Reading).X(r => r.Station).Y(r => r.Value).ToTemplate());
                var rows = Enumerable.Range(0, total).Select(i => new Reading("Station " + i, i == failAt ? double.NaN : i));
                return (new DataPage(new ChartGroup("g", handle), [handle.Stream(AsyncRows.Of(rows))]), e => Assert.That(e, Is.InstanceOf<ArgumentException>()));
            }

            case "source":
            {
                var handle = SalesHandle();
                var rows = AsyncRows.Of(Many(total), i => { if (i == failAt) { throw boom; } });
                return (new DataPage(new ChartGroup("g", handle), [handle.Stream(rows)]), e => Assert.That(e, Is.SameAs(boom)));
            }

            case "maxrows":
            {
                var handle = SalesHandle(maxRows: failAt);
                return (new DataPage(new ChartGroup("g", handle), [handle.Stream(AsyncRows.Of(Many(total)))]), e => Assert.That(e, Is.TypeOf<ChartRowLimitException>()));
            }

            default:
            {
                var handle = SalesHandle();
                var cts = new CancellationTokenSource();
                var rows = AsyncRows.Of(Many(total), i => { if (i == failAt) { cts.Cancel(); } });
                CancelSource = cts;
                return (new DataPage(new ChartGroup("g", handle), [handle.Stream(rows)]), e => Assert.That(e, Is.InstanceOf<OperationCanceledException>()));
            }
        }
    }

    private static CancellationTokenSource? CancelSource;

    private static async Task<(RecordingStream Stream, Exception Error)> Fail(string kind, int failAt, int total)
    {
        CancelSource = null;
        var (page, check) = Failing(kind, failAt, total);
        var stream = new RecordingStream(asyncOnly: true);
        Exception? error = null;
        try
        {
            await page.WriteAsync(stream, CancelSource?.Token ?? default);
        }
        catch (Exception e)
        {
            error = e;
        }

        Assert.That(error, Is.Not.Null);
        check(error!);
        return (stream, error!);
    }

    private static async IAsyncEnumerable<Sale> EmptyIgnoringToken()
    {
        await Task.Yield();
        yield break;
    }

    // source: DataPage.WriteAsync doc (a cancelled token throws before the first byte, even for a stream that ignores it)
    [Test]
    public async Task Streamed_PreCancelledToken_ThrowsBeforeTheFirstByteForAnEmptySource()
    {
        var handle = SalesHandle();
        var page = new DataPage(new ChartGroup("g", handle), [handle.Stream(EmptyIgnoringToken())]);
        var stream = new TokenIgnoringStream();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.That(async () => await page.WriteAsync(stream, cts.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(stream.Written, Is.EqualTo(0));
    }

    private sealed class TokenIgnoringStream : Stream
    {
        public long Written { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Written += buffer.Length;
            return ValueTask.CompletedTask;
        }

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush() => throw new NotSupportedException();

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private static readonly string[] Kinds = ["predicate", "nan", "source", "maxrows", "cancel"];

    // source: §14.4.2 Errors of streamed parts; §14.8.5 step 8a
    [TestCaseSource(nameof(Kinds))]
    public async Task Streamed_FailuresBeforeTheFirstDrain_LeaveAnEmptyStream(string kind)
    {
        var (stream, _) = await Fail(kind, 3, 10);
        Assert.That(stream.Received, Is.EqualTo(0));
    }

    // source: §14.4.6 Failure bullet; §14.8.5 step 8a
    [TestCaseSource(nameof(Kinds))]
    public async Task Streamed_FailuresAfterADrain_LeaveATruncatedPrefix(string kind)
    {
        var (stream, _) = await Fail(kind, 600, 700);
        Assert.That(stream.Received, Is.GreaterThan(0));
        Assert.That(stream.Text, Does.StartWith("{\"contract\":1,\"group\":"));
        Assert.That(() => JsonDocument.Parse(stream.Bytes), Throws.InstanceOf<JsonException>());
    }

    // source: §14.4.2 Depth row; JsonDepth.cs (page writer at 65 accepts 60)
    [TestCase(60)]
    [TestCase(61)]
    public async Task Streamed_DepthParity(int depth)
    {
        JsonNode node = 1;
        for (var i = 0; i < depth; i++)
        {
            node = new JsonArray(node);
        }

        var handle = new ChartHandle<Nested>("n", Chart.Line(DeliveryJson.Default.Nested).X(r => r.Value).ToTemplate());
        Nested[] rows = [new(node)];
        var page = new DataPage(new ChartGroup("g", handle), [handle.Stream(AsyncRows.Of(rows))]);
        if (depth <= 60)
        {
            Assert.That(await Written(page), Is.EqualTo(await Written(new DataPage(new ChartGroup("g", handle), [await handle.RowsAsync(AsyncRows.Of(rows))]))));
        }
        else
        {
            Assert.That(async () => await Written(page), Throws.InstanceOf<InvalidOperationException>().Or.InstanceOf<JsonException>());
        }
    }

    // source: §14.4.1 argument contract
    [Test]
    public void Stream_Null_Throws() =>
        Assert.That(() => SalesHandle().Stream(null!), Throws.ArgumentNullException);
}
