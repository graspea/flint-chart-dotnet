using System.Text.Json.Nodes;
using Graspea.Flint.Validation;

namespace Graspea.Flint.Tests.Validation;

public sealed partial class ChartValidatorTests
{
    private static ChartEncoding Typed(string field, EncodingType type, Aggregate? aggregate = null) =>
        new() { Field = field, Type = type, Aggregate = aggregate };

    private static ChartEncoding Q(string field) => Typed(field, EncodingType.Quantitative);

    private static ChartEncoding N(string field) => Typed(field, EncodingType.Nominal);

    private static ChartEncoding O(string field) => Typed(field, EncodingType.Ordinal);

    private static ChartEncoding Tm(string field) => Typed(field, EncodingType.Temporal);

    private static ChartEncoding Sum(string field, EncodingType type) => Typed(field, type, Aggregate.Sum);

    private static TestCaseData SpecCase(string name, string chartType, (string Channel, EncodingValue Value)[] encodings, string? channel, string reason, string? totals = null) =>
        new TestCaseData(
            (Action<ChartAssemblyInput>)(i =>
            {
                i.ChartSpec.ChartType = new ChartType(chartType);
                i.ChartSpec.Encodings = new();
                foreach (var (ch, value) in encodings)
                {
                    i.ChartSpec.Encodings[ch] = value;
                }

                if (totals is not null)
                {
                    i.ChartSpec.ChartProperties = new() { ["totals"] = totals };
                }
            }),
            channel,
            reason).SetArgDisplayNames(name);

