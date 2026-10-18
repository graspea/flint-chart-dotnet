using System.Text.Json.Nodes;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Support;
using Graspea.Flint.Validation;

namespace Graspea.Flint.Tests.Validation;

/// <summary>
/// Every code of design §6 on a minimal document, with the severity its upstream fact implies (§10.3 unit: validation;
/// §10.4 tests 16 and 17). Facts marked "probe" are recorded in generator/verify/probes.json.
/// </summary>
public sealed partial class ChartValidatorTests
{
    private static JsonArray Rows() =>
    [
        new JsonObject { ["city"] = "Seattle", ["revenue"] = 501479, ["cost"] = 10 },
        new JsonObject { ["city"] = "Boston", ["revenue"] = 300000, ["cost"] = 20 },
    ];

    /// <summary>The probes' baseline: a Bar Chart of revenue by city, which every backend assembles.</summary>
    private static ChartAssemblyInput Bar(Action<ChartAssemblyInput>? change = null)
    {
        var input = new ChartAssemblyInput
        {
            Data = ChartData.FromJson(Rows()),
            ChartSpec = new ChartSpec { ChartType = ChartType.BarChart, Encodings = { ["x"] = "city", ["y"] = "revenue" } },
        };
        change?.Invoke(input);
        return input;
    }

    private static ChartAssemblyInput Line(Action<ChartAssemblyInput>? change = null)
    {
        var input = Bar(change: null);
        input.ChartSpec.ChartType = ChartType.LineChart;
        change?.Invoke(input);
        return input;
    }

    private static IEnumerable<ValidationIssue> Issues(ChartAssemblyInput input, FlintBackend? backend = null, string? code = null) =>
        ChartValidator.Validate(input, backend).Issues.Where(i => code is null || i.Code == code);

    private static void AssertOne(ChartAssemblyInput input, FlintBackend? backend, string code, IssueSeverity severity) =>
        Assert.That(ChartValidator.Validate(input, backend).Issues, Has.Exactly(1).Matches<ValidationIssue>(i => i.Code == code && i.Severity == severity));

    /// <summary>A property of a template on a backend in the committed IR, or null when that backend does not declare it.</summary>
    private static JsonNode? IrProperty(string chartType, FlintBackend backend, string key)
    {
        var id = backend switch
        {
            FlintBackend.VegaLite => "vegalite",
            FlintBackend.ECharts => "echarts",
            FlintBackend.ChartJs => "chartjs",
            FlintBackend.Plotly => "plotly",
            _ => "excel",
        };
        var template = CommittedIr.Catalog["chartTypes"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == chartType)!["backends"]![id];
        return template?["properties"]!.AsArray().SingleOrDefault(p => p!["key"]!.GetValue<string>() == key);
    }

    private static void AssertNone(ChartAssemblyInput input, FlintBackend? backend, string code) =>
        Assert.That(Issues(input, backend, code), Is.Empty);

    // source: probe "baseline" (the Bar Chart assembles on all five) and design §6 (a clean document yields nothing)
    [Test]
    public void Validate_CleanDocument_HasNoIssues()
    {
        var result = ChartValidator.Validate(Bar());
        Assert.That(result.Issues, Is.Empty);
        Assert.That(result.IsValid, Is.True);
        Assert.That((Action)(() => result.ThrowIfInvalid()), Throws.Nothing);
    }

    // source: probe "chartType unknown" (throws on all five) and design §6 unknown_chart_type
    [Test]
    public void Validate_UnknownChartType_IsError()
    {
        var input = Bar(i => i.ChartSpec.ChartType = new ChartType("Image Chart"));
        AssertOne(input, null, "unknown_chart_type", IssueSeverity.Error);
        AssertOne(input, FlintBackend.Excel, "unknown_chart_type", IssueSeverity.Error);
    }

    // source: ir/flint-ir.json (Doughnut Chart has only a chartjs template) and design §10.4 test 16
    [Test]
    public void Validate_ChartTypeMissingOnBackend_IsError()
    {
        var input = Bar(i => i.ChartSpec.ChartType = ChartType.DoughnutChart);
        AssertOne(input, FlintBackend.VegaLite, "chart_type_not_on_backend", IssueSeverity.Error);
        AssertNone(input, FlintBackend.ChartJs, "chart_type_not_on_backend");
        AssertNone(input, null, "chart_type_not_on_backend");
    }

    // source: probe "encodings missing" (throws on all five)
    [Test]
    public void Validate_MissingEncodings_IsError()
    {
        AssertOne(Bar(i => i.ChartSpec.Encodings = null!), FlintBackend.ECharts, "missing_encodings", IssueSeverity.Error);
    }

    // source: probe "encodings empty" (throws on Excel only)
    [Test]
    public void Validate_EmptyEncodings_IsErrorOnExcelOnly()
    {
        var input = Bar(i => i.ChartSpec.Encodings.Clear());
        AssertOne(input, FlintBackend.Excel, "encodings_empty", IssueSeverity.Error);
        AssertOne(input, FlintBackend.VegaLite, "encodings_empty", IssueSeverity.Info);
        AssertOne(input, null, "encodings_empty", IssueSeverity.Error);
    }

