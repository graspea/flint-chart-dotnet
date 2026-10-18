using System.Text.Json.Nodes;
using Graspea.Flint.Tests.Support;
using Graspea.Flint.Validation;

namespace Graspea.Flint.Tests.Parity;

/// <summary>
/// The compiled catalog tables against the committed IR, read through the validator and the structs (design §10.1
/// rule 4, §10.4 test 15). Both sides come from the IR, so a failure here is an emitter bug (§9.3).
/// </summary>
public sealed class CatalogCrossCheckTests
{
    private static readonly string[] BackendIds = ["vegalite", "echarts", "chartjs", "plotly", "excel"];

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

    public static IEnumerable<TestCaseData> ChartTypes() =>
        CommittedIr.Catalog["chartTypes"]!.AsArray().Select(t => new TestCaseData(t!["name"]!.GetValue<string>())
            .SetName($"CompiledTables_EqualIr({t["name"]!.GetValue<string>()})"));

    private static JsonNode IrChartType(string name) =>
        CommittedIr.Catalog["chartTypes"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == name)!;

    // source: ir/flint-ir.json catalog.chartTypes[name].backends (which backends have the template, the channels each
    // declares) and catalog.channels (the union, design §6 unsupported_channel and excel_unsupported for Excel)
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
                    else if (id == "excel")
                    {
                        Assert.That(Has(result, "unsupported_channel", channel) || Has(result, "excel_unsupported", channel), Is.True, $"{id}: {channel} is not declared");
                    }
                    else
                    {
                        Assert.That(Has(result, "unsupported_channel", channel), Is.True, $"{id}: {channel} is not declared");
                    }
                }
            }
        }
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
    // contains theta, declared only by the Excel Pie and Donut templates)
    [Test]
    public void CompiledTables_EqualIr_BackendsAndChannelUnion()
    {
        Assert.That(Enum.GetValues<FlintBackend>().Select(b => BackendIds[(int)b]), Is.EqualTo(CommittedIr.Strings(CommittedIr.Catalog["backends"])));
        Assert.That(CommittedIr.Strings(CommittedIr.Catalog["channels"]), Does.Contain("theta"));
        var pie = ChartValidator.Validate(Document("Pie Chart", s => s.Encodings = new() { ["color"] = "a", ["theta"] = "b" }), FlintBackend.Excel);
        Assert.That(Has(pie, "unsupported_channel", "theta"), Is.False);
    }
}
