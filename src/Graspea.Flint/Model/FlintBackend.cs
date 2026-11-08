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

    /// <summary>Image-Charts (<c>image-charts</c>): an <c>https://image-charts.com</c> image URL that carries the chart's
    /// values, which reach that service when the image is loaded.</summary>
    ImageCharts,
}
