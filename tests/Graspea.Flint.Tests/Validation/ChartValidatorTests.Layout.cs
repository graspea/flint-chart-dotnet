using Graspea.Flint.Validation;

namespace Graspea.Flint.Tests.Validation;

public sealed partial class ChartValidatorTests
{
    private static ChartAssemblyInput Layout(string chartType, params (string Channel, EncodingValue Value)[] encodings) => Bar(i =>
    {
        i.ChartSpec.ChartType = new ChartType(chartType);
        i.ChartSpec.Encodings = new();
        foreach (var (channel, value) in encodings)
        {
            i.ChartSpec.Encodings[channel] = value;
        }
    });

    private static string[] Unsupported(ChartAssemblyInput input, FlintBackend? backend) =>
        Issues(input, backend, "unsupported_channel").Select(i => i.Path).ToArray();

    // source: src/echarts/assemble.ts:351-361 and src/plotly/assemble.ts:332-346 (a column or row field facets the chart when
    // the template declares x or y; Plotly not where the template lays out its own grid, src/plotly/templates/sparkline.ts:78);
    // src/vegalite/assemble.ts:1185-1189 with src/vegalite/templates/scatter.ts:322-327 (the Vega-Lite Ranged Dot Plot writes
    // them on the top-level encoding, which restructureFacets makes the facet). Rendered with vega-lite 6.5.0 and echarts
    // 6.1.0: one panel per value, headed by the value, each in its own grid. src/echarts/facet.ts:353-359 and :424-428 (the
    // faceted option keeps grid, axes, series, tooltip, color and legend, and gives each series an axis index): a Calendar
    // Heatmap loses its calendar and visualMap, and echarts 6.1.0 throws "Heatmap must use with visualMap"; the funnel, graph
    // and sankey series keep their canvas position (src/echarts/templates/funnel.ts:140-143, graph.ts:145-148,
    // sankey.ts:152-155), so every panel's chart is drawn in the same place. Those four stay reported.
    [Test]
    public void Layout_ColumnAndRowOnATemplateThatLeavesThemOut_AreReadWhereTheFacetPanelsDraw()
    {
        string[] none = [];
        using (Assert.EnterMultipleScope())
        {
            foreach (var backend in new[] { FlintBackend.VegaLite, FlintBackend.ECharts, FlintBackend.Plotly })
            {
                Assert.That(Unsupported(Layout("Ranged Dot Plot", ("x", "revenue"), ("y", "city"), ("column", "cost")), backend), Is.EqualTo(none), $"ranged dot column, {backend}");
                Assert.That(Unsupported(Layout("Ranged Dot Plot", ("x", "revenue"), ("y", "city"), ("row", "cost")), backend), Is.EqualTo(none), $"ranged dot row, {backend}");
            }

            Assert.That(Unsupported(Layout("Ranged Dot Plot", ("x", "revenue"), ("y", "city"), ("column", "cost")), null), Is.EqualTo(none), "ranged dot, no backend");
            Assert.That(Unsupported(Layout("Funnel Chart", ("y", "city"), ("size", "revenue"), ("row", "cost")), FlintBackend.Plotly), Is.EqualTo(none), "funnel, plotly");
            Assert.That(Unsupported(Layout("Rose Chart", ("x", "city"), ("y", "revenue"), ("column", "cost")), FlintBackend.Plotly), Is.EqualTo(none), "rose, plotly");

            Assert.That(Unsupported(Layout("Ranged Dot Plot", ("x", "revenue"), ("y", "city"), ("column", "")), FlintBackend.ECharts), Is.EqualTo(new[] { "chart_spec.encodings.column" }), "an empty field");
            Assert.That(Unsupported(Layout("Pyramid Chart", ("x", "revenue"), ("y", "city"), ("column", new ChartEncoding { Aggregate = Aggregate.Count })), FlintBackend.ECharts), Is.EqualTo(new[] { "chart_spec.encodings.column" }), "a count column: one panel headed \"1\"");
            Assert.That(Unsupported(Layout("Ranged Dot Plot", ("x", "revenue"), ("y", "city"), ("column", new ChartEncoding { Field = "cost", Aggregate = Aggregate.Count })), FlintBackend.ECharts), Is.EqualTo(new[] { "chart_spec.encodings.column" }), "a counted field is the constant _count");
            Assert.That(Unsupported(Layout("Ranged Dot Plot", ("x", "revenue"), ("y", "city"), ("column", new ChartEncoding { Field = "cost", Aggregate = Aggregate.Count })), null), Is.EqualTo(new[] { "chart_spec.encodings.column" }), "a counted field, no backend");
            Assert.That(Unsupported(Layout("Calendar Heatmap", ("x", "city"), ("color", "revenue"), ("column", "cost")), FlintBackend.ECharts), Is.EqualTo(new[] { "chart_spec.encodings.column" }), "echarts calendar heatmap: the faceted option drops the calendar and visualMap");
            Assert.That(Unsupported(Layout("Funnel Chart", ("y", "city"), ("size", "revenue"), ("column", "cost")), FlintBackend.ECharts), Is.EqualTo(new[] { "chart_spec.encodings.column" }), "echarts funnel: every panel's funnel in the same place");
            Assert.That(Unsupported(Layout("Network Graph", ("x", "city"), ("y", "revenue"), ("column", "cost")), FlintBackend.ECharts), Is.EqualTo(new[] { "chart_spec.encodings.column" }), "echarts network graph: every panel's graph in the same place");
            Assert.That(Unsupported(Layout("Sankey Diagram", ("x", "city"), ("y", "revenue"), ("row", "cost")), FlintBackend.ECharts), Is.EqualTo(new[] { "chart_spec.encodings.row" }), "echarts sankey: every panel's sankey in the same place");
            Assert.That(Unsupported(Layout("Gauge Chart", ("size", "revenue"), ("row", "city")), FlintBackend.ECharts), Is.EqualTo(new[] { "chart_spec.encodings.row" }), "gauge: no x or y, no facet");
            Assert.That(Unsupported(Layout("Sparkline", ("x", "city"), ("y", "revenue"), ("column", "cost")), FlintBackend.Plotly), Is.EqualTo(new[] { "chart_spec.encodings.column" }), "plotly sparkline lays out its own grid");
            Assert.That(Unsupported(Layout("Map", ("longitude", "revenue"), ("latitude", "cost"), ("column", "city")), FlintBackend.VegaLite), Is.EqualTo(new[] { "chart_spec.encodings.column" }), "vega-lite map: inside a layer, dropped");
            Assert.That(Unsupported(Layout("Pyramid Chart", ("x", "revenue"), ("y", "city"), ("column", "cost")), FlintBackend.VegaLite), Is.EqualTo(new[] { "chart_spec.encodings.column" }), "vega-lite pyramid: not written");
        }
    }