    // source: probe "channel null" (throws on vegalite, echarts, chartjs and plotly; Excel accepts it)
    [Test]
    public void Validate_NullChannel_IsErrorExceptOnExcel()
    {
        var input = Bar(i => i.ChartSpec.Encodings["color"] = null!);
        AssertOne(input, FlintBackend.VegaLite, "channel_null", IssueSeverity.Error);
        AssertOne(input, FlintBackend.Excel, "channel_null", IssueSeverity.Info);
        Assert.That(Issues(input, FlintBackend.Excel, "channel_null").Single().Channel, Is.EqualTo(Channel.Color));
    }

    // source: probe "data null" (throws on all five)
    [Test]
    public void Validate_NullData_IsError()
    {
        AssertOne(Bar(i => i.Data = null!), FlintBackend.Plotly, "data_missing", IssueSeverity.Error);
    }

    // source: probe "data values empty" (Excel throws: untyped fields cannot be quantitative without rows) and
    // "data values empty, typed encodings" (no throw anywhere)
    [Test]
    public void Validate_EmptyData_IsErrorOnExcelOnly()
    {
        var untyped = Bar(i => i.Data = ChartData.FromJson([]));
        AssertOne(untyped, FlintBackend.Excel, "data_empty", IssueSeverity.Error);
        AssertOne(untyped, FlintBackend.VegaLite, "data_empty", IssueSeverity.Info);

        var typed = Bar(i =>
        {
            i.Data = ChartData.FromJson([]);
            i.ChartSpec.Encodings["x"] = new ChartEncoding { Field = "city", Type = EncodingType.Nominal };
            i.ChartSpec.Encodings["y"] = new ChartEncoding { Field = "revenue", Type = EncodingType.Quantitative };
        });
        AssertOne(typed, FlintBackend.Excel, "data_empty", IssueSeverity.Info);
        Assert.That(ChartValidator.Validate(typed).IsValid, Is.True);
    }

    // source: probe "data url only" (Excel throws; the other backends never fetch the URL) and design §6 data_url_only
    [Test]
    public void Validate_UrlOnlyData_IsErrorOnExcelOnly()
    {
        var input = Bar(i => i.Data = ChartData.FromUrl("https://example.com/rows.json"));
        AssertOne(input, FlintBackend.Excel, "data_url_only", IssueSeverity.Error);
        AssertOne(input, FlintBackend.VegaLite, "data_url_not_fetched", IssueSeverity.Info);
        AssertNone(input, FlintBackend.VegaLite, "data_url_only");
    }

    // source: design §6 excel_unsupported (Excel throws on a bound column/row; supportsFacets false in ir/flint-ir.json)
    [Test]
    public void Validate_FacetOnExcel_IsExcelUnsupported()
    {
        var input = Bar(i => i.ChartSpec.Encodings["column"] = "city");
        Assert.That(Issues(input, FlintBackend.Excel, "excel_unsupported"), Has.Exactly(1).Matches<ValidationIssue>(i => i.Severity == IssueSeverity.Error && i.Channel == new Channel("column")));
        AssertNone(input, FlintBackend.VegaLite, "excel_unsupported");
        Assert.That(ChartValidator.Validate(input, FlintBackend.VegaLite).IsValid, Is.True);
    }

    // source: design §6 excel_unsupported and tests/fixtures/flint-chart@0.5.1/probe-expectations.jsonl (Excel reads an
    // array encoding as an unbound channel: a static series on y of a Line Chart throws, one on color assembles)
    [Test]
    public void Validate_StaticSeriesOnExcel_IsExcelUnsupportedWhereTheTemplateNeedsTheChannel()
    {
        var input = Line(i => i.ChartSpec.Encodings["y"] = EncodingValue.Series(new() { Field = "revenue" }, new() { Field = "cost" }));
        Assert.That(Issues(input, FlintBackend.Excel, "excel_unsupported"), Has.Some.Matches<ValidationIssue>(i => i.Severity == IssueSeverity.Error && i.Channel == Channel.Y));
        Assert.That(ChartValidator.Validate(input, FlintBackend.VegaLite).IsValid, Is.True);

        var color = Line(i => i.ChartSpec.Encodings["color"] = EncodingValue.Series(new() { Field = "revenue" }, new() { Field = "cost" }));
        AssertNone(color, FlintBackend.Excel, "excel_unsupported");
    }

