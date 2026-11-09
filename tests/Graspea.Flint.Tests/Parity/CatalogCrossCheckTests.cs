using System.Text.Json.Nodes;
using Graspea.Flint.Tests.Support;
using Graspea.Flint.Validation;

namespace Graspea.Flint.Tests.Parity;

/// <summary>
/// The compiled catalog tables against the committed IR, read through the validator and the structs (design §10.1
/// rule 4, §10.4 test 15). Both sides come from the IR, so a failure here is an emitter bug (§9.3).
/// The native channels read though undeclared come from the probe record (CommittedIr.Reads) and are checked per base by ChannelReadsTests.
/// The layout channels read though undeclared are listed here (LayoutReadsUndeclared); ChannelReadsTests checks per base that
/// the validator reports every undeclared channel the layout assembler ignores.
/// </summary>
public sealed class CatalogCrossCheckTests
{
    private static readonly string[] BackendIds = ["vegalite", "echarts", "chartjs", "plotly", "excel", "image-charts"];

    private static FlintBackend Backend(string id) => (FlintBackend)Array.IndexOf(BackendIds, id);

    private static ChartAssemblyInput Document(string chartType, Action<ChartSpec>? change = null)
    {
        var spec = new ChartSpec { ChartType = new ChartType(chartType), Encodings = { ["x"] = "a" } };
        change?.Invoke(spec);
        return new ChartAssemblyInput
        {
            Data = ChartData.FromJson([new JsonObject { ["a"] = "p", ["b"] = 1 }, new JsonObject { ["a"] = "q", ["b"] = 2 }]),
            ChartSpec = spec,
        };
    }

    private static bool Has(ValidationResult result, string code, string? channel = null) =>
        result.Issues.Any(i => i.Code == code && (channel is null || i.Channel?.Name == channel));

    /// <summary>The upstream version the layout exemptions (LayoutReads: the facets, the Pyramid group and the four ECharts
    /// facets that stay reported) were reviewed against, by rendering.</summary>
    private const string LayoutReadsReviewedVersion = "1.0.1";

    /// <summary>The (backend, template, channel) a layout assembler draws from a bound field though the template does not
    /// declare the channel, in flint-chart 1.0.1, on a document that binds only x: column and row facet the chart, one panel
    /// per value, each in its own grid or subplot. ECharts also facets a Calendar Heatmap, Funnel Chart, Network Graph and
    /// Sankey Diagram, and they stay reported: the faceted option does not render the first and draws every panel's chart in
    /// the same place on the others (src/echarts/facet.ts:353-359, :424-428). The group of an ECharts or Plotly Pyramid Chart
    /// is read only beside x and y (ChartValidatorTests.Layout_GroupOfAPyramid_IsTheSplitWithoutAColorField).</summary>
    private static readonly HashSet<(string, string, string)> LayoutReadsUndeclared =
    [
        ("vegalite", "Ranged Dot Plot", "column"), ("vegalite", "Ranged Dot Plot", "row"),
        ("echarts", "Pyramid Chart", "column"), ("echarts", "Pyramid Chart", "row"),
        ("echarts", "Ranged Dot Plot", "column"), ("echarts", "Ranged Dot Plot", "row"),
        ("plotly", "Funnel Chart", "column"), ("plotly", "Funnel Chart", "row"),
        ("plotly", "Pyramid Chart", "column"), ("plotly", "Pyramid Chart", "row"),
        ("plotly", "Ranged Dot Plot", "column"), ("plotly", "Ranged Dot Plot", "row"),
        ("plotly", "Rose Chart", "column"), ("plotly", "Rose Chart", "row"),
    ];

    public static IEnumerable<TestCaseData> ChartTypes() =>
        CommittedIr.Catalog["chartTypes"]!.AsArray().Select(t => new TestCaseData(t!["name"]!.GetValue<string>())
            .SetName($"CompiledTables_EqualIr({t["name"]!.GetValue<string>()})"));

    private static JsonNode IrChartType(string name) =>
        CommittedIr.Catalog["chartTypes"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == name)!;