    private static IEnumerable<TestCaseData> SpecDecidedExcelRules()
    {
        const string XyReason = "quantitative x and y fields for a native Excel XY chart";
        const string LineColor = "continuous color on a native Excel line chart";
        const string OneMeasure = "one quantitative measure axis";

        yield return SpecCase("area y nominal", "Area Chart", [("x", "city"), ("y", N("revenue"))], "y", "a quantitative y field for a native Excel area chart"); // area.ts:10
        yield return SpecCase("bar both axes", "Bar Chart", [("x", N("city")), ("y", O("revenue"))], null, OneMeasure); // bar.ts:10
        yield return SpecCase("grouped both axes", "Grouped Bar Chart", [("x", N("city")), ("y", O("revenue")), ("group", "city")], null, OneMeasure); // bar.ts:19
        yield return SpecCase("grouped continuous group", "Grouped Bar Chart", [("x", "city"), ("y", "revenue"), ("group", Tm("cost"))], "group", "continuous grouping"); // bar.ts:20
        yield return SpecCase("stacked both axes", "Stacked Bar Chart", [("x", N("city")), ("y", O("revenue"))], null, OneMeasure); // bar.ts:29
        yield return SpecCase("stacked continuous color", "Stacked Bar Chart", [("x", "city"), ("y", "revenue"), ("color", Tm("cost"))], "color", "continuous color in a native Excel stacked bar chart"); // bar.ts:30
        yield return SpecCase("line y ordinal", "Line Chart", [("x", "city"), ("y", O("revenue"))], "y", "a quantitative y field for a native Excel line chart"); // line.ts:11
        yield return SpecCase("line temporal color", "Line Chart", [("x", "city"), ("y", "revenue"), ("color", Tm("cost"))], "color", LineColor); // line.ts:14
        yield return SpecCase("line quantitative color", "Line Chart", [("x", "city"), ("y", "revenue"), ("color", Q("cost"))], "color", LineColor); // line.ts:14
        yield return SpecCase("line aggregated color", "Line Chart", [("x", "city"), ("y", "revenue"), ("color", Sum("city", EncodingType.Nominal))], "color", LineColor); // line.ts:14
        yield return SpecCase("scatter x nominal", "Scatter Plot", [("x", N("revenue")), ("y", Q("cost"))], "x", XyReason); // types.ts:25
        yield return SpecCase("scatter y ordinal", "Scatter Plot", [("x", Q("revenue")), ("y", O("cost"))], "y", XyReason); // types.ts:25
        yield return SpecCase("scatter temporal color", "Scatter Plot", [("x", "revenue"), ("y", "cost"), ("color", Tm("cost"))], "color", "continuous color in a native Excel Scatter or Bubble chart"); // scatter.ts:12
        yield return SpecCase("connected x nominal", "Connected Scatter Plot", [("x", N("revenue")), ("y", Q("cost")), ("order", "revenue")], "x", XyReason); // types.ts:25
        yield return SpecCase("connected temporal color", "Connected Scatter Plot", [("x", "revenue"), ("y", "cost"), ("order", "revenue"), ("color", Tm("cost"))], "color", "continuous color in a native Excel connected scatter chart"); // scatter.ts:26
        yield return SpecCase("boxplot temporal x", "Boxplot", [("x", Tm("city")), ("y", "revenue")], "x", "a categorical x field for a native Excel box and whisker chart"); // boxplot.ts:12
        yield return SpecCase("boxplot y nominal", "Boxplot", [("x", "city"), ("y", N("revenue"))], "y", "a quantitative y field for a native Excel box and whisker chart"); // boxplot.ts:15
        yield return SpecCase("candlestick x nominal", "Candlestick Chart", [("x", N("city")), ("open", "revenue"), ("high", "revenue"), ("low", "revenue"), ("close", "revenue")], "x", "a temporal or ordered x field"); // candlestick.ts:23
        yield return SpecCase("candlestick open nominal", "Candlestick Chart", [("x", O("city")), ("open", N("revenue")), ("high", "revenue"), ("low", "revenue"), ("close", "revenue")], "open", "a quantitative open field"); // candlestick.ts:27
        yield return SpecCase("funnel aggregated stage", "Funnel Chart", [("y", Sum("city", EncodingType.Nominal)), ("size", "revenue")], "y", "a categorical y stage field"); // funnel.ts:12
        yield return SpecCase("funnel size nominal", "Funnel Chart", [("y", "city"), ("size", N("revenue"))], "size", "a quantitative size field for a native Excel funnel chart"); // funnel.ts:15
        yield return SpecCase("funnel no size", "Funnel Chart", [("y", "city")], "size", "a quantitative size field for a native Excel funnel chart"); // funnel.ts:15
        yield return SpecCase("histogram value ordinal", "Histogram", [("x", O("revenue"))], "x", "a quantitative value field for a native Excel histogram"); // histogram.ts:19
        yield return SpecCase("pyramid both axes", "Pyramid Chart", [("x", N("city")), ("y", O("revenue")), ("color", "city")], null, "exactly one quantitative measure axis"); // pyramid.ts:25
        yield return SpecCase("pyramid temporal color", "Pyramid Chart", [("x", Sum("revenue", EncodingType.Quantitative)), ("y", N("city")), ("color", Tm("cost"))], "color", "a discrete color field for a native Excel pyramid chart"); // pyramid.ts:35
        yield return SpecCase("radar temporal x", "Radar Chart", [("x", Tm("city")), ("y", "revenue")], "x", "a categorical metric x field"); // radar.ts:23
        yield return SpecCase("radar y nominal", "Radar Chart", [("x", "city"), ("y", N("revenue"))], "y", "a quantitative y field for a native Excel radar chart"); // radar.ts:26
        yield return SpecCase("radar temporal color", "Radar Chart", [("x", "city"), ("y", "revenue"), ("color", Tm("cost"))], "color", "a discrete color field for native Excel radar series"); // radar.ts:29
        yield return SpecCase("sunburst aggregated color", "Sunburst Chart", [("color", Sum("city", EncodingType.Nominal)), ("size", "revenue")], "color", "a categorical color field for a native Excel sunburst chart"); // sunburst.ts:11
        yield return SpecCase("sunburst size nominal", "Sunburst Chart", [("color", "city"), ("size", N("revenue"))], "size", "a quantitative size field for a native Excel sunburst chart"); // sunburst.ts:14
        yield return SpecCase("sunburst no size", "Sunburst Chart", [("color", "city")], "size", "a quantitative size field for a native Excel sunburst chart"); // sunburst.ts:14
        yield return SpecCase("sunburst aggregated group", "Sunburst Chart", [("color", "city"), ("size", "revenue"), ("group", Sum("cost", EncodingType.Ordinal))], "group", "a categorical group field"); // sunburst.ts:17
        yield return SpecCase("sunburst aggregated detail", "Sunburst Chart", [("color", "city"), ("size", "revenue"), ("group", "city"), ("detail", Sum("cost", EncodingType.Ordinal))], "detail", "a categorical detail field for a native Excel sunburst hierarchy"); // sunburst.ts:23
        yield return SpecCase("treemap aggregated color", "Treemap", [("color", Sum("city", EncodingType.Nominal)), ("size", "revenue")], "color", "a categorical color field for a native Excel treemap"); // treemap.ts:11
        yield return SpecCase("treemap size nominal", "Treemap", [("color", "city"), ("size", N("revenue"))], "size", "a quantitative size field for a native Excel treemap"); // treemap.ts:14
        yield return SpecCase("treemap no size", "Treemap", [("color", "city")], "size", "a quantitative size field for a native Excel treemap"); // treemap.ts:14
        yield return SpecCase("treemap aggregated detail", "Treemap", [("color", "city"), ("size", "revenue"), ("detail", Sum("cost", EncodingType.Ordinal))], "detail", "a categorical detail field for a native Excel treemap hierarchy"); // treemap.ts:17
        yield return SpecCase("waterfall y nominal", "Waterfall Chart", [("x", "city"), ("y", N("revenue"))], "y", "a quantitative y field for a native Excel waterfall chart"); // waterfall.ts:14
        yield return SpecCase("waterfall no y", "Waterfall Chart", [("x", "city")], "y", "a quantitative y field for a native Excel waterfall chart"); // waterfall.ts:14
        yield return SpecCase("waterfall aggregated color", "Waterfall Chart", [("x", "city"), ("y", "revenue"), ("color", Sum("cost", EncodingType.Nominal))], "color", "start/delta/end roles"); // waterfall.ts:17
        yield return SpecCase("waterfall explicit last total", "Waterfall Chart", [("x", "city"), ("y", "revenue")], null, "cannot mark a final total", totals: "last"); // waterfall.ts:32-34
    }

