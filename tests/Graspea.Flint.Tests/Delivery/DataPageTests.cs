using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Graspea.Flint.Delivery;
using Graspea.Flint.Serialization;

namespace Graspea.Flint.Tests.Delivery;

public sealed class DataPageTests
{
    private static ChartHandle<Sale> Handle(string id, int? maxRows = null) =>
        new(id, Chart.Line(DeliveryJson.Default.Sale).X(r => r.Month).Y(r => r.Revenue).ToTemplate(), maxRows);

    private static Sale[] Sales(int count, int offset = 0) =>
        [.. Enumerable.Range(offset, count).Select(i => new Sale(new DateOnly(2025, 1, 1).AddMonths(i), "North", 100 + i))];

    // source: §14.4.1 DataPage summary; §14.5 rule 4 (empty next)
    [Test]
    public void Invariants()
    {
        var a = Handle("revenue");
        var b = Handle("traffic");
        var group = new ChartGroup("overview", a, b);
        var outside = Handle("revenue");

        Assert.That(() => new DataPage(group, [outside.Withheld("x"), b.Withheld("x")]), Throws.ArgumentException);
        Assert.That(() => new DataPage(group, [a.Withheld("x")]), Throws.ArgumentException);
        Assert.That(() => new DataPage(group, [a.Withheld("x"), a.Withheld("y"), b.Withheld("x")]), Throws.ArgumentException);
        Assert.That(() => new DataPage(group, [a.Withheld("x"), null!]), Throws.ArgumentException);
        Assert.That(() => new DataPage(group, null!), Throws.ArgumentNullException);
        Assert.That(() => new DataPage(null!, [a.Withheld("x")]), Throws.ArgumentNullException);
        Assert.That(() => new DataPage(group, [a.Withheld("x"), b.Withheld("x")], ""), Throws.ArgumentException);

        var page = new DataPage(group, [b.Withheld("x"), a.Rows(Sales(1))], "p2");
        Assert.That(page.Group, Is.SameAs(group));
        Assert.That(page.Next, Is.EqualTo("p2"));
        Assert.That(page.Charts.Select(c => c.Chart), Is.EqualTo(new[] { a, b }).AsCollection);
        var doc = JsonNode.Parse(page.ToUtf8Bytes())!["charts"]!.AsObject();
        Assert.That(doc.Select(p => p.Key), Is.EqualTo(new[] { "revenue", "traffic" }));
    }

    // source: §14.4.1 DataPage snapshot (copies its parts list and exposes a read-only view)
    [Test]
    public void Charts_IsASnapshotAndReadOnly()
    {
        var a = Handle("revenue");
        var group = new ChartGroup("g", a);
        var parts = new List<ChartPart> { a.Withheld("x") };
        var page = new DataPage(group, parts);
        parts.Clear();
        Assert.That(page.Charts, Has.Count.EqualTo(1));
        Assert.That(page.Charts as IList<ChartPart>, Has.Property("IsReadOnly").True);
        Assert.That(page.Charts, Is.Not.InstanceOf<List<ChartPart>>());
    }

    // source: §14.5 page document and rules 4-5
    [Test]
    public void Document()
    {
        var revenue = Handle("revenue");
        var traffic = Handle("traffic");
        var group = new ChartGroup("overview", revenue, traffic);
        var page = new DataPage(group, [revenue.Rows([new Sale(new DateOnly(2025, 1, 1), "North", 1200.50m)]), traffic.Withheld("restricted")], "p2");
        Assert.That(page.ToJson(), Is.EqualTo(
            """{"contract":1,"group":"overview","charts":{"revenue":{"rows":[{"month":"2025-01-01","region":"North","revenue":1200.50}]},"traffic":{"withheld":"restricted"}},"next":"p2"}"""));
        var last = new DataPage(group, [revenue.Withheld("a"), traffic.Withheld("b")]);
        Assert.That(last.ToJson(), Does.Not.Contain("next"));
    }

    // source: §14.4.2 Byte identity (indented: JSON-equal); §14.8.5 step 7
    [Test]
    public void FinishersAgree()
    {
        var revenue = Handle("revenue");
        var group = new ChartGroup("overview", revenue);
        var page = new DataPage(group, [revenue.Rows(Sales(3))], "n");
        var bytes = page.ToUtf8Bytes();
        Assert.That(page.ToUtf8Bytes(), Is.EqualTo(bytes));
        Assert.That(Encoding.UTF8.GetBytes(page.ToJson()), Is.EqualTo(bytes));
        var sink = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(sink))
        {
            page.WriteTo(writer);
        }

        Assert.That(sink.WrittenSpan.ToArray(), Is.EqualTo(bytes));

        var indented = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(indented, new JsonWriterOptions { Indented = true }))
        {
            page.WriteTo(writer);
        }

        Assert.That(JsonNode.DeepEquals(JsonNode.Parse(indented.WrittenSpan), JsonNode.Parse(page.ToJson())), Is.True);
        Assert.That(() => page.WriteTo(null!), Throws.ArgumentNullException);
    }

    // source: §14.8.5 step 7, §14.5 rule 5
    [Test]
    public void UnevenPages_ConcatenateToBindValues()
    {
        var revenue = Handle("revenue");
        var group = new ChartGroup("g", revenue);
        var all = Sales(5);
        var pages = new[] { all[..2], all[2..2], all[2..] };
        var joined = new JsonArray();
        foreach (var rows in pages)
        {
            var page = new DataPage(group, [revenue.Rows(rows)]);
            foreach (var row in JsonNode.Parse(page.ToUtf8Bytes())!["charts"]!["revenue"]!["rows"]!.AsArray())
            {
                joined.Add(row!.DeepClone());
            }
        }

        var expected = JsonNode.Parse(FlintJson.SerializeToUtf8Bytes(revenue.Template.Bind(all)))!["data"]!["values"]!;
        Assert.That(joined.ToJsonString(), Is.EqualTo(expected.ToJsonString()));
    }
}