    // source: review D2-6 (the issue text adds "Excel " in front of the reason, and "it " in the no-rows form, so a reason
    // starts with its verb)
    [Test]
    public void Validate_ExcelReasons_ReadAsOneSentence()
    {
        var inputs = new[]
        {
            Bar(i => i.ChartSpec.Encodings["column"] = "city"),
            Line(i => i.ChartSpec.Encodings["y"] = EncodingValue.Series(new() { Field = "revenue" }, new() { Field = "cost" })),
            Bar(i =>
            {
                i.ChartSpec.ChartType = ChartType.Histogram;
                i.Data = ChartData.FromJson([]);
                i.ChartSpec.Encodings = new() { ["x"] = new ChartEncoding { Field = "revenue", Type = EncodingType.Quantitative } };
            }),
            Bar(i =>
            {
                i.ChartSpec.ChartType = ChartType.RadarChart;
                i.ChartSpec.Encodings = new() { ["x"] = "city", ["y"] = "revenue" };
            }),
        };
        foreach (var input in inputs)
        {
            var messages = Issues(input, FlintBackend.Excel).Where(i => i.Code is "excel_unsupported" or "data_empty" && i.Severity == IssueSeverity.Error)
                .Select(i => i.Message).ToArray();
            Assert.That(messages, Is.Not.Empty, input.ChartSpec.ChartType.Name);
            Assert.That(messages, Has.None.Contains("Excel Excel").And.None.Contains("it Excel"));
        }
    }

    // source: design §6 Info canvas_too_large (> 4000 px, the MCP front door limit; the assemblers accept it)
    [Test]
    public void Validate_CanvasTooLarge_IsInfo()
    {
        AssertOne(Bar(i => i.ChartSpec.CanvasSize = new ChartSize(4001, 300)), null, "canvas_too_large", IssueSeverity.Info);
        AssertNone(Bar(i => i.ChartSpec.CanvasSize = new ChartSize(4000, 4000)), null, "canvas_too_large");
    }

    // source: probe "theme_spec unknown preset" (throws on vegalite and plotly only; design §7.5)
    [Test]
    public void Validate_UnknownThemePreset_IsErrorOnThemeBackendsOnly()
    {
        var input = Bar(i => i.ThemeSpec = new ThemePreset("no-such-preset"));
        AssertOne(input, FlintBackend.Plotly, "unknown_theme_preset", IssueSeverity.Error);
        AssertOne(input, FlintBackend.ECharts, "unknown_theme_preset", IssueSeverity.Info);
        AssertOne(input, null, "unknown_theme_preset", IssueSeverity.Error);
    }

    // source: core/theme/presets.ts:67-84 (an object's extends goes through presetSpec, which throws on an unknown id) and
    // probe themeBackends.unknownExtendsThrows (vegalite and plotly)
    [Test]
    public void Validate_UnknownThemeExtends_IsErrorOnThemeBackendsOnly()
    {
        var input = Bar(i => i.ThemeSpec = new Theming.ThemeSpec { Extends = "no-such-preset" });
        AssertOne(input, FlintBackend.VegaLite, "unknown_theme_preset", IssueSeverity.Error);
        AssertOne(input, FlintBackend.Plotly, "unknown_theme_preset", IssueSeverity.Error);
        AssertOne(input, FlintBackend.ECharts, "unknown_theme_preset", IssueSeverity.Info);
        AssertOne(input, null, "unknown_theme_preset", IssueSeverity.Error);
        Assert.That(Issues(input, null, "unknown_theme_preset").Single().Path, Is.EqualTo("theme_spec.extends"));
        AssertNone(Bar(i => i.ThemeSpec = new Theming.ThemeSpec { Extends = "economist" }), null, "unknown_theme_preset");
    }

    public static IEnumerable<TestCaseData> StaticSeriesViolations()
    {
        static TestCaseData Case(string name, string code, Action<ChartAssemblyInput> change) =>
            new TestCaseData(code, change).SetName($"StaticSeries_UpstreamRule_IsError({name})");

        static EncodingValue Series(params string[] fields) => EncodingValue.Series(fields.Select(f => new ChartEncoding { Field = f }).ToArray());

        yield return Case("two series channels", "series_multiple_channels", i =>
        {
            i.ChartSpec.Encodings["x"] = Series("cost", "revenue");
            i.ChartSpec.Encodings["y"] = Series("revenue", "cost");
        });
        yield return Case("series on color", "series_channel_not_xy", i => i.ChartSpec.Encodings["color"] = Series("revenue", "cost"));
        yield return Case("one entry", "series_too_few", i => i.ChartSpec.Encodings["y"] = Series("revenue"));
        yield return Case("duplicate field", "series_duplicate_field", i => i.ChartSpec.Encodings["y"] = Series("revenue", "revenue"));
        yield return Case("field not in the first row", "series_field_missing", i => i.ChartSpec.Encodings["y"] = Series("revenue", "profit"));
        yield return Case("color bound", "series_color_bound", i =>
        {
            i.ChartSpec.Encodings["y"] = Series("revenue", "cost");
            i.ChartSpec.Encodings["color"] = "city";
        });
        yield return Case("nominal entry", "series_nominal_entry", i => i.ChartSpec.Encodings["y"] = EncodingValue.Series(
            new ChartEncoding { Field = "revenue" }, new ChartEncoding { Field = "cost", Type = EncodingType.Ordinal }));
    }