    // source: ir/flint-ir.json catalog.chartTypes[name].backends (which backends have the template, the channels each
    // declares) and catalog.channels (the union, design §6 unsupported_channel, and excel_unsupported for Excel and
    // image_charts_unsupported for Image-Charts)
    [TestCaseSource(nameof(ChartTypes))]
    public void CompiledTables_EqualIr(string chartType)
    {
        var backends = IrChartType(chartType)["backends"]!.AsObject();
        var union = CommittedIr.Strings(CommittedIr.Catalog["channels"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Has(ChartValidator.Validate(Document(chartType)), "unknown_chart_type"), Is.False, "known chart type");
            foreach (var id in BackendIds)
            {
                var on = backends.ContainsKey(id);
                Assert.That(Has(ChartValidator.Validate(Document(chartType), Backend(id)), "chart_type_not_on_backend"), Is.EqualTo(!on), $"{id}: template present");
                if (!on)
                {
                    continue;
                }

                var declared = CommittedIr.Strings(backends[id]!["channels"]);
                foreach (var channel in union)
                {
                    var result = ChartValidator.Validate(Document(chartType, s => s.Encodings[channel] = "b"), Backend(id));
                    if (declared.Contains(channel))
                    {
                        Assert.That(Has(result, "unsupported_channel", channel), Is.False, $"{id}: {channel} is declared");
                    }
                    else if (id is "excel" or "image-charts")
                    {
                        var unsupported = id == "excel" ? "excel_unsupported" : "image_charts_unsupported";
                        if (channel is "column" or "row" && backends[id]!["supportsFacets"]?.GetValue<bool>() == false)
                        {
                            // source: supportsFacets false: both native assemblers throw on a bound facet
                            Assert.That(Has(result, unsupported, channel), Is.True, $"{id}: a bound {channel} throws");
                        }
                        else if (!CommittedIr.Reads(id, chartType).Contains(channel))
                        {
                            // source: probes.json channelReads (a channel the assembler reads though undeclared under some
                            // base depends on the document's other encodings and types; ChannelReadsTests checks it per base)
                            Assert.That(Has(result, "unsupported_channel", channel) || Has(result, unsupported, channel), Is.True, $"{id}: {channel} is not declared");
                        }
                    }
                    else if (LayoutReadsUndeclared.Contains((id, chartType, channel)))
                    {
                        // source: LayoutReads.ReadsUndeclared against flint-chart 1.0.1 (Vega-Lite and ECharts rendered with
                        // vega-lite 6.5.0 and echarts 6.1.0, Plotly read from its subplot domains and annotations): the
                        // field's values make the facet panels, each in its own grid or subplot
                        Assert.That(Has(result, "unsupported_channel", channel), Is.False, $"{id}: {channel} is read though undeclared");
                    }
                    else if (CommittedIr.RetiredIn($"catalog.chartTypes[{chartType}].backends.{id}.channels[{channel}]") is not null)
                    {
                        // source: generator/retired.json and design part 3 §8.4 (a retired channel is retired_member)
                        Assert.That(Has(result, "retired_member", channel), Is.True, $"{id}: {channel} is retired");
                    }
                    else
                    {
                        Assert.That(Has(result, "unsupported_channel", channel), Is.True, $"{id}: {channel} is not declared");
                    }
                }
            }
        }
    }

    // source: docs/maintainers.md "Running the upstream sync": the exemptions of LayoutReads were classified by rendering the
    // pinned flint-chart (1.0.1) with vega-lite 6.5.0, vega 6.4.0 and echarts 6.1.0, and by reading the Plotly subplot domains;
    // a green sync is no evidence that they still hold. This fails on a new pin so that the review is done first.
    [Test]
    public void LayoutReads_AreReviewedAgainstThePinnedVersion()
    {
        Assert.That(CommittedIr.Root["upstream"]!["version"]!.GetValue<string>(), Is.EqualTo(LayoutReadsReviewedVersion),
            "the pinned flint-chart changed: re-review src/Graspea.Flint/Validation/Rules/LayoutReads.cs as docs/maintainers.md "
            + "(Running the upstream sync) describes, then set LayoutReadsReviewedVersion to the reviewed version");
    }

