namespace Graspea.Flint.Validation.Rules;

/// <summary>
/// The channels a layout backend (Vega-Lite, ECharts, Chart.js, Plotly) of the pinned package draws from the field's
/// values though its template does not declare them; <c>unsupported_channel</c> is not reported for them.
/// <list type="bullet">
/// <item><c>column</c> and <c>row</c> with a field (<see cref="FieldTypes.FieldOf"/>) and no <c>count</c> aggregate facet the chart, one panel per value,
/// each drawing its own subset, on the templates of <see cref="Facets"/>. ECharts and Plotly facet only a template that
/// declares <c>x</c> or <c>y</c> (src/echarts/assemble.ts:351-361, src/plotly/assemble.ts:332-346), Plotly not one that
/// sets <c>selfManagesFacets</c> (the Sparkline, src/plotly/templates/sparkline.ts:78). That gate is not the rule: ECharts
/// also facets the Calendar Heatmap, Funnel Chart, Network Graph and Sankey Diagram, and they stay reported. The faceted
/// option keeps only the grids, axes, series, tooltip, color and legend, and gives each series an axis index
/// (src/echarts/facet.ts:353-359, :424-428): a Calendar Heatmap loses its <c>calendar</c> and <c>visualMap</c> and does
/// not render, and the funnel, graph and sankey series keep the canvas position their template sets
/// (src/echarts/templates/funnel.ts:140-143, graph.ts:145-148, sankey.ts:152-155), so every panel's chart is drawn in the
/// same place. Vega-Lite facets only the Ranged Dot Plot, whose template writes every resolved encoding on the top-level
/// encoding (src/vegalite/templates/scatter.ts:322-327; src/vegalite/assemble.ts:1185-1189 always resolves the two, and
/// restructureFacets, :1410-1501, makes them the facet). Every Chart.js template declares both channels.</item>
/// <item><c>group</c> with a field and no <c>count</c> aggregate of a Pyramid Chart on ECharts and Plotly while <c>color</c> does not resolve: its values
/// are the two sides (src/echarts/templates/pyramid.ts:35, src/plotly/templates/bar.ts:500); both templates draw nothing
/// unless <c>x</c> and <c>y</c> resolve (pyramid.ts:30, bar.ts:495). A channel resolves as
/// <c>channelSemantics[ch]?.field</c> reads it: a non-empty field, or a <c>count</c> aggregate, which upstream names
/// <c>_count</c> (src/core/resolve-semantics.ts:625-637). A <c>count</c> aggregate on <c>column</c>, <c>row</c> or <c>group</c>
/// itself is not exempt, with a field or without: upstream renames the field to <c>_count</c> (:682-685), so the facet is one
/// panel headed by the count and the split is a constant, and neither draws the field's values.</item>
/// </list>
/// </summary>
internal static class LayoutReads
{
    /// <summary>The templates whose <c>column</c> and <c>row</c> fields the backend draws as facet panels, each in its own
    /// grid or subplot, though the template leaves both out (rendered with vega-lite 6.5.0 and echarts 6.1.0; Plotly read
    /// from its subplot domains and annotations).</summary>
    private static readonly HashSet<(FlintBackend Backend, string ChartType)> Facets =
    [
        (FlintBackend.VegaLite, "Ranged Dot Plot"),
        (FlintBackend.ECharts, "Pyramid Chart"), (FlintBackend.ECharts, "Ranged Dot Plot"),
        (FlintBackend.Plotly, "Funnel Chart"), (FlintBackend.Plotly, "Pyramid Chart"), (FlintBackend.Plotly, "Ranged Dot Plot"), (FlintBackend.Plotly, "Rose Chart"),
    ];

    internal static bool ReadsUndeclared(FlintBackend backend, ChartAssemblyInput input, FieldTypes types, string channel)
    {
        var chartType = input.ChartSpec.ChartType.Name;
        return channel switch
        {
            "column" or "row" => HasField(types, channel) && Facets.Contains((backend, chartType)),
            "group" => chartType == "Pyramid Chart" && backend is (FlintBackend.ECharts or FlintBackend.Plotly)
                && HasField(types, "group") && Resolves(types, "x") && Resolves(types, "y") && !Resolves(types, "color"),
            _ => false,
        };
    }

    /// <summary>Why an ECharts facet of <c>column</c> or <c>row</c> with a field (no <c>count</c> aggregate) is broken, for the four
    /// templates the class docs name, in the words of <c>unsupported_channel</c>; null for any other case.</summary>
    internal static string? BrokenFacet(FlintBackend backend, ChartAssemblyInput input, FieldTypes types, string channel) =>
        backend == FlintBackend.ECharts && channel is "column" or "row" && HasField(types, channel)
            ? input.ChartSpec.ChartType.Name switch
            {
                "Funnel Chart" or "Network Graph" or "Sankey Diagram" => "echarts facets it, but draws every panel's chart in the same place",
                "Calendar Heatmap" => "echarts facets it, but the faceted chart loses its calendar and color scale and does not render",
                _ => null,
            }
            : null;

    /// <summary>A non-empty field without a <c>count</c> aggregate: upstream renames a counted field to <c>_count</c>
    /// (src/core/resolve-semantics.ts:682-685), so the facet or split is by the count, not the field's values.</summary>
    private static bool HasField(FieldTypes types, string channel) =>
        types.FieldOf(channel) is not null && types.Encoding(channel)?.Aggregate?.Name != "count";

    /// <summary>Whether <c>channelSemantics[ch]</c> exists: a non-empty field or a <c>count</c> aggregate.</summary>
    private static bool Resolves(FieldTypes types, string channel) => types.Of(channel) is not FieldType.Absent;
}