    // source: part 01 §2 (static-series.ts:114-206 throw conditions, probed in p5) and design §6 series_* (four layout backends)
    [TestCaseSource(nameof(StaticSeriesViolations))]
    public void StaticSeries_UpstreamRule_IsError(string code, Action<ChartAssemblyInput> change)
    {
        var input = Line(change);
        foreach (var backend in new[] { FlintBackend.VegaLite, FlintBackend.ECharts, FlintBackend.ChartJs, FlintBackend.Plotly })
        {
            AssertOne(input, backend, code, IssueSeverity.Error);
        }

        AssertNone(input, FlintBackend.Excel, code);
    }

    // source: part 01 §2 static series (a valid series: two quantitative fields on y, color unbound) assembles
    [Test]
    public void StaticSeries_ValidSeries_HasNoSeriesIssue()
    {
        var input = Line(i => i.ChartSpec.Encodings["y"] = EncodingValue.Series(new() { Field = "revenue" }, new() { Field = "cost" }));
        Assert.That(ChartValidator.Validate(input, FlintBackend.VegaLite).Issues, Is.Empty);
    }

    // source: design §6 (validation never throws; an issue on a channel names it) and FlintJson, which reads an empty
    // encoding key as given; a Channel cannot be empty, so the issue carries no channel
    [Test]
    public void Warnings_UnsupportedEmptyChannelKey_IsReportedWithoutAChannel()
    {
        var input = Bar(i => i.ChartSpec.Encodings[""] = "cost");

        var issue = Issues(input, null, "unsupported_channel").Single();
        Assert.That(issue.Path, Is.EqualTo("chart_spec.encodings."));
        Assert.That(issue.Channel, Is.Null);
    }

    // source: ir/flint-ir.json (Line Chart declares detail on vegalite only) and design §6 unsupported_channel
    [Test]
    public void Warnings_UnsupportedChannel_PerBackendAndAny()
    {
        var input = Line(i => i.ChartSpec.Encodings["detail"] = "city");
        AssertNone(input, FlintBackend.VegaLite, "unsupported_channel");
        AssertOne(input, FlintBackend.ECharts, "unsupported_channel", IssueSeverity.Warning);
        AssertNone(input, null, "unsupported_channel");

        var goal = Bar(i => i.ChartSpec.Encodings["goal"] = "cost");
        AssertOne(goal, null, "unsupported_channel", IssueSeverity.Warning);
        Assert.That(Issues(goal, null, "unsupported_channel").Single().Channel, Is.EqualTo(Channel.Goal));
    }

    // source: probe p5 (a field missing from data[0] assembles; the encoding resolves to nothing) and design §6
    // unknown_field (exempt for aggregate count without a field)
    [Test]
    public void Warnings_UnknownField_ExemptForCountWithoutField()
    {
        var input = Bar(i => i.ChartSpec.Encodings["y"] = "profit");
        AssertOne(input, null, "unknown_field", IssueSeverity.Warning);
        Assert.That(Issues(input, null, "unknown_field").Single().Field, Is.EqualTo("profit"));

        AssertNone(Bar(i => i.ChartSpec.Encodings["y"] = new ChartEncoding { Aggregate = Aggregate.Count }), null, "unknown_field");
    }

    // source: probe p16 (an unregistered name is kept and has no effect) and design §6 unknown_semantic_type
    [Test]
    public void Warnings_UnknownSemanticType()
    {
        var input = Bar(i => i.SemanticTypes = new() { ["revenue"] = new SemanticType("Revenue"), ["city"] = SemanticType.City });
        AssertOne(input, null, "unknown_semantic_type", IssueSeverity.Warning);
        Assert.That(Issues(input, null, "unknown_semantic_type").Single().Field, Is.EqualTo("revenue"));
    }

    // source: probe p4 "option value invalid" (invalid-option-value on the four layout backends) and
    // normalize-properties.ts (a display label is coerced, not dropped)
    [Test]
    public void Warnings_InvalidOptionValue_FollowsUpstreamNormalisation()
    {
        AssertOne(Line(i => i.ChartSpec.ChartProperties = new() { ["interpolate"] = "zzz" }), FlintBackend.VegaLite, "invalid_option_value", IssueSeverity.Warning);
        AssertNone(Line(i => i.ChartSpec.ChartProperties = new() { ["interpolate"] = "monotone" }), FlintBackend.VegaLite, "invalid_option_value");
        AssertNone(Line(i => i.ChartSpec.ChartProperties = new() { ["interpolate"] = " monotone (SMOOTH) " }), FlintBackend.VegaLite, "invalid_option_value");
        AssertNone(Line(i => i.ChartSpec.ChartProperties = new() { ["interpolate"] = null }), FlintBackend.VegaLite, "invalid_option_value");
        AssertNone(Line(i => i.ChartSpec.ChartProperties = new() { ["interpolate"] = "zzz" }), FlintBackend.Excel, "invalid_option_value");
    }

