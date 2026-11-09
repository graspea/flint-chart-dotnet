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

    // source: src/excel/templates/candlestick.ts:31-33 (the stock chart throws on a color, group or detail field) and
    // probes.json channelReads (binding any of them changes the throw, so none is ignored)
    [Test]
    public void Excel_CandlestickColorGroupOrDetail_IsReportedOnTheChannel()
    {
        var input = Bar(i =>
        {
            i.ChartSpec.ChartType = ChartType.CandlestickChart;
            i.ChartSpec.Encodings = new() { ["x"] = O("city"), ["open"] = "revenue", ["high"] = "revenue", ["low"] = "revenue", ["close"] = "revenue", ["color"] = "city", ["detail"] = "city" };
        });
        var stock = Issues(input, FlintBackend.Excel, "excel_unsupported").Where(i => i.Message.Contains("color, group, or detail", StringComparison.Ordinal));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stock.Select(i => i.Path), Is.EqualTo(new[] { "chart_spec.encodings.color", "chart_spec.encodings.detail" }));
            Assert.That(Issues(input, FlintBackend.Excel, "unsupported_channel"), Is.Empty);
            Assert.That(ChartTemplate.Create<JsonObject>(input).Validate(FlintBackend.Excel).Issues.Where(i => i.Code == "excel_unsupported" && i.Message.Contains("color, group, or detail", StringComparison.Ordinal)).Select(i => i.Path),
                Is.EqualTo(new[] { "chart_spec.encodings.color", "chart_spec.encodings.detail" }), "template");
        }
    }

    private static ChartAssemblyInput ExcelChart(ChartType chartType, params (string Channel, EncodingValue Value)[] encodings) => Bar(i =>
    {
        i.ChartSpec.ChartType = chartType;
        i.ChartSpec.Encodings = new();
        foreach (var (channel, value) in encodings)
        {
            i.ChartSpec.Encodings[channel] = value;
        }
    });

    private static string[] UnsupportedOnExcel(ChartAssemblyInput input) =>
        Issues(input, FlintBackend.Excel, "unsupported_channel").Select(i => i.Path).ToArray();

    // source: src/excel/assemble.ts:270-275 (seriesCand = encodings.group ? 'group' : a categorical color: the series of
    // every shared-path template but Pie and Donut, which read none, :277-281); the Excel Line template does not declare
    // group (src/excel/templates/line.ts:8); probes.json channelReads
    [Test]
    public void Excel_GroupOnALineChart_IsReadAsTheSeries()
    {
        var nominal = new ChartEncoding { Type = EncodingType.Nominal };
        using (Assert.EnterMultipleScope())
        {
            Assert.That(UnsupportedOnExcel(Line(i => i.ChartSpec.Encodings["group"] = N("city"))), Is.Empty, "a group field");
            Assert.That(UnsupportedOnExcel(Line(i => { i.ChartSpec.Encodings["color"] = "city"; i.ChartSpec.Encodings["group"] = nominal; })), Is.Empty, "no field, displacing a color");
            Assert.That(UnsupportedOnExcel(Line(i => i.ChartSpec.Encodings["group"] = nominal)), Is.EqualTo(new[] { "chart_spec.encodings.group" }), "no field and no color");
            Assert.That(UnsupportedOnExcel(Bar(i => { i.ChartSpec.Encodings["color"] = "revenue"; i.ChartSpec.Encodings["group"] = nominal; })), Is.EqualTo(new[] { "chart_spec.encodings.group" }), "no field beside a quantitative color");
            Assert.That(UnsupportedOnExcel(Bar(i => { i.ChartSpec.Encodings["color"] = nominal; i.ChartSpec.Encodings["group"] = nominal; })), Is.EqualTo(new[] { "chart_spec.encodings.group" }), "no field beside a field-less color");
            Assert.That(UnsupportedOnExcel(ExcelChart(ChartType.PieChart, ("color", "city"), ("size", "revenue"), ("group", "city"))), Is.EqualTo(new[] { "chart_spec.encodings.group" }), "a Pie Chart has no series");
        }
    }

    // source: src/excel/assemble.ts:293 (dashField = fieldOf('strokeDash')), :403-448 (the series descriptors and formats
    // off the XY path) and :587-591 (the legend shows for a series or a dash field)
    [Test]
    public void Excel_StrokeDashWithAField_IsRead()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(UnsupportedOnExcel(Bar(i => i.ChartSpec.Encodings["strokeDash"] = "city")), Is.Empty, "bar");
            Assert.That(UnsupportedOnExcel(ExcelChart(ChartType.PieChart, ("color", "city"), ("size", "revenue"), ("strokeDash", "city"))), Is.Empty, "pie");
            Assert.That(UnsupportedOnExcel(Bar(i => i.ChartSpec.Encodings["strokeDash"] = "")), Is.EqualTo(new[] { "chart_spec.encodings.strokeDash" }), "an empty field");
            Assert.That(UnsupportedOnExcel(Scatter(i => i.ChartSpec.Encodings["strokeDash"] = "city")), Is.Empty, "scatter without a series: it shows the legend");
            Assert.That(UnsupportedOnExcel(Scatter(i => { i.ChartSpec.Encodings["color"] = "city"; i.ChartSpec.Encodings["strokeDash"] = "city"; })),
                Is.EqualTo(new[] { "chart_spec.encodings.strokeDash" }), "scatter with a series");
            Assert.That(Issues(Bar(i => i.ChartSpec.Encodings["strokeDash"] = "city"), null, "unsupported_channel"), Is.Empty, "no backend");
        }
    }

    // source: src/excel/assemble.ts:294-295 and :320-377 (size and order are read on the XY path: a Scatter or Connected
    // Scatter Plot, mapping.xy, :282-283, and a Line Chart over quantitative x and y, numericXLine, :309-314)
    [Test]
    public void Excel_SizeAndOrder_AreReadOnTheXyPath()
    {
        var numericLine = ExcelChart(ChartType.LineChart, ("x", "revenue"), ("y", "cost"), ("size", "cost"), ("order", "revenue"));
        var nominalX = ExcelChart(ChartType.LineChart, ("x", N("revenue")), ("y", "cost"), ("size", "cost"));
        IEnumerable<string> TemplatePaths(ChartAssemblyInput input) =>
            ChartTemplate.Create<JsonObject>(input).Validate(FlintBackend.Excel).Issues.Where(i => i.Code == "unsupported_channel").Select(i => i.Path);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(UnsupportedOnExcel(ExcelChart(ChartType.ConnectedScatterPlot, ("x", "revenue"), ("y", "cost"), ("order", "revenue"), ("size", "cost"))), Is.Empty, "connected scatter size");
            Assert.That(UnsupportedOnExcel(Scatter(i => i.ChartSpec.Encodings["order"] = "revenue")), Is.Empty, "scatter order");
            Assert.That(UnsupportedOnExcel(numericLine), Is.Empty, "line over numeric x and y");
            Assert.That(UnsupportedOnExcel(Line(i => { i.ChartSpec.Encodings["size"] = "cost"; i.ChartSpec.Encodings["order"] = "cost"; })),
                Is.EquivalentTo(new[] { "chart_spec.encodings.size", "chart_spec.encodings.order" }), "line over a category");
            Assert.That(TemplatePaths(numericLine), Is.Empty, "template: the rows decide x and y");
            Assert.That(TemplatePaths(nominalX), Is.EqualTo(new[] { "chart_spec.encodings.size" }), "template: a nominal x decides");
        }
    }

    // source: src/excel/assemble.ts:277-281 (a Pie or Donut Chart takes color, else x, as the slice and size, else theta,
    // else y, as the value; a bound encoding of any shape shadows the next) and src/excel/templates/histogram.ts:19, :25
    // (y is the value without an x field)
    [Test]
    public void Excel_SliceValueAndHistogramFallbacks_AreRead()
    {
        var histogram = new ChartType("Histogram");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(UnsupportedOnExcel(ExcelChart(ChartType.PieChart, ("x", "city"), ("y", "revenue"))), Is.Empty, "pie x and y");
            Assert.That(UnsupportedOnExcel(ExcelChart(ChartType.DonutChart, ("color", "city"), ("size", "revenue"), ("x", "city"), ("y", "revenue"))),
                Is.EquivalentTo(new[] { "chart_spec.encodings.x", "chart_spec.encodings.y" }), "shadowed by color and size");
            Assert.That(UnsupportedOnExcel(ExcelChart(histogram, ("y", "revenue"))), Is.Empty, "histogram y without x");
            Assert.That(Issues(ExcelChart(histogram, ("y", "revenue")), null, "unsupported_channel"), Is.Empty, "histogram y without x, no backend");
            Assert.That(UnsupportedOnExcel(ExcelChart(histogram, ("x", "revenue"), ("y", "cost"))), Is.EqualTo(new[] { "chart_spec.encodings.y" }), "histogram with x");
        }
    }

    // source: src/excel/templates/histogram.ts:19-20 (validate() takes x as the value channel only when its field is
    // non-empty, else y) and :25-27 (instantiate() reads fieldOf('x') ?? fieldOf('y'), so an empty x field is the value
    // field, read as the rows' "" key, and it throws "Excel histogram requires finite numeric values."); probe case
    // "histogram with an empty x field and y"
    [Test]
    public void Excel_HistogramWithAnEmptyXField_ReadsTheEmptyKey()
    {
        var histogram = new ChartType("Histogram");
        var empty = ExcelChart(histogram, ("x", ""), ("y", "revenue"));
        var emptyKey = ExcelChart(histogram, ("x", ""), ("y", "revenue"));
        emptyKey.Data = ChartData.FromJson([new JsonObject { ["city"] = "a", ["revenue"] = 10, [""] = 3 }, new JsonObject { ["city"] = "b", ["revenue"] = 20, [""] = 4 }]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Issues(empty, FlintBackend.Excel, "excel_unsupported").Select(i => (i.Path, i.Severity)),
                Is.EqualTo(new[] { ("chart_spec.encodings.x", IssueSeverity.Error) }));
            Assert.That(Issues(empty, FlintBackend.Excel, "excel_unsupported").Single().Message, Does.Contain("finite numeric values"));
            Assert.That(ChartValidator.Validate(empty).IsValid, Is.False, "no backend");
            AssertNone(emptyKey, FlintBackend.Excel, "excel_unsupported");
            AssertNone(ExcelChart(histogram, ("x", new ChartEncoding()), ("y", "revenue")), FlintBackend.Excel, "excel_unsupported");
            AssertNone(ExcelChart(histogram, ("y", "revenue")), FlintBackend.Excel, "excel_unsupported");
            Assert.That(ChartTemplate.Create<JsonObject>(empty).Validate(FlintBackend.Excel).IsValid, Is.True, "template: the rows decide");
        }
    }

    /// <summary>An Excel document of <paramref name="chartType"/> over <paramref name="rows"/>, with semantic types.</summary>
    private static ChartAssemblyInput ExcelTable(ChartType chartType, JsonObject[] rows, Dictionary<string, SemanticType>? semanticTypes, params (string Channel, EncodingValue Value)[] encodings)
    {
        var input = ExcelChart(chartType, encodings);
        input.Data = ChartData.FromJson(new JsonArray([.. rows.Select(r => r.DeepClone())]));
        if (semanticTypes is not null)
        {
            input.SemanticTypes = semanticTypes.ToDictionary(p => p.Key, p => (SemanticAnnotation)p.Value);
        }

        return input;
    }

    private static string[] ExcelErrors(ChartAssemblyInput input) =>
        Issues(input, FlintBackend.Excel, "excel_unsupported").Select(i => $"{i.Path}: {i.Message}").ToArray();

    // source: src/excel/assemble.ts:201, :216, :225 (the templates read convertedData) and src/core/resolve-semantics.ts:428-483
    // (convertTemporalData writes every value of a temporal key of the first row as a string: a YearMonth 202501 is
    // "2025-01", a timestamp an ISO date-time, null "null"); src/excel/templates/histogram.ts:25-27 (Number of each value);
    // probe cases "histogram with an empty x field and a numeric empty key", "... a YearMonth empty key" and "histogram of
    // a quantitative YearMonth field"
    [Test]
    public void Excel_HistogramValues_AreReadAfterTheTemporalConversion()
    {
        var histogram = new ChartType("Histogram");
        JsonObject[] Keyed(string key, params JsonNode?[] values) => [.. values.Select((v, i) => new JsonObject { [key] = v?.DeepClone(), ["revenue"] = 10 + i })];
        var emptyKey = Keyed("", 202501, 202502);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ExcelErrors(ExcelTable(histogram, emptyKey, new() { [""] = SemanticType.YearMonth }, ("x", ""), ("y", "revenue"))),
                Is.EqualTo(new[] { "chart_spec.encodings.x: Excel requires finite numeric values for a histogram, and reads the empty x field as the value field (the rows' \"\" key); Excel throws." }), "YearMonth empty key");
            Assert.That(ExcelErrors(ExcelTable(histogram, emptyKey, null, ("x", ""), ("y", "revenue"))), Is.Empty, "unannotated numbers stay numbers");
            Assert.That(ExcelErrors(ExcelTable(histogram, Keyed("month", 202501, 202502), new() { ["month"] = SemanticType.YearMonth }, ("x", Q("month")))),
                Has.Length.EqualTo(1).And.Some.Contains("finite numeric values"), "quantitative YearMonth");
            Assert.That(ExcelErrors(ExcelTable(histogram, Keyed("at", 1700000000, 1700003600), new() { ["at"] = SemanticType.Timestamp }, ("x", Q("at")))),
                Has.Length.EqualTo(1).And.Some.Contains("finite numeric values"), "timestamps are ISO date-times");
            Assert.That(ExcelErrors(ExcelTable(histogram, Keyed("year", null, null), new() { ["year"] = SemanticType.Year }, ("x", Q("year")))),
                Has.Length.EqualTo(1).And.Some.Contains("finite numeric values"), "null is \"null\"");
            Assert.That(ExcelErrors(ExcelTable(histogram, Keyed("year", 2020, 2021), null, ("x", Q("year")))), Is.Empty, "an implicit Year is its floor, a number");
            Assert.That(ExcelErrors(ExcelTable(histogram, [new JsonObject { ["revenue"] = 10 }, new JsonObject { [""] = 202502, ["revenue"] = 20 }], new() { [""] = SemanticType.YearMonth }, ("x", ""), ("y", "revenue"))),
                Is.Empty, "only the keys of the first row are converted");
            Assert.That(ChartTemplate.Create<JsonObject>(ExcelTable(histogram, emptyKey, new() { [""] = SemanticType.YearMonth }, ("x", ""), ("y", "revenue"))).Validate(FlintBackend.Excel).IsValid,
                Is.True, "template: the rows decide");
        }
    }

    // source: src/core/resolve-semantics.ts:428-483 and src/excel/templates/radar.ts:45-47 (String of each converted metric):
    // three spellings of one YearMonth, or of one untyped date, are one metric; probe case "radar with one YearMonth metric
    // in three spellings"
    [Test]
    public void Excel_RadarMetrics_AreCountedAfterTheTemporalConversion()
    {
        JsonObject[] Metrics(params JsonNode[] metrics) => [.. metrics.Select((m, i) => new JsonObject { ["metric"] = m.DeepClone(), ["revenue"] = 10 + i })];
        var spellings = Metrics(202501, "2025-01", "Jan 2025");
        const string Three = "chart_spec.encodings.x: Excel requires at least three metric axes for a radar chart; Excel throws.";
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ExcelErrors(ExcelTable(ChartType.RadarChart, spellings, new() { ["metric"] = SemanticType.YearMonth }, ("x", N("metric")), ("y", "revenue"))), Is.EqualTo(new[] { Three }), "YearMonth");
            Assert.That(ExcelErrors(ExcelTable(ChartType.RadarChart, spellings, null, ("x", N("metric")), ("y", "revenue"))), Is.Empty, "unannotated: three metrics");
            Assert.That(ExcelErrors(ExcelTable(ChartType.RadarChart, Metrics("Jan 1, 2025", "January 1, 2025", "2025/01/01"), null, ("x", N("metric")), ("y", "revenue"))),
                Is.EqualTo(new[] { Three }), "untyped full dates read as one date");
            Assert.That(ExcelErrors(ExcelTable(ChartType.RadarChart, Metrics(5, "2025-01-01", "Jan 1, 2025"), null, ("x", N("metric")), ("y", "revenue"))),
                Is.Empty, "only Date.parse could tell whether the key is temporal: the rule skips");
            Assert.That(ExcelErrors(ExcelTable(ChartType.RadarChart, Metrics("2025-01-01T10:00+24:00", "2025-01-02T10:00+24:00"), null, ("x", N("metric")), ("y", "revenue"))),
                Is.EqualTo(new[] { Three }), "undecided, but the conversion leaves every value as it is");
        }
    }

    // source: src/core/resolve-semantics.ts:428-483 and src/excel/templates/pyramid.ts:38-41 (the non-null distinct converted
    // values): a null date is the string "null", a missing one "undefined", and 2024 and "2024" of a Year are one group, a two-digit Year string is its four-digit year (expandToFullYear, :412-419) and a fractional Year its floor (:458-459);
    // probe cases "pyramid with a date color and a null" and "pyramid with a Year color as numbers and strings"
    [Test]
    public void Excel_PyramidGroups_AreCountedAfterTheTemporalConversion()
    {
        JsonObject[] Sides(params JsonNode?[] sides) => [.. sides.Select((s, i) => new JsonObject { ["age"] = i % 2 == 0 ? "a" : "b", ["wave"] = s?.DeepClone(), ["revenue"] = 1 + i })];
        ChartAssemblyInput Pyramid(JsonObject[] rows, Dictionary<string, SemanticType>? semanticTypes = null) =>
            ExcelTable(ChartType.PyramidChart, rows, semanticTypes, ("x", "revenue"), ("y", "age"), ("color", "wave"));
        var missing = Sides("2024-01-01", "2025-01-01");
        missing = [.. missing, new JsonObject { ["age"] = "a", ["revenue"] = 3 }];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ExcelErrors(Pyramid(Sides("2024-01-01", "2025-01-01", null))), Has.Length.EqualTo(1).And.Some.Contains("(found 3)"), "a null date");
            Assert.That(ExcelErrors(Pyramid(missing)), Has.Length.EqualTo(1).And.Some.Contains("(found 3)"), "a missing date");
            Assert.That(ExcelErrors(Pyramid(Sides("M", "F", null))), Is.Empty, "a null category is no group");
            Assert.That(ExcelErrors(Pyramid(Sides(2024, "2024", 2025), new() { ["wave"] = SemanticType.Year })), Is.Empty, "2024 and \"2024\" are one year");
            Assert.That(ExcelErrors(Pyramid(Sides(2024, "24", 2025), new() { ["wave"] = SemanticType.Year })), Is.Empty, "a two-digit Year string is its four-digit year");
            Assert.That(ExcelErrors(Pyramid(Sides(2024.5, "2024", 2025), new() { ["wave"] = SemanticType.Year })), Is.Empty, "a fractional Year is its floor");
            Assert.That(ExcelErrors(Pyramid(Sides("2024-01-01", "2024/01/01", "2025-01-01"))), Is.Empty, "an untyped 2024/01/01 is 2024-01-01");
            Assert.That(ExcelErrors(Pyramid(Sides(5, "2024-01-01", "2024/01/01"))), Is.Empty, "only Date.parse could tell whether 5 makes the key temporal: the rule skips");
        }
    }

    // source: src/core/resolve-semantics.ts:428-483 and src/excel/templates/waterfall.ts:22, :32 (resolveTotalsMode over the
    // finite converted values): YearMonth values are strings, so no last value reconciles; probe case "waterfall of a
    // quantitative YearMonth y"
    [Test]
    public void Excel_WaterfallTotals_ReadTheValuesAfterTheTemporalConversion()
    {
        JsonObject[] Steps(params JsonNode?[] values) => [.. values.Select((v, i) => new JsonObject { ["step"] = $"s{i}", ["month"] = v?.DeepClone() })];
        ChartAssemblyInput Waterfall(JsonObject[] rows, SemanticType? semanticType) =>
            ExcelTable(ChartType.WaterfallChart, rows, semanticType is { } t ? new() { ["month"] = t } : null, ("x", "step"), ("y", Q("month")));
        const string Final = "chart_spec: Excel cannot mark a final total because Office.js does not expose Waterfall total-point semantics; Excel throws.";
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ExcelErrors(Waterfall(Steps(202501, 202502, 405003), SemanticType.YearMonth)), Is.Empty, "YearMonth strings are not finite");
            Assert.That(ExcelErrors(Waterfall(Steps(202501, 202502, 405003), null)), Is.EqualTo(new[] { Final }), "unannotated: the last value reconciles");
            Assert.That(ExcelErrors(Waterfall(Steps(5, 5, null), SemanticType.Year)), Is.EqualTo(new[] { Final }), "a null Year is \"null\", not 0");
        }
    }

    // source: src/core/resolve-semantics.ts:428-483 and src/excel/templates/candlestick.ts:40-53 (new Date of each converted
    // x): a null date is "null", an invalid date; a Date 20250102 is "2025-01-02"; a month-name date is an ISO date; a seconds or a milliseconds timestamp is its ISO instant (isLikelyTimestamp and timestampToMs, :223-231, read at :460-461); probe
    // cases "candlestick with a null first date" and "candlestick with month-name dates out of order"
    [Test]
    public void Excel_CandlestickRows_AreReadAfterTheTemporalConversion()
    {
        JsonObject[] Days(params JsonNode?[] days) => [.. days.Select((d, i) => new JsonObject { ["day"] = d?.DeepClone(), ["open"] = 10 + i, ["high"] = 12 + i, ["low"] = 9 + i, ["close"] = 11 + i })];
        ChartAssemblyInput Stock(JsonObject[] rows, SemanticType? semanticType = null) =>
            ExcelTable(ChartType.CandlestickChart, rows, semanticType is { } t ? new() { ["day"] = t } : null, ("x", "day"), ("open", "open"), ("high", "high"), ("low", "low"), ("close", "close"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ExcelErrors(Stock(Days(null, "2025-01-02", "2025-01-03"))),
                Is.EqualTo(new[] { "chart_spec.encodings.x: Excel requires a valid date at row 1 for a native Excel stock chart; Excel throws." }), "a null first date");
            Assert.That(ExcelErrors(Stock(Days("2025-01-01", null, "2025-01-03"))),
                Is.EqualTo(new[] { "chart_spec.encodings.x: Excel requires a valid date at row 2 for a native Excel stock chart; Excel throws." }), "a null date is invalid, not 1970");
            Assert.That(ExcelErrors(Stock(Days("2025-01-01", 20250102), SemanticType.Date)), Is.Empty, "20250102 is 2 January 2025");
            Assert.That(ExcelErrors(Stock(Days("Jan 2, 2025", "Jan 1, 2025"))), Has.Length.EqualTo(1).And.Some.Contains("strictly increasing"), "month-name dates read as ISO dates");
            Assert.That(ExcelErrors(Stock(Days("2025-01-01", "2025-01-02", "2025-01-03"))), Is.Empty, "ISO dates");
            Assert.That(ExcelErrors(Stock(Days(1700000000, 1700000000000), SemanticType.Timestamp)), Has.Length.EqualTo(1).And.Some.Contains("strictly increasing"), "a seconds and a milliseconds timestamp of one instant");
        }
    }
}