    // source: src/excel/templates/*.ts of flint-chart 1.0.1 (each case names its template line) and src/core/resolve-semantics.ts:547-564, :583-589, :682-689 (the spec decides the type)
    [TestCaseSource(nameof(SpecDecidedExcelRules))]
    public void Excel_SpecDecidedRule_IsReportedWithoutRows(Action<ChartAssemblyInput> spec, string? channel, string reason)
    {
        bool Reported(IEnumerable<ValidationIssue> issues) =>
            issues.Any(i => i.Code == "excel_unsupported" && i.Severity == IssueSeverity.Error && i.Channel?.Name == channel
                && i.Message.Contains(reason, StringComparison.Ordinal));

        var noRows = Bar(i =>
        {
            spec(i);
            i.Data = ChartData.FromJson([]);
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Reported(Issues(Bar(spec), FlintBackend.Excel)), Is.True, "with rows");
            Assert.That(Reported(ChartTemplate.Create<JsonObject>(Bar(spec)).Validate(FlintBackend.Excel).Issues), Is.True, "template");
            Assert.That(Reported(Issues(StreamedBar(spec), FlintBackend.Excel)), Is.True, "streamed");
            Assert.That(Reported(Issues(noRows, FlintBackend.Excel)), Is.True, "no rows");
            Assert.That(Issues(noRows, FlintBackend.Excel, "data_empty").Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Message),
                Has.None.Contains(reason), "not blamed on the rows");
        }
    }

    // source: resolve-semantics.ts:557-564 (ISO hack from quantitative only), :682-689 (aggregate), :547-555 (explicit type over the semantic type)
    [Test]
    public void Excel_TypesTheRowsDecide_AreSkippedWithoutRows()
    {
        bool Valid(ChartAssemblyInput input) => ChartTemplate.Create<JsonObject>(input).Validate(FlintBackend.Excel).IsValid;
        IEnumerable<ValidationIssue> TemplateIssues(ChartAssemblyInput input) =>
            ChartTemplate.Create<JsonObject>(input).Validate(FlintBackend.Excel).Issues.Where(i => i.Code == "excel_unsupported");

        var aggregated = Bar(i =>
        {
            i.ChartSpec.ChartType = new ChartType("Area Chart");
            i.ChartSpec.Encodings = new() { ["x"] = "city", ["y"] = Sum("revenue", EncodingType.Nominal) };
        });
        AssertNone(aggregated, FlintBackend.Excel, "excel_unsupported");
        Assert.That(Valid(aggregated), Is.True);
        AssertNone(StreamedBar(i => { i.ChartSpec.ChartType = new ChartType("Area Chart"); i.ChartSpec.Encodings = new() { ["x"] = "city", ["y"] = Sum("revenue", EncodingType.Nominal) }; }), FlintBackend.Excel, "excel_unsupported");
        aggregated.Data = ChartData.FromJson([]);
        AssertNone(aggregated, FlintBackend.Excel, "excel_unsupported");

        var values = Bar(i =>
        {
            i.ChartSpec.ChartType = new ChartType("Area Chart");
            i.ChartSpec.Encodings = new() { ["x"] = "revenue", ["y"] = "city" };
        });
        Assert.That(Issues(values, FlintBackend.Excel, "excel_unsupported"),
            Has.Exactly(1).Matches<ValidationIssue>(i => i.Severity == IssueSeverity.Error && i.Channel == Channel.Y));
        Assert.That(TemplateIssues(values), Is.Empty);

        ChartAssemblyInput IsoLine() => Line(i =>
        {
            i.Data = ChartData.FromJson([new JsonObject { ["city"] = "a", ["at"] = "2024-01-15T10:00:00Z" }, new JsonObject { ["city"] = "b", ["at"] = "2024-01-16T10:00:00Z" }]);
            i.ChartSpec.Encodings["y"] = Typed("at", EncodingType.Quantitative);
        });
        Assert.That(Issues(IsoLine(), FlintBackend.Excel, "excel_unsupported"),
            Has.Exactly(1).Matches<ValidationIssue>(i => i.Severity == IssueSeverity.Error && i.Channel == Channel.Y));
        Assert.That(TemplateIssues(IsoLine()), Is.Empty);

        var semantic = Bar(i =>
        {
            i.ChartSpec.ChartType = new ChartType("Area Chart");
            i.ChartSpec.Encodings = new() { ["x"] = "city", ["y"] = N("revenue") };
            i.SemanticTypes = new() { ["revenue"] = new SemanticAnnotation { Type = SemanticType.Amount } };
        });
        Assert.That(ChartTemplate.Create<JsonObject>(semantic).Validate(FlintBackend.Excel).Issues,
            Has.Exactly(1).Matches<ValidationIssue>(i => i.Code == "excel_unsupported" && i.Severity == IssueSeverity.Error && i.Channel == Channel.Y));
    }

    // source: types.ts:24-27 requireQuantitativeAxes, split per axis as ImageCharts.cs
    [Test]
    public void Excel_XyChart_ReportsEachAxis()
    {
        var input = Bar(i =>
        {
            i.ChartSpec.ChartType = ChartType.ScatterPlot;
            i.ChartSpec.Encodings = new() { ["x"] = N("revenue"), ["y"] = O("cost") };
        });
        var issues = Issues(input, FlintBackend.Excel, "excel_unsupported").ToList();
        Assert.That(issues.Select(i => i.Channel?.Name), Is.EquivalentTo(new[] { "x", "y" }));
        Assert.That(issues.Select(i => i.Path), Is.EquivalentTo(new[] { "chart_spec.encodings.x", "chart_spec.encodings.y" }));
    }

    // source: ValidationRun.DataChecks (a spec reason is reported as the native code; the Info no longer says the chart assembles)
    [Test]
    public void NativeBackends_NoRowsInfo_SaysASpecRuleThrows()
    {
        var excel = Bar(i =>
        {
            i.Data = ChartData.FromJson([]);
            i.ChartSpec.ChartType = new ChartType("Area Chart");
            i.ChartSpec.Encodings = new() { ["x"] = "city", ["y"] = N("revenue") };
        });
        var info = Issues(excel, FlintBackend.Excel, "data_empty").Single();
        Assert.That(info.Severity, Is.EqualTo(IssueSeverity.Info));
        Assert.That(info.Message, Does.Contain("throws whatever the rows (excel_unsupported)"));

        var imageCharts = Scatter(i =>
        {
            i.Data = ChartData.FromJson([]);
            i.ChartSpec.Encodings["x"] = O("revenue");
            i.ChartSpec.Encodings["y"] = Q("cost");
        });
        Assert.That(Issues(imageCharts, FlintBackend.ImageCharts, "data_empty").Single().Message, Does.Contain("(image_charts_unsupported)"));

        // source: probe "data values empty, typed encodings"
        var clean = Bar(i =>
        {
            i.Data = ChartData.FromJson([]);
            i.ChartSpec.Encodings = new() { ["x"] = N("city"), ["y"] = Q("revenue") };
        });
        Assert.That(Issues(clean, FlintBackend.Excel, "data_empty").Single().Message, Does.Contain("assembles an empty chart"));
    }

    private static void GroupedBar(ChartAssemblyInput i, params (string Channel, EncodingValue Value)[] encodings)
    {
        i.ChartSpec.ChartType = ChartType.GroupedBarChart;
        i.ChartSpec.Encodings = new() { ["x"] = "city", ["y"] = "revenue" };
        foreach (var (channel, value) in encodings)
        {
            i.ChartSpec.Encodings[channel] = value;
        }
    }

    // source: src/core/static-series.ts:63-73 (normalizeChartEncodingAliases: color becomes group when group is unbound) and
    // src/excel/templates/bar.ts:20; probe "grouped bar with a quantitative color and no group"
    [Test]
    public void Excel_GroupedBarContinuousColorWithoutGroup_IsReadAsTheGroup()
    {
        void Spec(ChartAssemblyInput i) => GroupedBar(i, ("color", Q("cost")));
        bool Reported(IEnumerable<ValidationIssue> issues) =>
            issues.Any(i => i.Code == "excel_unsupported" && i.Severity == IssueSeverity.Error && i.Channel == Channel.Color
                && i.Path == "chart_spec.encodings.color" && i.Message.Contains("continuous grouping", StringComparison.Ordinal));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Reported(Issues(Bar(Spec), FlintBackend.Excel)), Is.True, "with rows");
            Assert.That(Reported(ChartTemplate.Create<JsonObject>(Bar(Spec)).Validate(FlintBackend.Excel).Issues), Is.True, "template");
            Assert.That(Reported(Issues(StreamedBar(Spec), FlintBackend.Excel)), Is.True, "streamed");
            Assert.That(Reported(Issues(Bar(Spec))), Is.True, "no backend");
            Assert.That(Issues(Bar(Spec), FlintBackend.Excel, "unsupported_channel"), Is.Empty);
        }
    }

    // source: probe "grouped bar with a nominal color and no group" (assembles on Excel)
    [Test]
    public void Excel_GroupedBarNominalColorWithoutGroup_IsNotAnError()
    {
        var input = Bar(i => GroupedBar(i, ("color", N("city"))));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Issues(input, FlintBackend.Excel, "excel_unsupported"), Is.Empty);
            Assert.That(Issues(input, FlintBackend.Excel, "unsupported_channel"), Is.Empty);
            Assert.That(Issues(Bar(i => GroupedBar(i, ("color", "city"))), FlintBackend.Excel, "unsupported_channel"), Is.Empty, "shorthand");
        }
    }

    // source: normalizeChartEncodingAliases (a bound group wins and color stays a channel Excel never reads); verified with
    // assembleExcel on flint-chart 1.0.1: a nominal group with a quantitative color assembles, a continuous group throws
    // whatever the color, and an encoding with an empty field still counts as bound
    [Test]
    public void Excel_GroupedBarWithGroup_IgnoresColor()
    {
        var nominalGroup = Bar(i => GroupedBar(i, ("group", N("city")), ("color", Q("cost"))));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Issues(nominalGroup, FlintBackend.Excel, "excel_unsupported"), Is.Empty);
            Assert.That(Issues(nominalGroup, FlintBackend.Excel, "unsupported_channel").Select(i => i.Path), Is.EqualTo(new[] { "chart_spec.encodings.color" }));
            Assert.That(Issues(Bar(i => GroupedBar(i, ("group", ""), ("color", Q("cost")))), FlintBackend.Excel, "excel_unsupported"), Is.Empty, "empty group field");
            Assert.That(Issues(Bar(i => GroupedBar(i, ("group", ""), ("color", Q("cost")))), FlintBackend.Excel, "unsupported_channel").ToList(), Has.Count.EqualTo(1), "empty group field");
        }

        var continuousGroup = Bar(i => GroupedBar(i, ("group", Q("cost")), ("color", N("city"))));
        Assert.That(Issues(continuousGroup, FlintBackend.Excel, "excel_unsupported").Select(i => i.Channel?.Name), Is.EqualTo(new[] { "group" }));
    }

    // source: normalizeChartEncodingAliases (only a Grouped Bar Chart aliases): a Line Chart's group is not aliased, and the Excel Line template does not declare it, so it is reported as before
    [Test]
    public void Excel_GroupWithoutColorOnALineChart_IsStillUnsupported()
    {
        var line = Line(i => i.ChartSpec.Encodings["group"] = N("city"));
        Assert.That(Issues(line, FlintBackend.Excel, "unsupported_channel").Select(i => i.Path), Is.EqualTo(new[] { "chart_spec.encodings.group" }));
    }
}