    // source: d.ts T:1994-2001 (aggregate literals) and design §6 Info invalid_literal (no recorded drop; probe p4
    // "aggregate median" assembles)
    [Test]
    public void Infos_InvalidLiteral()
    {
        var input = Bar(i => i.ChartSpec.Encodings["y"] = new ChartEncoding { Field = "revenue", Aggregate = new Aggregate("median") });
        AssertOne(input, null, "invalid_literal", IssueSeverity.Info);
        Assert.That(Issues(input, null, "invalid_literal").Single().Path, Is.EqualTo("chart_spec.encodings.y.aggregate"));
    }

    // source: design §6 Info inconsistent_row_keys (the MCP front door checks the union of row keys, the assemblers do not)
    [Test]
    public void Infos_InconsistentRowKeys()
    {
        var input = Bar(i => i.Data = ChartData.FromJson([new JsonObject { ["city"] = "A", ["revenue"] = 1 }, new JsonObject { ["city"] = "B" }]));
        AssertOne(input, null, "inconsistent_row_keys", IssueSeverity.Info);
        Assert.That(Issues(input, null, "inconsistent_row_keys").Single().Path, Is.EqualTo("data.values[1]"));
    }

    // source: generator/verify/probes.json nullHandling "row number, no temporal key" (a number row assembles when no key of
    // the first row is temporal; the MCP front door rejects it)
    [Test]
    public void Infos_RowNotObject()
    {
        var input = Bar(i => i.Data = ChartData.FromJson([new JsonObject { ["city"] = "A", ["revenue"] = 1 }, 5]));
        AssertOne(input, null, "row_not_object", IssueSeverity.Info);
    }

    // source: generator/verify/probes.json nullHandling "row null" and "row null first" (every backend throws reading a
    // key of a null row)
    [Test]
    public void Errors_NullRow_OnEveryBackend()
    {
        var second = Bar(i => i.Data = ChartData.FromJson([new JsonObject { ["city"] = "A", ["revenue"] = 1 }, null]));
        AssertOne(second, null, "row_not_object", IssueSeverity.Error);
        Assert.That(Issues(second, null, "row_not_object").Single().Path, Is.EqualTo("data.values[1]"));
        foreach (var backend in Enum.GetValues<FlintBackend>())
        {
            AssertOne(second, backend, "row_not_object", IssueSeverity.Error);
        }

        AssertOne(Bar(i => i.Data = ChartData.FromJson([null, new JsonObject { ["city"] = "A", ["revenue"] = 1 }])), null, "row_not_object", IssueSeverity.Error);
    }

    // source: generator/verify/probes.json nullHandling "row number with temporal x" and "row number with an unbound
    // temporal key" (convertTemporalData writes every temporal key of the first row back into each row)
    [Test]
    public void Errors_PrimitiveRow_WithATemporalKey()
    {
        var line = Line(i => i.Data = ChartData.FromJson(
            [new JsonObject { ["month"] = "2024-01-15", ["revenue"] = 1 }, 7, new JsonObject { ["month"] = "2024-02-15", ["revenue"] = 2 }]));
        line.ChartSpec.Encodings["x"] = "month";
        AssertOne(line, null, "row_not_object", IssueSeverity.Error);
        AssertOne(line, FlintBackend.Excel, "row_not_object", IssueSeverity.Error);

        var unbound = Bar(i => i.Data = ChartData.FromJson(
            [new JsonObject { ["city"] = "A", ["revenue"] = 1, ["day"] = "2024-01-10" }, "x", new JsonObject { ["city"] = "B", ["revenue"] = 2, ["day"] = "2024-01-11" }]));
        AssertOne(unbound, null, "row_not_object", IssueSeverity.Error);
    }

    // source: generator/verify/probes.json prototypeKeys (THEME_PRESETS["constructor"] and THEME_PRESETS["toString"] are
    // truthy, so nothing throws)
    [Test]
    public void Validate_ThemeNamedLikeAnObjectPrototypeMember_IsNotUnknown()
    {
        AssertNone(Bar(i => i.ThemeSpec = new ThemePreset("constructor")), null, "unknown_theme_preset");
        AssertNone(Bar(i => i.ThemeSpec = new Theming.ThemeSpec { Extends = "toString" }), null, "unknown_theme_preset");
        Assert.That(ChartValidator.Validate(Bar(i => i.ThemeSpec = new ThemePreset("__proto__")), null).IsValid, Is.True);
    }

