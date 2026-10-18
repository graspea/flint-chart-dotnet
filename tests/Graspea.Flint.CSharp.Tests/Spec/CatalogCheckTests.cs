using System.Text.Json.Nodes;
using Graspea.Flint.CSharp;
using Graspea.Flint.CSharp.Tests.Support;
using Graspea.Flint.Serialization;

namespace Graspea.Flint.CSharp.Tests.Spec;

/// <summary>
/// The catalog checks of design §12.6.7: the validator rules that need no data, under their FLINT ids, with the validator's
/// severity for every target backend (<c>backend == null</c>) and the position of the offending node.
/// </summary>
public sealed class CatalogCheckTests
{
    internal static List<SpecIssue> Check(string json) => CatalogCheck.Run(new SpecDocument(JsonReader.Parse(json)));

    private static string Doc(string chartType, string encodings, string extra = "", string specExtra = "") =>
        $"{{{extra}\"chart_spec\":{{\"chartType\":\"{chartType}\",{specExtra}\"encodings\":{encodings}}}}}";

    private static string Describe(SpecIssue i) => $"{i.Id} {i.Code} {i.Severity} {i.Path} @{i.Start.Line}:{i.Start.Column}";

    // source: docs/validation-codes.md and the validator (ValidationRun.ResolveTemplate): unknown_chart_type is an Error at
    // the chartType value; the other template rules then do not run
    [Test]
    public void Run_UnknownChartType_IsFlint110AtTheChartType()
    {
        var issues = Check("{\"chart_spec\":{\"chartType\":\"Image Chart\",\"encodings\":{\"x\":\"a\"}}}");

        Assert.That(issues.Select(Describe), Is.EqualTo(new[] { "FLINT110 unknown_chart_type Error chart_spec.chartType @1:28" }));
        Assert.That(issues[0].Message, Does.EndWith("(unknown_chart_type)"));
        Assert.That(issues[0].End, Is.EqualTo(40));
    }

    // source: ValidationRun.UnsupportedChannel (a Warning when no backend's template declares the channel)
    [Test]
    public void Run_ChannelNoBackendDeclares_IsFlint111AtTheKey()
    {
        var issues = Check(Doc("Line Chart", "{\"x\":\"a\",\"y\":\"b\",\"goal\":\"c\"}"));

        Assert.That(issues.Select(Describe), Is.EqualTo(new[] { "FLINT111 unsupported_channel Warning chart_spec.encodings.goal @1:70" }));
    }

    // source: ValidationRun.PropertyChecks and Accepts (normalizeChartProperties: a value equal to an option value, or a
    // label ignoring case and surrounding whitespace)
    [TestCase("\"wobbly\"", 1)]
    [TestCase("\"monotone\"", 0)]
    [TestCase("\" MONOTONE (SMOOTH) \"", 0)]
    public void Run_DiscreteValueOutsideTheOptions_IsFlint112(string value, int expected)
    {
        var issues = Check(Doc("Line Chart", "{\"x\":\"a\",\"y\":\"b\"}", specExtra: $"\"chartProperties\":{{\"interpolate\":{value}}},"));

        Assert.That(issues.Where(i => i.Id == "FLINT112").Select(Describe).ToList(), Has.Count.EqualTo(expected));
        if (expected == 1)
        {
            Assert.That(Describe(issues.Single()), Is.EqualTo("FLINT112 invalid_option_value Warning chart_spec.chartProperties.interpolate @1:74"));
        }
    }

    // source: ValidationRun.SemanticTypeChecks (unknown_semantic_type, Warning)
    [Test]
    public void Run_UnregisteredSemanticType_IsFlint113()
    {
        var issues = Check(Doc("Line Chart", "{\"x\":\"a\",\"y\":\"b\"}", extra: "\"semantic_types\":{\"a\":\"Revenue\"},"));

        Assert.That(issues.Select(Describe), Is.EqualTo(new[] { "FLINT113 unknown_semantic_type Warning semantic_types.a @1:24" }));
    }

    // source: ValidationRun.ThemeChecks (unknown_theme_preset: an Error where the template is on Vega-Lite or Plotly,
    // which throw, an Info for a template neither has; the object form names its extends)
    [TestCase("Line Chart", "\"theme_spec\":\"neon\",", "FLINT114 unknown_theme_preset Error theme_spec @1:15")]
    [TestCase("Line Chart", "\"theme_spec\":{\"extends\":\"neon\"},", "FLINT114 unknown_theme_preset Error theme_spec.extends @1:26")]
    [TestCase("Line Chart", "\"theme_spec\":\"economist\",", null)]
    [TestCase("Line Chart", "\"theme_spec\":\"toString\",", null)]
    public void Run_UnknownThemePreset_IsFlint114(string chartType, string theme, string? expected)
    {
        var issues = Check(Doc(chartType, "{\"x\":\"a\",\"y\":\"b\"}", extra: theme));

        Assert.That(issues.Select(Describe), Is.EqualTo(expected is null ? Array.Empty<string>() : new[] { expected }));
    }

