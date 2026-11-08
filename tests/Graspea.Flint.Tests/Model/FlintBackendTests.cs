using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Model;

public sealed class FlintBackendTests
{
    // source: design §4.1 (FlintBackend { VegaLite, ECharts, ChartJs, Plotly, Excel }), part 9 §14.8.1 (ImageCharts appended
    // last) and ir/flint-ir.json catalog.backends
    [Test]
    public void Members_FollowTheIrBackendList()
    {
        Assert.That(Enum.GetNames<FlintBackend>(), Is.EqualTo(new[] { "VegaLite", "ECharts", "ChartJs", "Plotly", "Excel", "ImageCharts" }));
        Assert.That(CommittedIr.Strings(CommittedIr.Catalog["backends"]), Is.EqualTo(new[] { "vegalite", "echarts", "chartjs", "plotly", "excel", "image-charts" }));
    }
}