    // source: generator/verify/probes.json prototypeKeys (`semanticType in TYPE_REGISTRY` holds for a name of
    // Object.prototype, which has no visEncodings: every backend throws when the field is bound or a key of the first row)
    [Test]
    public void Errors_SemanticTypeNamedLikeAnObjectPrototypeMember()
    {
        var bound = Bar(i => i.SemanticTypes = new() { ["city"] = new SemanticType("constructor") });
        AssertOne(bound, null, "reserved_semantic_type", IssueSeverity.Error);
        AssertOne(bound, FlintBackend.Excel, "reserved_semantic_type", IssueSeverity.Error);
        AssertNone(bound, null, "unknown_semantic_type");

        AssertOne(Bar(i => i.SemanticTypes = new() { ["cost"] = new SemanticType("toString") }), null, "reserved_semantic_type", IssueSeverity.Error);
        AssertNone(Bar(i => i.SemanticTypes = new() { ["zz"] = new SemanticType("valueOf") }), null, "reserved_semantic_type");
    }

    // source: src/vegalite/assemble.ts (a sortBy outside x/y/color that is not a JSON array: console.warn and no sort),
    // src/echarts/assemble.ts (the failed JSON.parse is caught silently), src/echarts/templates/utils.ts and design §6
    // sort_by_invalid (size and group, honoured by some ECharts templates, are Info on an ECharts target)
    [Test]
    public void Warnings_SortByInvalid()
    {
        var invalid = Bar(i => i.ChartSpec.Encodings["x"] = new ChartEncoding { Field = "city", SortBy = "revenue" });
        AssertOne(invalid, null, "sort_by_invalid", IssueSeverity.Warning);
        Assert.That(Issues(invalid, null, "sort_by_invalid").Single().Path, Is.EqualTo("chart_spec.encodings.x.sortBy"));

        AssertNone(Bar(i => i.ChartSpec.Encodings["x"] = new ChartEncoding { Field = "city", SortBy = "y" }), null, "sort_by_invalid");
        AssertNone(Bar(i => i.ChartSpec.Encodings["x"] = new ChartEncoding { Field = "city", SortBy = SortBy.ExplicitOrder("Boston", "Seattle") }), null, "sort_by_invalid");
        AssertOne(Bar(i => i.ChartSpec.Encodings["x"] = new ChartEncoding { Field = "city", SortBy = "\"Boston\"" }), null, "sort_by_invalid", IssueSeverity.Warning);

        var bySize = Bar(i => i.ChartSpec.Encodings["x"] = new ChartEncoding { Field = "city", SortBy = "size" });
        AssertOne(bySize, FlintBackend.ECharts, "sort_by_invalid", IssueSeverity.Info);
        AssertOne(bySize, FlintBackend.VegaLite, "sort_by_invalid", IssueSeverity.Warning);
        AssertOne(bySize, null, "sort_by_invalid", IssueSeverity.Warning);
    }

    // source: flint-chart-mcp 0.5.1 server.js requiredChannels/hasEncodingBinding (x and y when the template declares both;
    // metric and value on KPI Card; a binding is a non-blank field or aggregate count) and design §6 Info
    // required_channel_missing (the assemblers accept it)
    [Test]
    public void Infos_RequiredChannelMissing()
    {
        var noY = Bar(i => i.ChartSpec.Encodings.Remove("y"));
        AssertOne(noY, null, "required_channel_missing", IssueSeverity.Info);
        Assert.That(Issues(noY, null, "required_channel_missing").Single().Channel, Is.EqualTo(Channel.Y));
        AssertNone(Bar(i => i.ChartSpec.Encodings["y"] = new ChartEncoding { Aggregate = Aggregate.Count }), null, "required_channel_missing");
        AssertOne(Bar(i => i.ChartSpec.Encodings["y"] = " "), null, "required_channel_missing", IssueSeverity.Info);

        var kpi = Bar(i =>
        {
            i.ChartSpec.ChartType = ChartType.KpiCard;
            i.ChartSpec.Encodings.Clear();
            i.ChartSpec.Encodings["metric"] = "city";
        });
        Assert.That(Issues(kpi, null, "required_channel_missing").Select(i => i.Channel), Is.EqualTo(new Channel?[] { Channel.Value }));
        AssertNone(Bar(i => i.ChartSpec.ChartType = ChartType.Histogram), null, "required_channel_missing");
    }

    // source: design §6 Info data_values_and_url (the assemblers read data.values only; part 01 §2)
    [Test]
    public void Infos_DataValuesAndUrl()
    {
        var input = Bar(i => i.Data = FlintJson.Deserialize("""{"data":{"values":[{"city":"A","revenue":1}],"url":"https://example.com/rows.json"},"chart_spec":{"chartType":"Bar Chart","encodings":{"x":"city","y":"revenue"}}}""").Data);
        AssertOne(input, null, "data_values_and_url", IssueSeverity.Info);
        AssertNone(Bar(), null, "data_values_and_url");
    }

    // source: flint-chart-mcp 0.5.1 server.js MAX_DATA_ROWS = 1e5 (more rows are rejected) and design §6 Info too_many_rows
    [Test]
    public void Infos_TooManyRows()
    {
        static ChartAssemblyInput WithRows(int count) => Bar(i => i.Data = ChartData.FromJson(new JsonArray(Enumerable.Range(0, count)
            .Select(n => (JsonNode)new JsonObject { ["city"] = "A", ["revenue"] = n }).ToArray())));
        AssertOne(WithRows(100_001), null, "too_many_rows", IssueSeverity.Info);
        AssertNone(WithRows(100_000), null, "too_many_rows");
    }

