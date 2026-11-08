using System.Text.Json.Nodes;
using Graspea.Flint.Tests.Support;
using Graspea.Flint.Validation;

namespace Graspea.Flint.Tests.Validation;

public sealed partial class ChartValidatorTests
{
    private static ChartAssemblyInput Pie(Action<ChartAssemblyInput> change)
    {
        var input = Bar(i => i.ChartSpec.ChartType = ChartType.PieChart);
        input.ChartSpec.Encodings.Clear();
        change(input);
        return input;
    }

    private static ChartAssemblyInput Scatter(Action<ChartAssemblyInput>? change = null)
    {
        var input = Bar(i =>
        {
            i.ChartSpec.ChartType = ChartType.ScatterPlot;
            i.ChartSpec.Encodings = new() { ["x"] = "revenue", ["y"] = "cost" };
        });
        change?.Invoke(input);
        return input;
    }

    // source: flint-chart 1.0.1 src/image-charts/assemble.ts:192-194 (a bound column or row throws on every chart type)
    [Test]
    public void ImageCharts_Facet_IsImageChartsUnsupported()
    {
        var input = Bar(i => i.ChartSpec.Encodings["column"] = "city");
        Assert.That(Issues(input, FlintBackend.ImageCharts, "image_charts_unsupported"),
            Has.Exactly(1).Matches<ValidationIssue>(i => i.Severity == IssueSeverity.Error && i.Channel == new Channel("column")));
        AssertNone(input, FlintBackend.ImageCharts, "unsupported_channel");
        AssertNone(input, FlintBackend.VegaLite, "image_charts_unsupported");
    }

    // source: assemble.ts:69-71 and :336-340 (an array encoding has no field, so a static series on y leaves the measure
    // unresolved; one on color is no series and assembles)
    [Test]
    public void ImageCharts_StaticSeries_IsAnUnboundChannel()
    {
        var y = Line(i => i.ChartSpec.Encodings["y"] = EncodingValue.Series(new() { Field = "revenue" }, new() { Field = "cost" }));
        AssertOne(y, FlintBackend.ImageCharts, "image_charts_unsupported", IssueSeverity.Error);
        var color = Line(i => i.ChartSpec.Encodings["color"] = EncodingValue.Series(new() { Field = "revenue" }, new() { Field = "cost" }));
        Assert.That(ChartValidator.Validate(color, FlintBackend.ImageCharts).IsValid, Is.True);
        AssertNone(color, FlintBackend.ImageCharts, "series_channel_not_xy");
    }

    // source: assemble.ts:265-269 (slice = color ?? x, value = size ?? theta ?? y: a missing field falls through, an empty
    // one does not)
    [Test]
    public void ImageCharts_PartToWhole_FallsThroughOnlyAMissingField()
    {
        var noField = Pie(i =>
        {
            i.ChartSpec.Encodings["color"] = new ChartEncoding { Type = EncodingType.Nominal };
            i.ChartSpec.Encodings["x"] = "city";
            i.ChartSpec.Encodings["size"] = "revenue";
        });
        Assert.That(ChartValidator.Validate(noField, FlintBackend.ImageCharts).IsValid, Is.True);
        AssertOne(noField, FlintBackend.Excel, "excel_unsupported", IssueSeverity.Error);

        var empty = Pie(i =>
        {
            i.ChartSpec.Encodings["color"] = new ChartEncoding { Field = "" };
            i.ChartSpec.Encodings["x"] = "city";
            i.ChartSpec.Encodings["size"] = "revenue";
        });
        AssertOne(empty, FlintBackend.ImageCharts, "image_charts_unsupported", IssueSeverity.Error);
        AssertOne(Pie(i => i.ChartSpec.Encodings["color"] = "city"), FlintBackend.ImageCharts, "image_charts_unsupported", IssueSeverity.Error);

        // the value order is size, then theta, then y: an empty size is the value even with theta bound, an empty theta
        // even with y bound
        AssertOne(Pie(i =>
        {
            i.ChartSpec.Encodings["color"] = "city";
            i.ChartSpec.Encodings["size"] = new ChartEncoding { Field = "" };
            i.ChartSpec.Encodings["theta"] = "revenue";
        }), FlintBackend.ImageCharts, "image_charts_unsupported", IssueSeverity.Error);
        AssertOne(Pie(i =>
        {
            i.ChartSpec.Encodings["color"] = "city";
            i.ChartSpec.Encodings["theta"] = new ChartEncoding { Field = "" };
            i.ChartSpec.Encodings["y"] = "revenue";
        }), FlintBackend.ImageCharts, "image_charts_unsupported", IssueSeverity.Error);
        Assert.That(ChartValidator.Validate(Pie(i =>
        {
            i.ChartSpec.Encodings["color"] = "city";
            i.ChartSpec.Encodings["theta"] = "revenue";
        }), FlintBackend.ImageCharts).IsValid, Is.True);
    }