    // source: src/echarts/templates/pyramid.ts:35 (colorField = color ?? group) and src/plotly/templates/bar.ts:500 (color ||
    // group): the first two group values are the two sides while color does not resolve, with x and y resolved (pyramid.ts:30,
    // bar.ts:495 return early without them); a count aggregate resolves, as _count (src/core/resolve-semantics.ts:625-637),
    // but group itself needs a field without one (:682-685);
    // the Vega-Lite Pyramid does not read group
    [Test]
    public void Layout_GroupOfAPyramid_IsTheSplitWithoutAColorField()
    {
        string[] none = [];
        string[] group = ["chart_spec.encodings.group"];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Unsupported(Layout("Pyramid Chart", ("x", "revenue"), ("y", "city"), ("group", "cost")), FlintBackend.ECharts), Is.EqualTo(none), "echarts");
            Assert.That(Unsupported(Layout("Pyramid Chart", ("x", "revenue"), ("y", "city"), ("group", "cost")), FlintBackend.Plotly), Is.EqualTo(none), "plotly");
            Assert.That(Unsupported(Layout("Pyramid Chart", ("x", "revenue"), ("y", "city"), ("color", ""), ("group", "cost")), FlintBackend.ECharts), Is.EqualTo(none), "an empty color field");
            Assert.That(Unsupported(Layout("Pyramid Chart", ("x", "revenue"), ("y", "city"), ("color", "city"), ("group", "cost")), FlintBackend.ECharts), Is.EqualTo(group), "a color field wins");
            Assert.That(Unsupported(Layout("Pyramid Chart", ("x", "revenue"), ("y", "city"), ("color", new ChartEncoding { Aggregate = Aggregate.Count }), ("group", "cost")), FlintBackend.ECharts), Is.EqualTo(group), "a count color wins");
            Assert.That(Unsupported(Layout("Pyramid Chart", ("x", new ChartEncoding { Aggregate = Aggregate.Count }), ("y", "city"), ("group", "cost")), FlintBackend.Plotly), Is.EqualTo(none), "a count x");
            Assert.That(Unsupported(Layout("Pyramid Chart", ("x", "revenue"), ("y", "city"), ("group", "")), FlintBackend.Plotly), Is.EqualTo(group), "an empty group field");
            Assert.That(Unsupported(Layout("Pyramid Chart", ("x", "revenue"), ("y", "city"), ("group", new ChartEncoding { Aggregate = Aggregate.Count })), FlintBackend.ECharts), Is.EqualTo(group), "a count group: a constant split, not a field's values");
            Assert.That(Unsupported(Layout("Pyramid Chart", ("x", "revenue"), ("y", "city"), ("group", new ChartEncoding { Field = "cost", Aggregate = Aggregate.Count })), FlintBackend.ECharts), Is.EqualTo(group), "a counted group field is the constant _count");
            Assert.That(Unsupported(Layout("Pyramid Chart", ("x", "revenue"), ("y", "city"), ("group", "cost")), FlintBackend.VegaLite), Is.EqualTo(group), "vega-lite");
            Assert.That(Unsupported(Layout("Pyramid Chart", ("y", "city"), ("group", "cost")), FlintBackend.ECharts), Is.EqualTo(group), "no x field: nothing is drawn (pyramid.ts:30)");
        }
    }

    // source: probes.json channelReads.layoutReads records these three (echarts Waterfall Chart group, vegalite Map row, vegalite
    // Density Plot y) as reads, but rendered with vega-lite 6.5.0 and echarts 6.1.0 the bound field reaches the spec and not the
    // chart's marks, so the warning stays: src/echarts/instantiate-spec.ts:447-451 (a legend title); src/vegalite/templates/map.ts:218-226,
    // :463 and vega-lite src/encoding.ts initEncoding over UNIT_CHANNELS (a facet inside a layer is dropped); src/core/structure.ts:403-416
    // (an axis title)
    [Test]
    public void Layout_NameOnlyAndDroppedChannels_StayUnsupported()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Unsupported(Layout("Waterfall Chart", ("x", "city"), ("y", "revenue"), ("group", "cost")), FlintBackend.ECharts), Is.EqualTo(new[] { "chart_spec.encodings.group" }), "echarts waterfall group: legend title only");
            Assert.That(Unsupported(Layout("Map", ("longitude", "revenue"), ("latitude", "cost"), ("row", "city")), FlintBackend.VegaLite), Is.EqualTo(new[] { "chart_spec.encodings.row" }), "vega-lite map row");
            Assert.That(Unsupported(Layout("Density Plot", ("color", "city"), ("y", "revenue")), FlintBackend.VegaLite), Is.EqualTo(new[] { "chart_spec.encodings.y" }), "vega-lite density y");
        }
    }
}
