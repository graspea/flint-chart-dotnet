using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Graspea.Flint.Delivery;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Delivery;

public sealed class ChartPartAsyncTests
{
    private static readonly Sale[] Sales =
    [
        new(new DateOnly(2025, 1, 1), "North", 1200.50m),
        new(new DateOnly(2025, 2, 1), "Coast <é+>\"", 980.00m),
    ];

    private static ChartTemplate<Sale> Template() =>
        Chart.Line(DeliveryJson.Default.Sale).X(r => r.Month).Y(r => r.Revenue).ToTemplate();

    private static ChartTemplate<Sale> RowOptionsTemplate() =>
        Chart.Of(ChartType.LineChart, Array.Empty<Sale>(), new RowOptions()).Encode(Channel.X, r => r.Month).Encode(Channel.Y, r => r.Revenue).ToTemplate();

    private static ChartTemplate<Note> PredicateTemplate()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            foreach (var property in info.Properties.Where(p => p.Name == "Text"))
            {
                property.ShouldSerialize = (_, value) => value is null;
            }
        });
        var typeInfo = (JsonTypeInfo<Note>)new JsonSerializerOptions { TypeInfoResolver = resolver }.GetTypeInfo(typeof(Note));
        return Chart.Line(typeInfo).X(r => r.City).ToTemplate();
    }

    private static string RowsText(ChartHandle handle, ChartPart part)
    {
        var page = new DataPage(new ChartGroup("g", handle), [part]);
        return JsonDocument.Parse(page.ToUtf8Bytes(), new JsonDocumentOptions { MaxDepth = 256 }).RootElement.GetProperty("charts").GetProperty(handle.Id).GetProperty("rows").GetRawText();
    }

    private static Sale Row(int i) => new(new DateOnly(2025, 1, 1).AddMonths(i), "North", i);

    // source: §14.8.5 step 4; Support/AsyncRows.cs
    [Test]
    public async Task RowsAsync_EqualsRows()
    {
        foreach (var template in new[] { Template(), RowOptionsTemplate() })
        {
            var handle = new ChartHandle<Sale>("revenue", template);
            Sale?[] withNull = [Sales[0], null, Sales[1]];
            foreach (var rows in new Sale?[][] { withNull, [], [Sales[1]] })
            {
                var expected = RowsText(handle, handle.Rows(rows!));
                Assert.That(RowsText(handle, await handle.RowsAsync(AsyncRows.Of<Sale>(rows!))), Is.EqualTo(expected));
                Assert.That(RowsText(handle, await handle.RowsAsync(AsyncRows.Iterate<Sale>(rows!))), Is.EqualTo(expected));
            }
        }
    }

    // source: §14.8.5 step 4; RowMaterializer.cs:87-104 (the same value-dependent predicate over an asynchronous source)
    [Test]
    public async Task RowsAsync_ValueDependentPredicate_ThrowsArgumentException()
    {
        var handle = new ChartHandle<Note>("notes", PredicateTemplate());
        Assert.That(await handle.RowsAsync(AsyncRows.Of(new[] { new Note("A", null) })), Is.Not.Null);
        Assert.That(async () => await handle.RowsAsync(AsyncRows.Of(new[] { new Note("A", null), new Note("B", "x") })),
            Throws.ArgumentException.With.Message.Contains("'Text'"));
    }

    // source: §14.8.5 step 5 (a bad excess row still gives the limit); RowLimit.cs:20-29
    [Test]
    public void RowsAsync_MaxRows_BadExcessRowGivesTheLimit()
    {
        var handle = new ChartHandle<Note>("notes", PredicateTemplate(), 1);
        Assert.That(async () => await handle.RowsAsync(AsyncRows.Of(new[] { new Note("A", null), new Note("B", "x") })), Throws.InstanceOf<ChartRowLimitException>());
    }

    // source: §14.4.2 Errors row (the token reaches the source); FlintJsonStreamingTests.cs:373-392
    [Test]
    public async Task RowsAsync_TokenReachesTheSource()
    {
        var handle = new ChartHandle<Sale>("revenue", Template());
        var source = AsyncRows.Of(Sales);
        using var cts = new CancellationTokenSource();
        await handle.RowsAsync(source, cts.Token);
        Assert.That(source.Token, Is.EqualTo(cts.Token));
    }

    // source: §14.8.5 step 4
    [Test]
    public async Task RowsAsync_CancelledMidway()
    {
        var handle = new ChartHandle<Sale>("revenue", Template());
        using var cts = new CancellationTokenSource();
        var source = AsyncRows.Of(Enumerable.Range(0, 10).Select(Row), i =>
        {
            if (i == 5)
            {
                cts.Cancel();
            }
        });
        Assert.That(async () => await handle.RowsAsync(source, cts.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(source.Disposals, Is.EqualTo(1));

        var again = await handle.RowsAsync(AsyncRows.Of(Sales));
        Assert.That(RowsText(handle, again), Does.StartWith("["));
    }

    private static async IAsyncEnumerable<Sale> EmptyIgnoringToken()
    {
        await Task.Yield();
        yield break;
    }

    private static async IAsyncEnumerable<Sale> CancelsAtTheEnd(CancellationTokenSource cts)
    {
        await Task.Yield();
        cts.Cancel();
        yield break;
    }

    // source: ChartHandleOfTRow.cs RowsAsync doc (cancelling throws OperationCanceledException and returns no part)
    [Test]
    public void RowsAsync_PreCancelledToken_ThrowsEvenForAnEmptySourceThatIgnoresIt()
    {
        var handle = new ChartHandle<Sale>("revenue", Template());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.That(async () => await handle.RowsAsync(EmptyIgnoringToken(), cts.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    // source: ChartHandleOfTRow.cs RowsAsync doc (cancelling throws OperationCanceledException and returns no part)
    [Test]
    public void RowsAsync_CancelledDuringTheFinalMoveNext_Throws()
    {
        var handle = new ChartHandle<Sale>("revenue", Template());
        using var cts = new CancellationTokenSource();
        Assert.That(async () => await handle.RowsAsync(CancelsAtTheEnd(cts), cts.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    // source: §14.4.2 Errors row; FlintJsonStreamingTests.cs:395-409
    [Test]
    public void RowsAsync_SourceException_IsUnwrapped()
    {
        var handle = new ChartHandle<Sale>("revenue", Template());
        var boom = new InvalidOperationException("boom");
        var source = AsyncRows.Of(Enumerable.Range(0, 10).Select(Row), i =>
        {
            if (i == 2)
            {
                throw boom;
            }
        });
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await handle.RowsAsync(source));
        Assert.That(ex, Is.SameAs(boom));
    }

    // source: §14.8.5 step 5; RowLimit.cs:20-29
    [Test]
    public async Task RowsAsync_MaxRows()
    {
        var handle = new ChartHandle<Sale>("revenue", Template(), 3);
        Assert.That(RowsText(handle, await handle.RowsAsync(AsyncRows.Of(Enumerable.Range(0, 3).Select(Row)))), Does.StartWith("["));

        var source = AsyncRows.Of(Enumerable.Range(0, 10).Select(Row));
        var ex = Assert.ThrowsAsync<ChartRowLimitException>(async () => await handle.RowsAsync(source))!;
        Assert.That(ex.Chart, Is.SameAs(handle));
        Assert.That(ex.Limit, Is.EqualTo(3));
        Assert.That(source.Yielded, Is.EqualTo(4));
    }

    // source: argument contract of §14.4.1
    [Test]
    public void RowsAsync_NullRows_Throws()
    {
        var handle = new ChartHandle<Sale>("revenue", Template());
        Assert.That(() => handle.RowsAsync(null!), Throws.TypeOf<ArgumentNullException>());
    }
}