    // source: assemble.ts:290-294 (quantitative x and y, from the resolved types); without rows only the spec part is
    // decided, and untyped fields with no rows are nominal (semantic-types.ts:274)
    [Test]
    public void ImageCharts_Scatter_NeedsQuantitativeXAndY()
    {
        Assert.That(ChartValidator.Validate(Scatter(), FlintBackend.ImageCharts).IsValid, Is.True);
        var nominal = Scatter(i => i.ChartSpec.Encodings["x"] = "city");
        Assert.That(Issues(nominal, FlintBackend.ImageCharts, "image_charts_unsupported").Single().Channel, Is.EqualTo(Channel.X));
        Assert.That(ChartTemplate.Create<JsonObject>(nominal).Validate(FlintBackend.ImageCharts).IsValid, Is.True);

        var empty = Scatter(i => i.Data = ChartData.FromJson([]));
        AssertOne(empty, FlintBackend.ImageCharts, "data_empty", IssueSeverity.Error);
        AssertOne(Bar(i => i.Data = ChartData.FromJson([])), FlintBackend.ImageCharts, "data_empty", IssueSeverity.Info);
        AssertOne(Scatter(i => i.Data = ChartData.FromUrl("https://example.com/rows.json")), FlintBackend.ImageCharts, "data_url_only", IssueSeverity.Error);
    }

    // source: assemble.ts:67-73 (encodings ?? {}, a null channel skipped) and :336-340 (no field throws)
    [Test]
    public void ImageCharts_EmptyEncodingsThrow_NullChannelIsIgnored()
    {
        AssertOne(Bar(i => i.ChartSpec.Encodings.Clear()), FlintBackend.ImageCharts, "encodings_empty", IssueSeverity.Error);
        var color = Bar(i => i.ChartSpec.Encodings["color"] = null!);
        AssertOne(color, FlintBackend.ImageCharts, "channel_null", IssueSeverity.Info);
        Assert.That(ChartValidator.Validate(color, FlintBackend.ImageCharts).IsValid, Is.True);
    }

    // source: assemble.ts (no read of theme_spec, options, chartProperties, sortBy or sortOrder; the artifact has no
    // warnings member)
    [Test]
    public void ImageCharts_ReadsNoThemeOptionPropertyOrSort()
    {
        AssertOne(Bar(i => i.ThemeSpec = new ThemePreset("no-such-preset")), FlintBackend.ImageCharts, "unknown_theme_preset", IssueSeverity.Info);
        AssertOne(Bar(i => i.ThemeSpec = ThemePreset.Economist), FlintBackend.ImageCharts, "theme_ignored_by_backend", IssueSeverity.Info);
        AssertOne(Bar(i => i.Options = new AssembleOptions { AddTooltips = true }), FlintBackend.ImageCharts, "option_overridden_by_backend", IssueSeverity.Info);
        AssertNone(Line(i => i.ChartSpec.ChartProperties = new() { ["interpolate"] = "zzz" }), FlintBackend.ImageCharts, "invalid_option_value");
        AssertNone(Bar(i => i.ChartSpec.Encodings["x"] = new ChartEncoding { Field = "city", SortBy = "size" }), FlintBackend.ImageCharts, "sort_by_invalid");
    }

    // source: chart-types.ts:33-44 (ten chart types) and ir/flint-ir.json
    [Test]
    public void ImageCharts_ChartTypeOutsideItsMap_IsNotOnBackend()
    {
        var issue = Issues(Bar(i => i.ChartSpec.ChartType = ChartType.Heatmap), FlintBackend.ImageCharts, "chart_type_not_on_backend").Single();
        Assert.That(issue.Message, Does.Contain("no image-charts template"));
    }

    // source: review D2-6 (a reason reads as one sentence after the backend name)
    [Test]
    public void ImageCharts_Reasons_ReadAsOneSentence()
    {
        var inputs = new[]
        {
            Bar(i => i.ChartSpec.Encodings["column"] = "city"),
            Line(i => i.ChartSpec.Encodings.Remove("y")),
            Scatter(i => i.Data = ChartData.FromJson([])),
            Pie(i => i.ChartSpec.Encodings["color"] = "city"),
        };
        foreach (var input in inputs)
        {
            var messages = Issues(input, FlintBackend.ImageCharts).Where(i => i.Code is "image_charts_unsupported" or "data_empty" && i.Severity == IssueSeverity.Error)
                .Select(i => i.Message).ToArray();
            Assert.That(messages, Is.Not.Empty, input.ChartSpec.ChartType.Name);
            Assert.That(messages, Has.None.Contains("Image-Charts Image-Charts").And.None.Contains("it Image-Charts"));
        }
    }