    public static IEnumerable<TestCaseData> DiscreteProperties() =>
        from t in CommittedIr.Catalog["chartTypes"]!.AsArray()
        from b in t!["backends"]!.AsObject()
        from p in b.Value!["properties"]!.AsArray()
        where p!["type"]!.GetValue<string>() == "discrete"
        select new TestCaseData(t["name"]!.GetValue<string>(), b.Key, p!["key"]!.GetValue<string>())
            .SetName($"DiscreteOptions_EqualIr({t["name"]!.GetValue<string>()}, {b.Key}, {p["key"]!.GetValue<string>()})");

    // source: ir/flint-ir.json catalog.chartTypes[name].backends.<backend>.properties[key].options (values and labels;
    // upstream accepts a value or a label and drops anything else with invalid-option-value, normalize-properties.ts)
    [TestCaseSource(nameof(DiscreteProperties))]
    public void DiscreteOptions_EqualIr(string chartType, string backend, string key)
    {
        var property = IrChartType(chartType)["backends"]![backend]!["properties"]!.AsArray().Single(p => p!["key"]!.GetValue<string>() == key)!;
        ValidationResult With(JsonNode? value) =>
            ChartValidator.Validate(Document(chartType, s => s.ChartProperties = new() { [key] = value }), Backend(backend));

        using (Assert.EnterMultipleScope())
        {
            foreach (var option in property["options"]!.AsArray())
            {
                var label = option!["label"]!.GetValue<string>();
                if (option["value"] is { } value && value.GetValueKind() != System.Text.Json.JsonValueKind.Array)
                {
                    Assert.That(Has(With(value.DeepClone()), "invalid_option_value"), Is.False, $"value of {label}");
                }

                Assert.That(Has(With(label), "invalid_option_value"), Is.False, $"label {label}");
            }

            Assert.That(Has(With("__not_an_option__"), "invalid_option_value"), Is.True, "a value outside the set");
        }
    }

    // source: ir/flint-ir.json catalog.themePresets (ids; an unknown id throws on vegalite and plotly, design §6)
    [Test]
    public void CompiledTables_EqualIr_ThemePresets()
    {
        var ids = CommittedIr.Catalog["themePresets"]!.AsArray().Select(p => p!["id"]!.GetValue<string>());
        using (Assert.EnterMultipleScope())
        {
            foreach (var id in ids)
            {
                var input = Document("Bar Chart");
                input.ThemeSpec = new ThemePreset(id);
                Assert.That(Has(ChartValidator.Validate(input, FlintBackend.VegaLite), "unknown_theme_preset"), Is.False, id);
            }

            var unknown = Document("Bar Chart");
            unknown.ThemeSpec = new ThemePreset("no-such-preset");
            Assert.That(Has(ChartValidator.Validate(unknown, FlintBackend.VegaLite), "unknown_theme_preset"), Is.True);
        }
    }

    // source: ir/flint-ir.json catalog.semanticTypes (registered names; design §6 unknown_semantic_type)
    [Test]
    public void CompiledTables_EqualIr_SemanticTypes()
    {
        var names = CommittedIr.Catalog["semanticTypes"]!.AsArray().Select(s => s!["name"]!.GetValue<string>()).ToArray();
        var input = Document("Bar Chart");
        input.SemanticTypes = names.ToDictionary(n => n, n => (SemanticAnnotation)new SemanticType(n));
        input.SemanticTypes["b"] = new SemanticType("NotARegisteredType");

        var unknown = ChartValidator.Validate(input).Issues.Where(i => i.Code == "unknown_semantic_type").Select(i => i.Field);
        Assert.That(unknown, Is.EqualTo(new[] { "b" }));
    }

    // source: ir/flint-ir.json catalog.backends (FlintBackend follows the fixed order) and catalog.channels (the union
    // contains theta, declared only by the Excel and Image-Charts Pie and Donut templates)
    [Test]
    public void CompiledTables_EqualIr_BackendsAndChannelUnion()
    {
        Assert.That(Enum.GetValues<FlintBackend>().Select(b => BackendIds[(int)b]), Is.EqualTo(CommittedIr.Strings(CommittedIr.Catalog["backends"])));
        Assert.That(CommittedIr.Strings(CommittedIr.Catalog["channels"]), Does.Contain("theta"));
        var pie = ChartValidator.Validate(Document("Pie Chart", s => s.Encodings = new() { ["color"] = "a", ["theta"] = "b" }), FlintBackend.Excel);
        Assert.That(Has(pie, "unsupported_channel", "theta"), Is.False);
    }
}