    // source: design §6 Info semantic_type_without_field (an annotation upstream never reads: the key is not a field of the
    // first row)
    [Test]
    public void Infos_SemanticTypeWithoutField()
    {
        var input = Bar(i => i.SemanticTypes = new() { ["city"] = SemanticType.City, ["profit"] = SemanticType.Profit });
        AssertOne(input, null, "semantic_type_without_field", IssueSeverity.Info);
        Assert.That(Issues(input, null, "semantic_type_without_field").Single().Path, Is.EqualTo("semantic_types.profit"));
    }

    // source: ir/flint-ir.json (Line Chart declares showPoints on some backends; Bar Chart never declares interpolate),
    // catalog hostStateKeys (sort is exempt), and design §6 Info unknown_chart_property
    [Test]
    public void Infos_UnknownChartProperty()
    {
        var undeclared = Bar(i => i.ChartSpec.ChartProperties = new() { ["interpolate"] = "linear", ["sort"] = "x" });
        AssertOne(undeclared, null, "unknown_chart_property", IssueSeverity.Info);
        Assert.That(Issues(undeclared, null, "unknown_chart_property").Single().Path, Is.EqualTo("chart_spec.chartProperties.interpolate"));

        var line = Line(i => i.ChartSpec.ChartProperties = new() { ["showPoints"] = true });
        foreach (var backend in Enum.GetValues<FlintBackend>())
        {
            if (IrProperty("Line Chart", backend, "showPoints") is not null)
            {
                AssertNone(line, backend, "unknown_chart_property");
            }
            else
            {
                AssertOne(line, backend, "unknown_chart_property", IssueSeverity.Info);
            }
        }
    }

    // source: ir/flint-ir.json (Bar Chart cornerRadius is continuous with a declared min and max) and design §6 Info
    // continuous_out_of_range (normalize-properties.ts checks discrete values only, so upstream passes it through)
    [Test]
    public void Infos_ContinuousOutOfRange()
    {
        var row = IrProperty("Bar Chart", FlintBackend.VegaLite, "cornerRadius")!;
        var (min, max) = (row["min"]!.GetValue<double>(), row["max"]!.GetValue<double>());
        AssertOne(Bar(i => i.ChartSpec.ChartProperties = new() { ["cornerRadius"] = max + 1 }), FlintBackend.VegaLite, "continuous_out_of_range", IssueSeverity.Info);
        AssertOne(Bar(i => i.ChartSpec.ChartProperties = new() { ["cornerRadius"] = min - 1 }), FlintBackend.VegaLite, "continuous_out_of_range", IssueSeverity.Info);
        AssertNone(Bar(i => i.ChartSpec.ChartProperties = new() { ["cornerRadius"] = max }), FlintBackend.VegaLite, "continuous_out_of_range");
    }

    // source: catalog backendsApplyingTheme (vegalite, plotly) and design §6 Info theme_ignored_by_backend
    [Test]
    public void Infos_ThemeIgnoredByBackend()
    {
        var input = Bar(i => i.ThemeSpec = ThemePreset.Economist);
        AssertOne(input, FlintBackend.ECharts, "theme_ignored_by_backend", IssueSeverity.Info);
        AssertNone(input, FlintBackend.VegaLite, "theme_ignored_by_backend");
        AssertOne(input, null, "theme_ignored_by_backend", IssueSeverity.Info);
        AssertNone(Bar(i => i.ThemeSpec = new ThemePreset("no-such-preset")), FlintBackend.ECharts, "theme_ignored_by_backend");
    }

    // source: catalog ignoredOptions (five options the four layout backends overwrite), AssembleOptions remarks (the Excel
    // backend reads no option) and design §6 Info option_overridden_by_backend
    [Test]
    public void Infos_OptionOverriddenByBackend()
    {
        var input = Bar(i => i.Options = new AssembleOptions { MaxStretchX = 2, AddTooltips = true });
        Assert.That(Issues(input, FlintBackend.VegaLite, "option_overridden_by_backend").Select(i => i.Path), Is.EqualTo(new[] { "options.maxStretchX" }));
        Assert.That(Issues(input, FlintBackend.Excel, "option_overridden_by_backend").Select(i => i.Path), Is.EquivalentTo(new[] { "options.maxStretchX", "options.addTooltips" }));
        Assert.That(Issues(input, null, "option_overridden_by_backend"), Has.Exactly(2).Matches<ValidationIssue>(i => i.Severity == IssueSeverity.Info));
    }

