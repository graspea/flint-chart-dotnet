using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Rows;

public sealed partial class ChartDataTests
{
    private static readonly Sale[] Sales =
    [
        new(new DateOnly(2024, 1, 1), 1200.50m, null),
        new(new DateOnly(2024, 2, 1), 980m, "EU"),
    ];

    // source: design §13.4 (FromRowsAsync is FromRows over an asynchronous source: the same keys, nulls and JSON) and
    // §5.3 (camelCase by default, decimal written exactly, null written)
    [Test]
    public async Task FromRowsAsync_Record_WritesTheSameJsonAsFromRows()
    {
        var data = await ChartData.FromRowsAsync(AsyncRows.Of(Sales));
        Assert.That(data.Values!.ToJsonString(), Is.EqualTo(ChartData.FromRows(Sales).Values!.ToJsonString()));
        Assert.That(data.Values!.ToJsonString(), Is.EqualTo("""[{"month":"2024-01-01","revenue":1200.50,"region":null},{"month":"2024-02-01","revenue":980,"region":"EU"}]"""));
        Assert.That(data.FirstRowKeys, Is.EqualTo(new[] { "month", "revenue", "region" }));
    }

    // source: design §13.4 and §5.3 (verbatim names through RowOptions, metadata through JsonTypeInfo)
    [Test]
    public async Task FromRowsAsync_OptionsAndTypeInfo_WriteTheSameKeysAsFromRows()
    {
        var verbatim = new RowOptions { PropertyNamingPolicy = null };
        Assert.That((await ChartData.FromRowsAsync(AsyncRows.Of(Sales), verbatim)).FirstRowKeys, Is.EqualTo(new[] { "Month", "Revenue", "Region" }));
        var typed = await ChartData.FromRowsAsync(AsyncRows.Of([new GdpRow(1.5, "CZ")]), RowContext.Default.GdpRow);
        Assert.That(typed.Values!.ToJsonString(), Is.EqualTo("""[{"GDP per capita":1.5,"country":"CZ"}]"""));
    }

    // source: design §13.4 ("the same metadata checks before the first row") and §13.10 (ArgumentException when the
    // member is called, before any row is read)
    [Test]
    public void FromRowsAsync_TypeInfoThatDropsNulls_ThrowsBeforeReadingARow()
    {
        var source = AsyncRows.Of([new GdpRow(1, "CZ")]);
        Assert.That(() => ChartData.FromRowsAsync(source, NullDroppingContext.Default.GdpRow), Throws.ArgumentException);
        Assert.That(() => ChartData.FromRowsAsync(AsyncRows.Of([new NullDroppingRow(null, 1)])), Throws.ArgumentException);
        Assert.That(source.Enumerations, Is.Zero);
    }

    // source: design §13.10 (value-dependent omission: ArgumentException at the offending row) and review R1-2
    [Test]
    public void FromRowsAsync_ValueDependentShouldSerialize_ThrowsAtTheRow()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            foreach (var property in info.Properties.Where(p => p.Name == "Note"))
            {
                property.ShouldSerialize = (_, value) => value is null;
            }
        });
        var typeInfo = (JsonTypeInfo<NoteRow>)new JsonSerializerOptions { TypeInfoResolver = resolver }.GetTypeInfo(typeof(NoteRow));
        var source = AsyncRows.Of([new NoteRow("A", null), new NoteRow("A", "x"), new NoteRow("A", null)]);

        Assert.That(async () => await ChartData.FromRowsAsync(source, typeInfo), Throws.ArgumentException.With.Message.Contains("'Note'"));
        Assert.That(source.Yielded, Is.EqualTo(2));
    }

    // source: design §13.10 (the token reaches the source and is checked before each row; cancellation throws
    // OperationCanceledException and disposes the enumerator)
    [Test]
    public void FromRowsAsync_CancelledMidSequence_ThrowsAndDisposesTheSource()
    {
        using var cancellation = new CancellationTokenSource();
        var source = AsyncRows.Of(Enumerable.Repeat(Sales[0], 10), i =>
        {
            if (i == 3)
            {
                cancellation.Cancel();
            }
        });

        Assert.That(async () => await ChartData.FromRowsAsync(source, cancellationToken: cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(source.Token, Is.EqualTo(cancellation.Token));
        Assert.That(source.Yielded, Is.EqualTo(4));
        Assert.That(source.Disposals, Is.EqualTo(1));
    }

    // source: design §13.10 (source exceptions propagate unchanged, not wrapped)
    [Test]
    public void FromRowsAsync_SourceThrows_PropagatesTheException()
    {
        var failure = new IOException("page 2 failed");
        var source = AsyncRows.Of(Sales, i =>
        {
            if (i == 1)
            {
                throw failure;
            }
        });

        Assert.That(async () => await ChartData.FromRowsAsync(source), Throws.Exception.SameAs(failure));
    }

    // source: design §13.9 (materialised data enumerates the source once, before the task completes)
    [Test]
    public async Task FromRowsAsync_EnumeratesTheSourceOnce()
    {
        var source = AsyncRows.Of(Sales);
        var data = await ChartData.FromRowsAsync(source, RowContextSales.Default.Sale);
        Assert.That(source.Enumerations, Is.EqualTo(1));
        Assert.That(data.Values, Has.Count.EqualTo(2));
    }

    // source: design §13.4 (argument checks as for FromRows)
    [Test]
    public void FromRowsAsync_NullArguments_Throw()
    {
        Assert.That(() => ChartData.FromRowsAsync<Sale>(null!), Throws.ArgumentNullException);
        Assert.That(() => ChartData.FromRowsAsync(AsyncRows.Of(Sales), (JsonTypeInfo<Sale>)null!), Throws.ArgumentNullException);
    }
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
[System.Text.Json.Serialization.JsonSerializable(typeof(Sale))]
internal sealed partial class RowContextSales : System.Text.Json.Serialization.JsonSerializerContext;
