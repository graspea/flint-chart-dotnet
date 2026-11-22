using System.Text.Json.Nodes;
using Graspea.Flint.Validation;
using Graspea.Flint.Validation.Rules;

namespace Graspea.Flint.Tests.Validation;

public sealed partial class ChartValidatorTests
{
    /// <summary>A Map of two points, or a Choropleth of two countries, with one chart property.</summary>
    private static ChartAssemblyInput Geo(string chartType, string key, JsonNode? value) => new()
    {
        Data = chartType == "Map"
            ? ChartData.FromJson([new JsonObject { ["lon"] = 10, ["lat"] = 50 }, new JsonObject { ["lon"] = 14, ["lat"] = 52 }])
            : ChartData.FromJson([new JsonObject { ["country"] = "France", ["revenue"] = 10 }, new JsonObject { ["country"] = "Spain", ["revenue"] = 20 }]),
        ChartSpec = new ChartSpec
        {
            ChartType = new ChartType(chartType),
            Encodings = chartType == "Map"
                ? new() { ["longitude"] = "lon", ["latitude"] = "lat" }
                : new() { ["id"] = "country", ["color"] = "revenue" },
            ChartProperties = new() { [key] = value },
        },
    };

    // source: flint-chart 1.1.1 src/core/normalize-properties.ts:63-65 (a discrete value outside the options is kept when
    // the def's accepts holds for it), src/vegalite/templates/map.ts:428-441 and :918-932 (boundaries accepts isBoundaryFile,
    // mapStyle accepts isCustomMapStyle, on the Vega-Lite Map and Choropleth), src/chart-types/boundaries.ts:63-80
    // (isBoundaryFile: an object, a non-blank url (String.prototype.trim) or one per detail, or object values, not both;
    // feature, ids, key, disputed and attribution each absent (undefined, not null) or well-formed; other members allowed) and
    // src/vegalite/templates/map-style.ts:55, :74 and :78-84 (isCustomMapStyle: an object with at least one member, each base
    // one of MAP_STYLES or a colour member of CUSTOM_COLORS with a non-blank string); probe cases "map with a boundary file",
    // "map with a boundary file of unknown ids", "map with a map style of its own", "map with an empty map style" and their
    // choropleth twins of tests/fixtures/flint-chart@1.1.1 (kept, or dropped with invalid-option-value, on vegalite)
    [TestCase("boundaries", "{\"url\":\"https://example.com/regions.json\"}", true)]
    [TestCase("boundaries", "{\"url\":\"\"}", false)]
    [TestCase("boundaries", "{\"url\":\"  \"}", false)]
    [TestCase("boundaries", "{\"url\":\"\\u00a0\\u2028\\ufeff\"}", false)]
    [TestCase("boundaries", "{\"url\":5}", false)]
    [TestCase("boundaries", "{\"url\":{\"coarse\":\"coarse.json\"}}", true)]
    [TestCase("boundaries", "{\"url\":{\"medium\":\"medium.json\"}}", true)]
    [TestCase("boundaries", "{\"url\":{\"coarse\":\"coarse.json\",\"medium\":\"medium.json\"}}", true)]
    [TestCase("boundaries", "{\"url\":{\"coarse\":\"coarse.json\",\"medium\":5}}", false)]
    [TestCase("boundaries", "{\"url\":{\"coarse\":\"coarse.json\",\"medium\":null}}", false)]
    [TestCase("boundaries", "{\"url\":{\"coarse\":\" \",\"medium\":\"medium.json\"}}", false)]
    [TestCase("boundaries", "{\"url\":{\"fine\":\"fine.json\"}}", false)]
    [TestCase("boundaries", "{\"url\":{}}", false)]
    [TestCase("boundaries", "{\"url\":[\"regions.json\"]}", false)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"values\":{}}", false)]
    [TestCase("boundaries", "{\"values\":{\"type\":\"Topology\",\"objects\":{},\"arcs\":[]},\"feature\":\"countries\"}", true)]
    [TestCase("boundaries", "{\"values\":{}}", true)]
    [TestCase("boundaries", "{\"values\":[]}", false)]
    [TestCase("boundaries", "{\"values\":\"regions\"}", false)]
    [TestCase("boundaries", "{\"values\":null,\"url\":\"regions.json\"}", true)]
    [TestCase("boundaries", "{\"feature\":\"countries\"}", false)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"feature\":null}", false)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"feature\":5}", false)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"feature\":\"\"}", true)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"ids\":\"iso-numeric\"}", true)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"ids\":\"iso3\"}", true)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"ids\":\"iso2\"}", true)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"ids\":\"fips\"}", true)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"ids\":\"as-is\"}", true)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"ids\":\"ISO3\"}", false)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"ids\":\"state-codes\"}", false)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"ids\":null}", false)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"key\":\"name\"}", true)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"key\":\" \"}", true)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"key\":\"\"}", false)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"key\":null}", false)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"key\":5}", false)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"disputed\":\"lines\"}", true)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"disputed\":null}", false)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"disputed\":true}", false)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"attribution\":\"Regions of the sample\"}", true)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"attribution\":{\"text\":\"Regions of the sample\"}}", true)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"attribution\":{\"text\":\"\",\"href\":5}}", true)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"attribution\":{}}", false)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"attribution\":{\"text\":5}}", false)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"attribution\":[]}", false)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"attribution\":null}", false)]
    [TestCase("boundaries", "{\"url\":\"regions.json\",\"projection\":\"mercator\",\"note\":null}", true)]
    [TestCase("boundaries", "[]", false)]
    [TestCase("boundaries", "5", false)]
    [TestCase("mapStyle", "{\"base\":\"dark\",\"sea\":\"#000000\"}", true)]
    [TestCase("mapStyle", "{\"land\":\"#eeeeee\"}", true)]
    [TestCase("mapStyle", "{\"base\":\"minimal\"}", true)]
    [TestCase("mapStyle", "{\"sea\":\"#e0f0ff\",\"land\":\"#f6f6f4\",\"coast\":\"#999999\",\"border\":\"#ffffff\",\"noData\":\"#dddddd\",\"graticule\":\"#eeeeee\",\"disputed\":\"#888888\"}", true)]
    [TestCase("mapStyle", "{}", false)]
    [TestCase("mapStyle", "{\"sea\":\"  \"}", false)]
    [TestCase("mapStyle", "{\"sea\":\"\"}", false)]
    [TestCase("mapStyle", "{\"sea\":5}", false)]
    [TestCase("mapStyle", "{\"sea\":null}", false)]
    [TestCase("mapStyle", "{\"base\":\"nope\"}", false)]
    [TestCase("mapStyle", "{\"base\":\"Dark\"}", false)]
    [TestCase("mapStyle", "{\"base\":null}", false)]
    [TestCase("mapStyle", "{\"base\":\"dark\",\"water\":\"#000000\"}", false)]
    [TestCase("mapStyle", "{\"base\":\"dark\",\"sea\":\"#000000\",\"rim\":\"#111111\"}", false)]
    [TestCase("mapStyle", "[]", false)]
    [TestCase("mapStyle", "[\"dark\"]", false)]
    public void Warnings_InvalidOptionValue_KeepsWhatAnAcceptsPredicateTakes(string key, string json, bool kept)
    {
        using (Assert.EnterMultipleScope())
        {
            foreach (var chartType in new[] { "Map", "Choropleth" })
            {
                foreach (var backend in new FlintBackend?[] { FlintBackend.VegaLite, null })
                {
                    var issues = Issues(Geo(chartType, key, JsonNode.Parse(json)), backend, "invalid_option_value").ToList();
                    Assert.That(issues, kept ? Is.Empty : Has.Count.EqualTo(1), $"{chartType}, {backend?.ToString() ?? "no backend"}");
                }
            }
        }
    }

    // source: flint-chart 1.1.1 src/core/normalize-properties.ts:97 (the invalid-option-value warning appends ", or
    // <acceptsLabel>" when the def has a label) and the acceptsLabel of Map boundaries and mapStyle on vegalite in
    // ir/flint-ir.json; the Plotly Map declares neither key, so it reports unknown_chart_property and never
    // invalid_option_value (no Plotly template has an accepts predicate)
    [Test]
    public void Warnings_InvalidOptionValue_NamesWhatThePredicateAccepts()
    {
        using (Assert.EnterMultipleScope())
        {
            foreach (var key in new[] { "boundaries", "mapStyle" })
            {
                var label = IrProperty("Map", FlintBackend.VegaLite, key)!["acceptsLabel"]!.GetValue<string>();
                var issue = Issues(Geo("Map", key, new JsonObject { ["wrong"] = 1 }), FlintBackend.VegaLite, "invalid_option_value").Single();
                Assert.That(issue.Message, Is.EqualTo($"{{\"wrong\":1}} is not an option of \"{key}\" on vegalite, or {label}; upstream drops it (invalid-option-value) and uses the default."), key);
                Assert.That(Issues(Geo("Map", key, new JsonObject { ["wrong"] = 1 }), FlintBackend.Plotly, "invalid_option_value"), Is.Empty, $"{key} on plotly");
                Assert.That(IrProperty("Map", FlintBackend.Plotly, key), Is.Null, $"{key} is not a Plotly property");
            }

            // A property without a predicate keeps the message it had.
            Assert.That(Issues(Line(i => i.ChartSpec.ChartProperties = new() { ["interpolate"] = "zzz" }), FlintBackend.VegaLite, "invalid_option_value").Single().Message,
                Is.EqualTo("\"zzz\" is not an option of \"interpolate\" on vegalite; upstream drops it (invalid-option-value) and uses the default."));
        }
    }

    // source: flint-chart 1.1.1 src/core/normalize-properties.ts:65 (def.accepts?.(value): the predicate is the def's, so a
    // template whose def of the same key declares none drops the value) and docs/generator.md "Accepting properties" (the
    // validator applies a predicate only where the row carries AcceptsLabel, empty when the def has no label)
    [Test]
    public void Accepts_AppliesOnlyWhereTheRowCarriesTheLabel()
    {
        var file = JsonNode.Parse("{\"url\":\"https://example.com/regions.json\"}")!;
        var style = JsonNode.Parse("{\"base\":\"dark\"}")!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Accepts.Keeps(new PropertyRow("boundaries", PropertyKind.Discrete, AcceptsLabel: "a file"), file), Is.True);
            Assert.That(Accepts.Keeps(new PropertyRow("boundaries", PropertyKind.Discrete, AcceptsLabel: string.Empty), file), Is.True, "a predicate without a label");
            Assert.That(Accepts.Keeps(new PropertyRow("boundaries", PropertyKind.Discrete), file), Is.False, "a def without a predicate");
            Assert.That(Accepts.Keeps(new PropertyRow("mapStyle", PropertyKind.Discrete, AcceptsLabel: "a style"), style), Is.True);
            Assert.That(Accepts.Keeps(new PropertyRow("mapStyle", PropertyKind.Discrete), style), Is.False, "a def without a predicate");
            Assert.That(Accepts.Keeps(new PropertyRow("mapStyle", PropertyKind.Discrete, AcceptsLabel: "a style"), file), Is.False, "each key its own predicate");
            Assert.That(Accepts.Keeps(new PropertyRow("projection", PropertyKind.Discrete, AcceptsLabel: "a projection"), file), Is.False, "a key without a ported predicate");
        }
    }
}
