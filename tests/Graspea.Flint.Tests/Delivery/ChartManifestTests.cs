using System.Collections;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Graspea.Flint.Delivery;

namespace Graspea.Flint.Tests.Delivery;

public sealed class ChartManifestTests
{
    private static ChartTemplate<Sale> SaleTemplate() =>
        Chart.Line(DeliveryJson.Default.Sale).X(r => r.Month).Y(r => r.Revenue).ToTemplate();

    private static ChartTemplate<Visit> VisitTemplate() =>
        Chart.Bar(DeliveryJson.Default.Visit).X(r => r.Source).Y(r => r.Visits).ToTemplate();

    private static ChartHandle SaleHandle(string id) => new ChartHandle<Sale>(id, SaleTemplate());

    private static ChartManifest TwoGroups(ChartHandle? revenue = null, ChartHandle? traffic = null, ChartHandle? stock = null) =>
        ChartManifest.Create(
            new ChartGroup("overview", revenue ?? SaleHandle("revenue"), traffic ?? new ChartHandle<Visit>("traffic", VisitTemplate())),
            new ChartGroup("inventory", stock ?? SaleHandle("stock")));

    private static string Expected() =>
        """{"contract":1,"flint":"@VERSION@","charts":{"revenue":{"input":{"chart_spec":{"chartType":"Line Chart","encodings":{"x":{"field":"month"},"y":{"field":"revenue"}}}}},"traffic":{"input":{"chart_spec":{"chartType":"Bar Chart","encodings":{"x":{"field":"source"},"y":{"field":"visits"}}}}},"stock":{"input":{"chart_spec":{"chartType":"Line Chart","encodings":{"x":{"field":"month"},"y":{"field":"revenue"}}}}}},"groups":{"overview":["revenue","traffic"],"inventory":["stock"]}}""".Replace("@VERSION@", FlintUpstream.Version);

    // source: §14.4.1 ChartManifest summary; §14.5 rule 3 (zero groups)
    [Test]
    public void Create_Invariants()
    {
        Assert.That(() => ChartManifest.Create(), Throws.ArgumentException);
        Assert.That(() => ChartManifest.Create(null!), Throws.ArgumentNullException);
        Assert.That(() => ChartManifest.Create(new ChartGroup("a", SaleHandle("x")), null!), Throws.ArgumentException);
        Assert.That(() => ChartManifest.Create(new ChartGroup("a", SaleHandle("x")), new ChartGroup("a", SaleHandle("y"))), Throws.ArgumentException);
        Assert.That(() => ChartManifest.Create(new ChartGroup("a", SaleHandle("x")), new ChartGroup("b", SaleHandle("x"))), Throws.ArgumentException);

        var shared = SaleHandle("x");
        var manifest = ChartManifest.Create(new ChartGroup("a", shared), new ChartGroup("b", shared, SaleHandle("y")));
        var doc = JsonDocument.Parse(manifest.Utf8Bytes);
        Assert.That(doc.RootElement.GetProperty("charts").EnumerateObject().Select(p => p.Name), Is.EqualTo(new[] { "x", "y" }));
        Assert.That(doc.RootElement.GetProperty("groups").GetProperty("a").EnumerateArray().Select(e => e.GetString()), Is.EqualTo(new[] { "x" }));
        Assert.That(doc.RootElement.GetProperty("groups").GetProperty("b").EnumerateArray().Select(e => e.GetString()), Is.EqualTo(new[] { "x", "y" }));
        Assert.That(manifest.Groups.Select(g => g.Name), Is.EqualTo(new[] { "a", "b" }));
    }

    // source: §14.5 manifest document and member order; §14.4.2 Byte identity and Hash
    [Test]
    public void Bytes_ArePinned()
    {
        var manifest = TwoGroups();
        Assert.That(Encoding.UTF8.GetString(manifest.Utf8Bytes.Span), Is.EqualTo(Expected()));
        var charts = JsonDocument.Parse(manifest.Utf8Bytes).RootElement.GetProperty("charts");
        Assert.That(charts.GetProperty("revenue").GetProperty("input").GetRawText(), Is.EqualTo(SaleTemplate().ToJson()));
        Assert.That(charts.GetProperty("traffic").GetProperty("input").GetRawText(), Is.EqualTo(VisitTemplate().ToJson()));
        Assert.That(charts.GetProperty("stock").GetProperty("input").GetRawText(), Is.EqualTo(SaleTemplate().ToJson()));
    }

    // source: §14.4.1 (lowercase hex SHA-256 of Utf8Bytes); §14.4.2 AOT row (ToHexString is uppercase on net8.0)
    [Test]
    public void Hash_IsLowercaseSha256()
    {
        var manifest = TwoGroups();
        Assert.That(manifest.Hash, Is.EqualTo(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Expected()))).ToLowerInvariant()));
        Assert.That(manifest.Hash, Does.Match("^[0-9a-f]{64}$"));
    }

    // source: §14.4.2 Hash row (equal definitions give an equal hash)
    [Test]
    public void FreshTemplates_GiveEqualBytes()
    {
        var withOptions = Chart.Of(ChartType.LineChart, Array.Empty<Sale>(), new RowOptions())
            .Encode(Channel.X, r => r.Month).Encode(Channel.Y, r => r.Revenue).ToTemplate();
        var a = ChartManifest.Create(new ChartGroup("g", new ChartHandle<Sale>("s", SaleTemplate())));
        var b = ChartManifest.Create(new ChartGroup("g", new ChartHandle<Sale>("s", withOptions)));
        Assert.That(b.Utf8Bytes.ToArray(), Is.EqualTo(a.Utf8Bytes.ToArray()));
        Assert.That(b.Hash, Is.EqualTo(a.Hash));
    }

    // source: §14.4.1 (WriteTo writes the stored bytes as one raw value); §14.4.5 WebSocket sample
    [Test]
    public void Finishers()
    {
        var manifest = TwoGroups();
        Assert.That(manifest.ToJson(), Is.EqualTo(Encoding.UTF8.GetString(manifest.Utf8Bytes.Span)));
        Assert.That(() => manifest.WriteTo(null!), Throws.ArgumentNullException);

        foreach (var indented in new[] { false, true })
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", 1);
                writer.WritePropertyName("result");
                manifest.WriteTo(writer);
                writer.WriteEndObject();
            }

            var outer = JsonNode.Parse(stream.ToArray())!;
            Assert.That(JsonNode.DeepEquals(outer["result"], JsonNode.Parse(manifest.ToJson())), Is.True);
            Assert.That((int)outer["id"]!, Is.EqualTo(1));
        }
    }

    // source: §14.4.1 ChartManifest snapshot (Groups is a copied read-only view)
    [Test]
    public void Groups_IsASnapshot()
    {
        var a = new ChartGroup("a", SaleHandle("x"));
        var input = new[] { a };
        var manifest = ChartManifest.Create(input);
        input[0] = null!;
        Assert.That(manifest.Groups[0], Is.SameAs(a));
        Assert.That(manifest.Groups as ChartGroup[], Is.Null);
        Assert.That(manifest.Groups as IList, Is.Not.Null.And.Property("IsReadOnly").True);
        Assert.That(() => ((IList)manifest.Groups)[0] = a, Throws.InstanceOf<NotSupportedException>());
    }
}
