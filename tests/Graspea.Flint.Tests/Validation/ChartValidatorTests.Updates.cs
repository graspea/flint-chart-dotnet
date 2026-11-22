using Graspea.Flint.Validation;

namespace Graspea.Flint.Tests.Validation;

/// <summary>The rules on <c>ChartAssemblyInput.Updates</c> (flint-chart 1.1.0+).</summary>
public sealed partial class ChartValidatorTests
{
    private static readonly FlintBackend[] UpdateReadingBackends = [FlintBackend.VegaLite, FlintBackend.ECharts, FlintBackend.ChartJs, FlintBackend.Plotly];

    // source: flint-chart 1.1.1 src/core/spec-updates.ts:9-15 (noteSpecUpdates reads the id of every entry of a non-empty
    // updates; assembleVegaLite returns through it at src/vegalite/assemble.ts:952, assembleECharts at
    // src/echarts/assemble.ts:612, assembleChartjs at src/chartjs/assemble.ts:509 and assemblePlotly at
    // src/plotly/assemble.ts:737, so a null entry throws there; src/excel and src/image-charts never read updates) and the
    // probe case "bar with a null update" (tests/fixtures/flint-chart@1.1.1/probe-expectations.jsonl)
    [Test]
    public void Validate_NullUpdate_IsErrorOnTheLayoutBackends()
    {
        var input = Bar(i => i.Updates = [new ChartUpdate { Id = "note", Ops = [] }, null!]);

        foreach (var backend in UpdateReadingBackends)
        {
            AssertOne(input, backend, "update_null", IssueSeverity.Error);
        }

        AssertNone(input, FlintBackend.Excel, "update_null");
        AssertNone(input, FlintBackend.ImageCharts, "update_null");
        var issue = Issues(input, null, "update_null").Single();
        Assert.That((issue.Severity, issue.Path), Is.EqualTo((IssueSeverity.Error, "updates[1]")));
        Assert.That(issue.Message, Is.EqualTo(
            "updates[1] is null; flint-chart reads the id of every update to note that a static output draws the chart without them. Throws on vegalite, echarts, chartjs, plotly."));
        Assert.That(ChartValidator.Validate(input, FlintBackend.Excel).IsValid, Is.True);
    }

    // source: flint-chart 1.1.1 src/core/spec-updates.ts:9-15 (reads only the count and each update's id: an update of any
    // other shape, an op that is null and an empty list assemble on the four layout backends, [{}] and [{id, ops: []}] too)
    [Test]
    public void Validate_UpdatesWithoutANullEntry_AreNotReported()
    {
        AssertNone(Bar(i => i.Updates = [new ChartUpdate()]), null, "update_null");
        AssertNone(Bar(i => i.Updates = [new ChartUpdate { Id = "note", Ops = [null!] }]), null, "update_null");
        AssertNone(Bar(i => i.Updates = []), null, "update_null");
        Assert.That(ChartValidator.Validate(Bar(i => i.Updates = [new ChartUpdate { Id = "note", Ops = [new SetOrderOp { Scope = "category", Field = "city" }] }])).Issues, Is.Empty);
    }
}