    // source: chart-types.ts:42 (Donut Chart is noAxes, as Pie) and assemble.ts:239, :265-269 (one part-to-whole rule)
    [Test]
    public void ImageCharts_Donut_NeedsASliceAndAValue()
    {
        AssertOne(Pie(i => { i.ChartSpec.ChartType = ChartType.DonutChart; i.ChartSpec.Encodings["color"] = "city"; }),
            FlintBackend.ImageCharts, "image_charts_unsupported", IssueSeverity.Error);
        AssertOne(Pie(i => { i.ChartSpec.ChartType = ChartType.DonutChart; i.ChartSpec.Encodings["size"] = "revenue"; }),
            FlintBackend.ImageCharts, "image_charts_unsupported", IssueSeverity.Error);
        Assert.That(ChartValidator.Validate(Pie(i =>
        {
            i.ChartSpec.ChartType = ChartType.DonutChart;
            i.ChartSpec.Encodings["color"] = "city";
            i.ChartSpec.Encodings["theta"] = "revenue";
        }), FlintBackend.ImageCharts).IsValid, Is.True);
    }

    // source: assemble.ts:239-248 and :333-340 (every chart type of the axes family throws without a category or a
    // measure; a horizontal bar only swaps them)
    [Test]
    public void ImageCharts_AxesFamily_NeedsXAndY(
        [Values("Bar Chart", "Grouped Bar Chart", "Stacked Bar Chart", "Line Chart", "Sparkline", "Area Chart", "Radar Chart")] string chartType,
        [Values("x", "y")] string removed)
    {
        var input = Bar(i => { i.ChartSpec.ChartType = new ChartType(chartType); i.ChartSpec.Encodings.Remove(removed); });
        Assert.That(Issues(input, FlintBackend.ImageCharts, "image_charts_unsupported"),
            Has.Exactly(1).Matches<ValidationIssue>(i => i.Severity == IssueSeverity.Error && i.Channel == null));
        Assert.That(ChartValidator.Validate(input, FlintBackend.ImageCharts).IsValid, Is.False);
    }

    public static IEnumerable<string> ImageChartsChartTypes() =>
        CommittedIr.Catalog["chartTypes"]!.AsArray().Where(t => t!["backends"]!["image-charts"] is not null).Select(t => t!["name"]!.GetValue<string>());

    // source: assemble.ts:192-194 (a bound column or row throws before any chart-type branch) and ir/flint-ir.json (the
    // ten Image-Charts chart types)
    [TestCaseSource(nameof(ImageChartsChartTypes))]
    public void ImageCharts_Facet_ThrowsOnEveryChartType(string chartType)
    {
        var input = chartType is "Pie Chart" or "Donut Chart"
            ? Pie(i =>
            {
                i.ChartSpec.ChartType = new ChartType(chartType);
                i.ChartSpec.Encodings["color"] = "city";
                i.ChartSpec.Encodings["size"] = "revenue";
                i.ChartSpec.Encodings["row"] = "city";
            })
            : Bar(i => { i.ChartSpec.ChartType = new ChartType(chartType); i.ChartSpec.Encodings["row"] = "city"; });
        Assert.That(Issues(input, FlintBackend.ImageCharts, "image_charts_unsupported"),
            Has.Exactly(1).Matches<ValidationIssue>(i => i.Severity == IssueSeverity.Error && i.Channel == Channel.Row));
        Assert.That(Issues(input, FlintBackend.ImageCharts, "unsupported_channel"), Has.None.Matches<ValidationIssue>(i => i.Channel == Channel.Row));
        Assert.That(ChartValidator.Validate(input, FlintBackend.ImageCharts).IsValid, Is.False);
    }

    // source: assemble.ts:290-294 (!xField || !yField): the reason names the unbound axis; a static series has no field
    [Test]
    public void ImageCharts_Scatter_UnboundAxis_IsAttributedToThatAxis()
    {
        var noY = Issues(Scatter(i => i.ChartSpec.Encodings.Remove("y")), FlintBackend.ImageCharts, "image_charts_unsupported").Single();
        Assert.That(noY.Channel, Is.EqualTo(Channel.Y));
        Assert.That(noY.Path, Is.EqualTo("chart_spec.encodings.y"));
        var noX = Issues(Scatter(i => i.ChartSpec.Encodings.Remove("x")), FlintBackend.ImageCharts, "image_charts_unsupported").Single();
        Assert.That(noX.Channel, Is.EqualTo(Channel.X));
        Assert.That(noX.Path, Is.EqualTo("chart_spec.encodings.x"));
        var series = Scatter(i => i.ChartSpec.Encodings["y"] = EncodingValue.Series(new() { Field = "cost" }, new() { Field = "revenue" }));
        Assert.That(Issues(series, FlintBackend.ImageCharts, "image_charts_unsupported").Single().Channel, Is.EqualTo(Channel.Y));
    }

