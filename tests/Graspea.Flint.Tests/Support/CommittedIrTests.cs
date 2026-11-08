using System.Text.Json.Nodes;

namespace Graspea.Flint.Tests.Support;

public sealed class CommittedIrTests
{
    // source: generator/src/retired.ts:31 (TEMPLATE: a backend id is [a-z][a-z-]*, so a retired Image-Charts channel is put
    // back into the facade catalog like any other) and ir/flint-ir.json (Sparkline declares x, y and color on image-charts)
    [Test]
    public void PutBack_HyphenatedBackendId_RestoresTheChannel()
    {
        var catalog = CommittedIr.Catalog.DeepClone();
        var chartTypes = catalog["chartTypes"]!.AsArray();
        var count = chartTypes.Count;
        var sparkline = chartTypes.Single(t => t!["name"]!.GetValue<string>() == "Sparkline")!;

        Assert.That(CommittedIr.PutBack(catalog, "catalog.chartTypes[Sparkline].backends.image-charts.channels[size]", JsonValue.Create("size")!), Is.True);
        Assert.That(CommittedIr.Strings(sparkline["backends"]!["image-charts"]!["channels"]), Is.EqualTo(new[] { "x", "y", "color", "size" }));
        Assert.That(CommittedIr.PutBack(catalog, "catalog.chartTypes[Sparkline].backends.image-charts.channels[color]", JsonValue.Create("color")!), Is.False);
        Assert.That(chartTypes, Has.Count.EqualTo(count));
        Assert.That(() => CommittedIr.PutBack(catalog, "catalog.chartTypes[Sparkline].backends.Image_Charts.channels[x]", JsonValue.Create("x")!), Throws.InvalidOperationException);
    }
}