    // source: ValidationRun.ThemeChecks with a template that only ECharts and Chart.js have: no backend that applies
    // themes, so the unknown preset is an Info
    [Test]
    public void Run_UnknownThemePresetWhereNoBackendAppliesThemes_IsAnInfo()
    {
        var withoutTheme = Catalog.ChartTypes.First(c => c.Templates.All(t => !Catalog.BackendsApplyingTheme.Contains(t.Backend)));
        var encodings = string.Join(",", withoutTheme.Templates[0].Channels.Take(1).Select(c => $"\"{c}\":\"a\""));
        var issues = Check(Doc(withoutTheme.Name, $"{{{encodings}}}", extra: "\"theme_spec\":\"neon\","));

        Assert.That(issues.Where(i => i.Id == "FLINT114").Select(i => i.Severity), Is.EqualTo(new[] { SpecSeverity.Info }));
    }

    // source: ValidationRun.Run (missing_encodings), EncodingChecks (channel_null: an Error on the layout backends, an
    // Info on Excel, reported at the highest), SemanticTypeChecks (reserved_semantic_type for a bound field) and
    // StaticSeries.Evaluate (the series rules in upstream's order, the first that holds); FLINT115 only, since a null y is
    // also an unbound required channel
    [TestCase("{\"chart_spec\":{\"chartType\":\"Line Chart\",\"encodings\":null}}", "FLINT115 missing_encodings Error chart_spec.encodings @1:53")]
    [TestCase("{\"chart_spec\":{\"chartType\":\"Line Chart\",\"encodings\":{\"x\":\"a\",\"y\":null}}}", "FLINT115 channel_null Error chart_spec.encodings.y @1:66")]
    [TestCase("{\"semantic_types\":{\"a\":\"toString\"},\"chart_spec\":{\"chartType\":\"Line Chart\",\"encodings\":{\"x\":\"a\",\"y\":\"b\"}}}", "FLINT115 reserved_semantic_type Error semantic_types.a @1:24")]
    [TestCase("{\"chart_spec\":{\"chartType\":\"Line Chart\",\"encodings\":{\"x\":[\"a\",\"b\"],\"y\":[\"c\",\"d\"]}}}", "FLINT115 series_multiple_channels Error chart_spec.encodings.y @1:68")]
    [TestCase("{\"chart_spec\":{\"chartType\":\"Line Chart\",\"encodings\":{\"x\":\"a\",\"y\":\"b\",\"color\":[\"c\",\"d\"]}}}", "FLINT115 series_channel_not_xy Error chart_spec.encodings.color @1:70")]
    [TestCase("{\"chart_spec\":{\"chartType\":\"Line Chart\",\"encodings\":{\"x\":\"a\",\"y\":[\"b\"]}}}", "FLINT115 series_too_few Error chart_spec.encodings.y @1:62")]
    [TestCase("{\"chart_spec\":{\"chartType\":\"Line Chart\",\"encodings\":{\"x\":\"a\",\"y\":[\"b\",{\"aggregate\":\"count\"}]}}}", "FLINT115 series_field_missing Error chart_spec.encodings.y @1:62")]
    [TestCase("{\"chart_spec\":{\"chartType\":\"Line Chart\",\"encodings\":{\"x\":\"a\",\"y\":[\"b\",\"b\"]}}}", "FLINT115 series_duplicate_field Error chart_spec.encodings.y @1:62")]
    [TestCase("{\"chart_spec\":{\"chartType\":\"Line Chart\",\"encodings\":{\"x\":\"a\",\"y\":[\"b\",{\"field\":\"c\",\"type\":\"nominal\"}]}}}", "FLINT115 series_nominal_entry Error chart_spec.encodings.y @1:62")]
    [TestCase("{\"chart_spec\":{\"chartType\":\"Line Chart\",\"encodings\":{\"x\":\"a\",\"y\":[\"b\",\"c\"],\"color\":\"d\"}}}", "FLINT115 series_color_bound Error chart_spec.encodings.color @1:76")]
    public void Run_ThrowClassRule_IsFlint115(string json, string expected)
    {
        Assert.That(Check(json).Where(i => i.Id == "FLINT115").Select(Describe), Is.EqualTo(new[] { expected }));
    }

    // source: ValidationRun.PropertyChecks (unknown_chart_property unless a host-state key, continuous_out_of_range),
    // LiteralChecks (invalid_literal), RequiredChannelChecks (required_channel_missing) and CanvasChecks
    // (canvas_too_large), all Info
    [TestCase("\"chartProperties\":{\"glow\":true},", "FLINT116 unknown_chart_property Info chart_spec.chartProperties.glow")]
    [TestCase("\"chartProperties\":{\"sort\":true},", null)]
    [TestCase("\"chartProperties\":{\"cornerRadius\":99},", "FLINT116 continuous_out_of_range Info chart_spec.chartProperties.cornerRadius")]
    [TestCase("\"baseSize\":{\"width\":5000,\"height\":10},", "FLINT116 canvas_too_large Info chart_spec.baseSize")]
    public void Run_InfoRule_IsFlint116(string specExtra, string? expected)
    {
        var issues = Check(Doc("Bar Chart", "{\"x\":\"a\",\"y\":\"b\"}", specExtra: specExtra));

        Assert.That(issues.Select(i => $"{i.Id} {i.Code} {i.Severity} {i.Path}"), Is.EqualTo(expected is null ? Array.Empty<string>() : new[] { expected }));
    }

