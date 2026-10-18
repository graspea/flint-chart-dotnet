using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Model;

public sealed class FlintBackendTests
{
    // source: design §4.1 (FlintBackend { VegaLite, ECharts, ChartJs, Plotly, Excel }) and ir/flint-ir.json catalog.backends
    [Test]
    public void Members_FollowTheIrBackendList()
    {
        Assert.That(Enum.GetNames<FlintBackend>(), Is.EqualTo(new[] { "VegaLite", "ECharts", "ChartJs", "Plotly", "Excel" }));
        Assert.That(CommittedIr.Strings(CommittedIr.Catalog["backends"]), Is.EqualTo(new[] { "vegalite", "echarts", "chartjs", "plotly", "excel" }));
    }
}