    public static IEnumerable<TestCaseData> RowValueInfos()
    {
        static TestCaseData Case(string name, string code, JsonNode? value, string? semanticType = null) =>
            new TestCaseData(code, value, semanticType).SetName($"Infos_RowValue({name})");

        yield return Case("long beyond 2^53", "large_integer", JsonValue.Create(9_007_199_254_740_993L));
        yield return Case("DateTime Unspecified", "unspecified_datetime", "2024-01-15T10:00:00");
        yield return Case("TimeSpan of a day or more", "timespan_as_string", "1.02:03:04");
        yield return Case("null in a Date column", "null_in_temporal_field", null, "Date");
        yield return Case("nested object", "nested_value", new JsonObject { ["a"] = 1 });
        yield return Case("collection", "nested_value", new JsonArray(1, 2));
    }

    // source: design part 2 §5.3 (.NET type to JSON table: the Info for each row value upstream may misread)
    [TestCaseSource(nameof(RowValueInfos))]
    public void Infos_RowValue(string code, JsonNode? value, string? semanticType)
    {
        var input = Bar(i =>
        {
            i.Data = ChartData.FromJson([new JsonObject { ["city"] = "A", ["revenue"] = 1, ["v"] = 1 }, new JsonObject { ["city"] = "B", ["revenue"] = 2, ["v"] = value }]);
            if (semanticType is not null)
            {
                i.SemanticTypes = new() { ["v"] = new SemanticType(semanticType) };
            }
        });
        AssertOne(input, null, code, IssueSeverity.Info);
        var issue = Issues(input, null, code).Single();
        Assert.That((issue.Path, issue.Field), Is.EqualTo(("data.values[1].v", "v")));
    }

    // source: design part 2 §5.3 (a time of day "10:00:00" infers nominal and a DateTimeOffset carries its offset, so
    // neither is reported; integers up to 2^53 are exact in JavaScript)
    [Test]
    public void Infos_RowValue_ExactValuesAreNotReported()
    {
        var input = Bar(i => i.Data = ChartData.FromJson(
        [
            new JsonObject { ["city"] = "A", ["revenue"] = 9_007_199_254_740_992L, ["t"] = "10:00:00", ["at"] = "2024-01-15T10:00:00Z", ["d"] = "2024-01-15" },
        ]));
        Assert.That(ChartValidator.Validate(input).Issues, Is.Empty);
    }

    // source: design §6 (ThrowIfInvalid throws FlintValidationException carrying every issue, not only the errors)
    [Test]
    public void ThrowIfInvalid_WithError_ThrowsWithEveryIssue()
    {
        var result = ChartValidator.Validate(Bar(i =>
        {
            i.ChartSpec.ChartType = new ChartType("Image Chart");
            i.ChartSpec.CanvasSize = new ChartSize(5000, 300);
        }));
        Assert.That(result.IsValid, Is.False);
        Assert.That((Action)(() => result.ThrowIfInvalid()), Throws.TypeOf<FlintValidationException>()
            .With.Property(nameof(FlintValidationException.Issues)).EquivalentTo(result.Issues)
            .And.Message.Contains("unknown_chart_type"));
        Assert.That(result.Issues.Select(i => i.Code), Is.EquivalentTo(new[] { "unknown_chart_type", "canvas_too_large" }));
    }

    // source: Excel templates of flint-chart 0.5.1 (scatter.ts requireQuantitativeAxes; line.ts continuous color) with the
    // types upstream resolves from the registered semantic types (Category nominal, Amount quantitative)
    [Test]
    public void Excel_ValidateConditions_UseResolvedTypes()
    {
        var scatter = Bar(i =>
        {
            i.ChartSpec.ChartType = ChartType.ScatterPlot;
            i.SemanticTypes = new() { ["city"] = SemanticType.Category, ["revenue"] = SemanticType.Amount };
        });
        Assert.That(Issues(scatter, FlintBackend.Excel, "excel_unsupported").Single().Message, Does.Contain("quantitative x and y"));

        var line = Line(i =>
        {
            i.ChartSpec.Encodings["color"] = "cost";
            i.SemanticTypes = new() { ["cost"] = SemanticType.Amount };
        });
        Assert.That(Issues(line, FlintBackend.Excel, "excel_unsupported").Single().Channel, Is.EqualTo(Channel.Color));
        Assert.That(ChartValidator.Validate(line, FlintBackend.VegaLite).IsValid, Is.True);
    }

    // source: design §6 (a rule that needs a type it cannot resolve skips): "Stage 1" parses as a date in V8 only, so
    // whether the column is temporal (an Excel throw for a line colour) cannot be decided without a JavaScript engine
    [Test]
    public void Excel_UndecidableType_ReportsNoError()
    {
        var input = Line(i =>
        {
            i.Data = ChartData.FromJson(new JsonArray(Enumerable.Range(1, 20).Select(n => (JsonNode)new JsonObject
            {
                ["city"] = $"C{n}", ["revenue"] = n, ["stage"] = $"Stage {n}",
            }).ToArray()));
            i.ChartSpec.Encodings["color"] = "stage";
            i.SemanticTypes = new() { ["stage"] = SemanticType.Date };
        });
        AssertNone(input, FlintBackend.Excel, "excel_unsupported");
    }
}
