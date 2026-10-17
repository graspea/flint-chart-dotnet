namespace Graspea.Flint;

/// <summary>The flint-chart assemblers a document can target.</summary>
public enum FlintBackend
{
    /// <summary>Vega-Lite (<c>vegalite</c>).</summary>
    VegaLite,

    /// <summary>Apache ECharts (<c>echarts</c>).</summary>
    ECharts,

    /// <summary>Chart.js (<c>chartjs</c>).</summary>
    ChartJs,

    /// <summary>Plotly (<c>plotly</c>).</summary>
    Plotly,

    /// <summary>Excel native charts (<c>excel</c>).</summary>
    Excel,
}