    // source: resolve-semantics.ts:549-550 (an explicit type wins over inference; the ISO hack only turns quantitative
    // into temporal, an aggregate forces quantitative), so an explicit type other than quantitative fails without rows
    [Test]
    public void ImageCharts_Scatter_ExplicitNonQuantitativeType_IsDecidedWithoutRows()
    {
        var nominal = Scatter(i => i.ChartSpec.Encodings["x"] = new ChartEncoding { Field = "revenue", Type = EncodingType.Nominal });
        var template = ChartTemplate.Create<JsonObject>(nominal).Validate(FlintBackend.ImageCharts);
        Assert.That(template.IsValid, Is.False);
        Assert.That(template.Issues.Where(i => i.Code == "image_charts_unsupported"),
            Has.Exactly(1).Matches<ValidationIssue>(i => i.Severity == IssueSeverity.Error && i.Channel == Channel.X));

        var temporal = Scatter(i => i.ChartSpec.Encodings["y"] = new ChartEncoding { Field = "cost", Type = EncodingType.Temporal });
        Assert.That(ChartTemplate.Create<JsonObject>(temporal).Validate(FlintBackend.ImageCharts).Issues.Where(i => i.Code == "image_charts_unsupported").Select(i => i.Channel),
            Is.EqualTo(new Channel?[] { Channel.Y }));

        var streamed = StreamedBar(i =>
        {
            i.ChartSpec.ChartType = ChartType.ScatterPlot;
            i.ChartSpec.Encodings = new() { ["x"] = new ChartEncoding { Field = "revenue", Type = EncodingType.Nominal }, ["y"] = "cost" };
        });
        AssertOne(streamed, FlintBackend.ImageCharts, "image_charts_unsupported", IssueSeverity.Error);

        var aggregated = Scatter(i => i.ChartSpec.Encodings["x"] = new ChartEncoding { Field = "revenue", Type = EncodingType.Nominal, Aggregate = Aggregate.Sum });
        Assert.That(ChartValidator.Validate(aggregated, FlintBackend.ImageCharts).IsValid, Is.True);
        Assert.That(ChartTemplate.Create<JsonObject>(aggregated).Validate(FlintBackend.ImageCharts).IsValid, Is.True);

        var noRows = Scatter(i =>
        {
            i.Data = ChartData.FromJson([]);
            i.ChartSpec.Encodings["x"] = new ChartEncoding { Field = "revenue", Type = EncodingType.Ordinal };
            i.ChartSpec.Encodings["y"] = new ChartEncoding { Field = "cost", Type = EncodingType.Quantitative };
        });
        Assert.That(Issues(noRows, FlintBackend.ImageCharts, "image_charts_unsupported").Single().Channel, Is.EqualTo(Channel.X));
        Assert.That(Issues(noRows, FlintBackend.ImageCharts, "data_empty"), Has.None.Matches<ValidationIssue>(i => i.Severity == IssueSeverity.Error));
    }

    // source: the spec rules (faceting, the part-to-whole slice and value, an unbound Scatter axis, the axes category and
    // measure; assemble.ts:192-194, :265-269, :290-294, :333-340) need no rows, so a template and streamed rows report them
    [Test]
    public void ImageCharts_SpecRules_AreReportedWithoutRows()
    {
        IEnumerable<ValidationIssue> Template(ChartAssemblyInput input) =>
            ChartTemplate.Create<JsonObject>(input).Validate(FlintBackend.ImageCharts).Issues.Where(i => i.Code == "image_charts_unsupported" && i.Severity == IssueSeverity.Error);

        Assert.That(Template(Bar(i => i.ChartSpec.Encodings["column"] = "city")), Has.Exactly(1).Matches<ValidationIssue>(i => i.Channel == Channel.Column));
        Assert.That(Template(Pie(i => i.ChartSpec.Encodings["color"] = "city")), Has.Exactly(1).Matches<ValidationIssue>(i => i.Channel == null));
        Assert.That(Template(Scatter(i => i.ChartSpec.Encodings.Remove("y"))), Has.Exactly(1).Matches<ValidationIssue>(i => i.Channel == Channel.Y));
        Assert.That(Template(Line(i => i.ChartSpec.Encodings.Remove("y"))), Has.Exactly(1).Matches<ValidationIssue>(i => i.Channel == null));
        AssertOne(StreamedBar(i => i.ChartSpec.Encodings["column"] = "city"), FlintBackend.ImageCharts, "image_charts_unsupported", IssueSeverity.Error);

        // a Sparkline has no Excel template, so on the default targets Image-Charts alone makes the faceted template invalid
        var sparkline = Bar(i => { i.ChartSpec.ChartType = ChartType.Sparkline; i.ChartSpec.Encodings["column"] = "city"; });
        Assert.That(ChartTemplate.Create<JsonObject>(sparkline).Validate().IsValid, Is.False);
    }