    // source: ValidationRun.LiteralChecks (a literal outside the pinned union, on an encoding and in the options) and
    // RequiredChannelChecks (x and y when a template declares both)
    [Test]
    public void Run_InvalidLiteralsAndAMissingChannel_AreFlint116()
    {
        var json = "{\"options\":{\"continuousMarkCrossSection\":{\"x\":1,\"y\":1,\"seriesCountAxis\":\"diagonal\"}},"
            + "\"chart_spec\":{\"chartType\":\"Line Chart\",\"encodings\":{\"x\":{\"field\":\"a\",\"aggregate\":\"median\"}}}}";

        Assert.That(Check(json).Select(i => $"{i.Id} {i.Code} {i.Severity} {i.Path}"), Is.EqualTo(new[]
        {
            "FLINT116 invalid_literal Info chart_spec.encodings.x.aggregate",
            "FLINT116 invalid_literal Info options.continuousMarkCrossSection.seriesCountAxis",
            "FLINT116 required_channel_missing Info chart_spec.encodings.y",
        }));
    }

    // source: design §12.6.7 (FLINT140, Info: unknown keys are passed through Extensions, at each key) and the tool's
    // findings (top-level and chart_spec keys)
    [Test]
    public void Run_UnknownKeys_AreFlint140AtEachKey()
    {
        var issues = Check("{\"interaction_spec\":{},\"chart_spec\":{\"chartType\":\"Line Chart\",\"zoom\":1,\"encodings\":{\"x\":\"a\",\"y\":\"b\"}}}");

        Assert.That(issues.Select(Describe), Is.EqualTo(new[]
        {
            "FLINT140 unknown_key Info interaction_spec @1:2",
            "FLINT140 unknown_key Info chart_spec.zoom @1:63",
        }));
        Assert.That(issues[0].End, Is.EqualTo(19));
    }

    /// <summary>The validator codes the catalog checks mirror (design §12.6.7, FLINT110 to FLINT116).</summary>
    private static readonly HashSet<string> Mirrored = new(StringComparer.Ordinal)
    {
        "unknown_chart_type", "unsupported_channel", "invalid_option_value", "unknown_semantic_type", "unknown_theme_preset",
        "missing_encodings", "channel_null", "reserved_semantic_type", "series_multiple_channels", "series_channel_not_xy",
        "series_too_few", "series_field_missing", "series_duplicate_field", "series_color_bound", "series_nominal_entry",
        "unknown_chart_property", "continuous_out_of_range", "invalid_literal", "semantic_type_without_field", "canvas_too_large",
        "required_channel_missing",
    };

    // source: design §12.10 (catalog checks: over the fixtures and probe cases, the core's codes equal the validator's
    // data-independent codes with the same severity); the validator runs on a template without row metadata keys
    // (ChartTemplate.Validate with a dynamic row shape), which skips every rule that reads the rows
    [Test]
    [Category("Contract")]
    public void Run_EveryCorpusDocument_EqualsTheValidatorWithoutRows()
    {
        var failures = new List<string>();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in SpecCorpus.Accepted)
        {
            var expected = ChartTemplate.Create<JsonObject>(FlintJson.Deserialize(c.Json)).Validate().Issues
                .Where(i => Mirrored.Contains(i.Code))
                .Select(i => $"{i.Code} {i.Severity} {i.Path}")
                .Order(StringComparer.Ordinal)
                .ToList();
            var actual = Check(c.Json).Where(i => Mirrored.Contains(i.Code))
                .Select(i => $"{i.Code} {i.Severity} {i.Path}")
                .Order(StringComparer.Ordinal)
                .ToList();
            codes.UnionWith(expected.Select(e => e.Split(' ')[0]));
            if (!expected.SequenceEqual(actual))
            {
                failures.Add($"{c.Id}: expected [{string.Join("; ", expected)}], got [{string.Join("; ", actual)}]");
            }
        }

        Assert.That(SpecCorpus.Accepted, Has.Count.GreaterThan(900));
        // The corpus reaches every mirrored code except canvas_too_large (a unit test above) and
        // semantic_type_without_field, which the validator skips without row keys.
        Assert.That(codes, Is.EquivalentTo(Mirrored.Except(["canvas_too_large", "semantic_type_without_field"])));
        Assert.That(failures, Is.Empty, string.Join("\n", failures.Take(20)));
    }
}
