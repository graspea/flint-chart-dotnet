using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Graspea.Flint.Delivery;
using Graspea.Flint.Serialization;

namespace Graspea.Flint.Tests.Delivery;

public sealed class ChartPartTests
{
    private static readonly Sale[] Sales =
    [
        new(new DateOnly(2025, 1, 1), "North", 1200.50m),
        new(new DateOnly(2025, 2, 1), "Coast <é+>\"", 980.00m),
    ];

    private static ChartTemplate<Sale> JsonTypeInfoTemplate() =>
        Chart.Line(DeliveryJson.Default.Sale).X(r => r.Month).Y(r => r.Revenue).ToTemplate();

    private static ChartTemplate<Sale> RowOptionsTemplate() =>
        Chart.Of(ChartType.LineChart, Array.Empty<Sale>(), new RowOptions()).Encode(Channel.X, r => r.Month).Encode(Channel.Y, r => r.Revenue).ToTemplate();

    private static string RowsText(ChartHandle handle, ChartPart part)
    {
        var page = new DataPage(new ChartGroup("g", handle), [part]);
        return JsonDocument.Parse(page.ToUtf8Bytes(), new JsonDocumentOptions { MaxDepth = 256 }).RootElement.GetProperty("charts").GetProperty(handle.Id).GetProperty("rows").GetRawText();
    }

    private static string BindValues<T>(ChartTemplate<T> template, IEnumerable<T> rows) =>
        JsonDocument.Parse(FlintJson.SerializeToUtf8Bytes(template.Bind(rows))).RootElement.GetProperty("data").GetProperty("values").GetRawText();

    // source: design §14.4.2 Byte identity; FlintJsonStreamingTests.cs:84-123 pattern
    [Test]
    public void Rows_EqualBindValues()
    {
        foreach (var template in new[] { JsonTypeInfoTemplate(), RowOptionsTemplate() })
        {
            var handle = new ChartHandle<Sale>("revenue", template);
            Sale?[] withNull = [Sales[0], null, Sales[1]];
            foreach (var rows in new Sale?[][] { withNull, [], [Sales[1]] })
            {
                Assert.That(RowsText(handle, handle.Rows(rows!)), Is.EqualTo(BindValues<Sale>(template, rows!)));
            }
        }
    }