    // source: assemble.ts:205-210 (a group with a field is the series of every family but part-to-whole) and :265-266
    // (x is the slice when color has no field, y the value when neither size nor theta has one; an empty field does not
    // fall through); the curated template leaves them undeclared (generator/overrides.json imageChartsChannels)
    [Test]
    public void ImageCharts_ReadUndeclaredChannels_AreNotUnsupported()
    {
        var pie = Pie(i => { i.ChartSpec.Encodings["x"] = "city"; i.ChartSpec.Encodings["y"] = "revenue"; });
        AssertNone(pie, FlintBackend.ImageCharts, "unsupported_channel");
        AssertNone(pie, null, "unsupported_channel");
        Assert.That(ChartValidator.Validate(pie, FlintBackend.ImageCharts).IsValid, Is.True);

        var line = Line(i => i.ChartSpec.Encodings["group"] = "city");
        AssertNone(line, FlintBackend.ImageCharts, "unsupported_channel");
        AssertNone(line, null, "unsupported_channel");
        AssertOne(line, FlintBackend.VegaLite, "unsupported_channel", IssueSeverity.Warning);

        var shadowed = Pie(i =>
        {
            i.ChartSpec.Encodings["color"] = "city";
            i.ChartSpec.Encodings["x"] = "city";
            i.ChartSpec.Encodings["size"] = "revenue";
            i.ChartSpec.Encodings["y"] = "revenue";
        });
        Assert.That(Issues(shadowed, FlintBackend.ImageCharts, "unsupported_channel").Select(i => i.Channel), Is.EquivalentTo(new Channel?[] { Channel.X, Channel.Y }));
        var emptyColor = Pie(i =>
        {
            i.ChartSpec.Encodings["color"] = new ChartEncoding { Field = "" };
            i.ChartSpec.Encodings["x"] = "city";
            i.ChartSpec.Encodings["theta"] = "revenue";
        });
        Assert.That(Issues(emptyColor, FlintBackend.ImageCharts, "unsupported_channel").Select(i => i.Channel), Is.EqualTo(new Channel?[] { Channel.X }));
        AssertOne(Line(i => i.ChartSpec.Encodings["group"] = new ChartEncoding { Type = EncodingType.Nominal }), FlintBackend.ImageCharts, "unsupported_channel", IssueSeverity.Warning);
        AssertOne(Pie(i =>
        {
            i.ChartSpec.Encodings["color"] = "city";
            i.ChartSpec.Encodings["size"] = "revenue";
            i.ChartSpec.Encodings["group"] = "city";
        }), FlintBackend.ImageCharts, "unsupported_channel", IssueSeverity.Warning);
    }

    // source: assemble.ts:69-71, :185-198 and resolve-semantics.ts (an array encoding has no field, so the native
    // assemblers never look up the annotation of a static-series entry; the layout backends do in normalizeStaticSeries)
    [Test]
    public void NativeBackends_SeriesOnlyField_IsNotReservedSemanticType()
    {
        var input = Scatter(i =>
        {
            i.ChartSpec.Encodings["color"] = EncodingValue.Series(new() { Field = "profit" }, new() { Field = "cost" });
            i.SemanticTypes = new() { ["profit"] = new SemanticAnnotation { Type = new SemanticType("constructor") } };
        });
        foreach (var native in new[] { FlintBackend.ImageCharts, FlintBackend.Excel })
        {
            AssertNone(input, native, "reserved_semantic_type");
            AssertOne(input, native, "unknown_semantic_type", IssueSeverity.Warning);
            Assert.That(ChartValidator.Validate(input, native).IsValid, Is.True, native.ToString());
        }

        AssertOne(input, FlintBackend.VegaLite, "reserved_semantic_type", IssueSeverity.Error);
        AssertOne(input, null, "reserved_semantic_type", IssueSeverity.Error);
    }
}
