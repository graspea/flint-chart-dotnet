using System.Text.Json.Serialization.Metadata;

namespace Graspea.Flint;

/// <summary>A builder for any chart type, including templates this version does not know (<c>Chart.Of</c>); it has only
/// the core setters of <see cref="ChartBuilder{TSelf, TRow}"/>.</summary>
/// <typeparam name="TRow">The row type.</typeparam>
public sealed partial class GenericChartBuilder<TRow> : ChartBuilder<GenericChartBuilder<TRow>, TRow>
{
    internal GenericChartBuilder(ChartType chartType, ChartRows<TRow> rows)
        : base(chartType, rows.Data, rows.TypeInfo)
    {
    }
}