    // source: RowMaterializer.cs:87-104; FlintJsonStreamingTests.cs:412-429
    [Test]
    public void Rows_ValueDependentPredicate_ThrowsArgumentException()
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
        var template = Chart.Line(typeInfo).X(r => r.City).ToTemplate();
        var handle = new ChartHandle<Note>("notes", template);
        Assert.That(handle.Rows([new Note("A", null)]), Is.Not.Null);
        Assert.That(() => handle.Rows([new Note("A", null), new Note("B", "x")]),
            Throws.ArgumentException.With.Message.Contains("'Text'"));
    }

    // source: §14.4.2 Errors row; ChartData.cs:64-67; docs/row-data.md:58; ChartDataTests.cs:114-119
    [Test]
    public void Rows_NonFiniteDouble()
    {
        Reading[] rows = [new("A", double.NaN)];
        var strict = new ChartHandle<Reading>("r", Chart.Line(DeliveryJson.Default.Reading).X(r => r.Station).Y(r => r.Value).ToTemplate());
        Assert.That(() => strict.Rows(rows), Throws.ArgumentException);

        var template = Chart.Of(ChartType.LineChart, Array.Empty<Reading>(), new RowOptions()).Encode(Channel.X, r => r.Station).Encode(Channel.Y, r => r.Value).ToTemplate();
        var handle = new ChartHandle<Reading>("r", template);
        Assert.That(RowsText(handle, handle.Rows(rows)), Is.EqualTo(BindValues(template, rows)));
        Assert.That(RowsText(handle, handle.Rows(rows)), Does.Contain("null"));
    }

    // source: §14.4.1 ChartRowLimitException summary (at row MaxRows + 1, before any byte); §14.4.2 MaxRows row
    [Test]
    public void Rows_MaxRows()
    {
        var handle = new ChartHandle<Sale>("revenue", JsonTypeInfoTemplate(), 3);
        Sale Row(int i) => new(new DateOnly(2025, 1, 1).AddMonths(i), "North", i);
        Assert.That(RowsText(handle, handle.Rows(Enumerable.Range(0, 3).Select(Row))), Does.StartWith("["));

        var ex = Assert.Throws<ChartRowLimitException>(() => handle.Rows(Enumerable.Range(0, 4).Select(Row)))!;
        Assert.That(ex.Chart, Is.SameAs(handle));
        Assert.That(ex.Limit, Is.EqualTo(3));
        Assert.That(ex, Is.InstanceOf<InvalidOperationException>());

        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            foreach (var property in info.Properties.Where(p => p.Name == "Text"))
            {
                property.ShouldSerialize = (_, value) => value is null;
            }
        });
        var typeInfo = (JsonTypeInfo<Note>)new JsonSerializerOptions { TypeInfoResolver = resolver }.GetTypeInfo(typeof(Note));
        var notes = new ChartHandle<Note>("n", Chart.Line(typeInfo).X(r => r.City).ToTemplate(), 1);
        Assert.That(() => notes.Rows([new Note("A", null), new Note("B", "x")]), Throws.TypeOf<ChartRowLimitException>());

        var counted = new ChartHandle<Counted>("c", Chart.Line(DeliveryJson.Default.Counted).X(r => r.City).ToTemplate(), 1);
        Counted.Reads = 0;
        Assert.That(() => counted.Rows([new Counted(), new Counted()]), Throws.TypeOf<ChartRowLimitException>());
        Assert.That(Counted.Reads, Is.EqualTo(1));
    }

    // source: RowLimit.cs:20-29 (count == max is compared before incrementing; the excess row is never serialised)
    [Test]
    public void Rows_MaxRowsIntMax_IsAccepted()
    {
        var handle = new ChartHandle<Sale>("revenue", JsonTypeInfoTemplate(), int.MaxValue);
        Assert.That(RowsText(handle, handle.Rows(Sales)), Does.StartWith("["));
    }

    // source: §14.4.2 Depth row; JsonDepth.cs (data.values last accepted 60)
    [TestCase(60)]
    [TestCase(61)]
    public void Rows_DepthParity(int depth)
    {
        JsonNode node = 1;
        for (var i = 0; i < depth; i++)
        {
            node = new JsonArray(node);
        }

        var template = Chart.Line(DeliveryJson.Default.Nested).X(r => r.Value).ToTemplate();
        var handle = new ChartHandle<Nested>("n", template);
        Nested[] rows = [new(node)];
        if (depth <= 60)
        {
            Assert.That(RowsText(handle, handle.Rows(rows)), Is.EqualTo(BindValues(template, rows)));
        }
        else
        {
            Assert.That(() => template.Bind(rows), Throws.Nothing);
            Assert.That(() => FlintJson.SerializeToUtf8Bytes(template.Bind(rows)), Throws.InstanceOf<JsonException>());
            Assert.That(() => handle.Rows(rows), Throws.InstanceOf<InvalidOperationException>().Or.InstanceOf<JsonException>());
        }
    }

    // source: §14.4.2 Byte identity (a converter that writes raw JSON: equal to the streamed bytes, JSON-equal to Bind)
    [Test]
    public void Rows_RawWritingConverter_EqualsStreamedBytes()
    {
        var template = Chart.Line(DeliveryJson.Default.RawRow).ToTemplate();
        var handle = new ChartHandle<RawRow>("raw", template);
        RawRow[] rows = [new(1), new(2)];
        var streamed = JsonDocument.Parse(FlintJson.SerializeToUtf8Bytes(template.BindStream(rows))).RootElement.GetProperty("data").GetProperty("values").GetRawText();
        var text = RowsText(handle, handle.Rows(rows));
        Assert.That(text, Is.EqualTo(streamed));
        Assert.That(JsonNode.DeepEquals(JsonNode.Parse(text), JsonNode.Parse(BindValues(template, rows))), Is.True);
    }

    // source: §14.4.2 Errors row (null source)
    [Test]
    public void Rows_Null_Throws() =>
        Assert.That(() => new ChartHandle<Sale>("a", JsonTypeInfoTemplate()).Rows(null!), Throws.ArgumentNullException);
}
