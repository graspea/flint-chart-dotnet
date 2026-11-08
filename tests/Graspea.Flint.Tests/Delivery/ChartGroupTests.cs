using System.Collections;
using Graspea.Flint.Delivery;
using Graspea.Flint.Validation;

namespace Graspea.Flint.Tests.Delivery;

public sealed class ChartGroupTests
{
    private static ChartHandle Handle(string id) =>
        new ChartHandle<Sale>(id, Chart.Line(DeliveryJson.Default.Sale).X(r => r.Month).Y(r => r.Revenue).ToTemplate());

    // source: §14.4.1 ChartGroup summary; §14.11 defaults
    [Test]
    public void ValidName_KeepsNameAndOrder()
    {
        var a = Handle("a");
        var b = Handle("b");
        var group = new ChartGroup("overview", a, b);
        Assert.That(group.Name, Is.EqualTo("overview"));
        Assert.That(group.Charts, Is.EqualTo(new[] { a, b }).AsCollection);
    }

    // source: §14.4.1 ChartGroup summary; §14.5 rule 3 (names, including "." and "..")
    [TestCase(".")]
    [TestCase("..")]
    [TestCase("__proto__")]
    public void LegalNames_AreAccepted(string name) =>
        Assert.That(new ChartGroup(name, Handle("a")).Name, Is.EqualTo(name));

    // source: §14.4.1 ChartGroup summary
    [Test]
    public void Invalid_Throws()
    {
        Assert.That(() => new ChartGroup("bad name", Handle("a")), Throws.ArgumentException);
        Assert.That(() => new ChartGroup("", Handle("a")), Throws.ArgumentException);
        Assert.That(() => new ChartGroup(null!, Handle("a")), Throws.ArgumentNullException);
        Assert.That(() => new ChartGroup("g"), Throws.ArgumentException);
        Assert.That(() => new ChartGroup("g", [Handle("a"), null!]), Throws.ArgumentException);
        Assert.That(() => new ChartGroup("g", Handle("a"), Handle("a")), Throws.ArgumentException);
        var same = Handle("a");
        Assert.That(() => new ChartGroup("g", same, same), Throws.ArgumentException);
    }

    // source: §14.4.1 ChartGroup snapshot (copies the array, read-only view)
    [Test]
    public void Charts_IsASnapshotAndReadOnly()
    {
        var a = Handle("a");
        var b = Handle("b");
        var input = new[] { a, b };
        var group = new ChartGroup("g", input);
        input[0] = b;
        Assert.That(group.Charts[0], Is.SameAs(a));
        Assert.That(group.Charts as ChartHandle[], Is.Null);
        Assert.That(group.Charts as IList, Is.Not.Null.And.Property("IsReadOnly").True);
        Assert.That(() => ((IList)group.Charts)[0] = b, Throws.InstanceOf<NotSupportedException>());
    }

    private static ChartTemplate<Sale> BrokenTemplate() =>
        Chart.Line(DeliveryJson.Default.Sale).X(r => r.Month).Y(r => r.Revenue)
            .Encoding(Channel.Color, e => e.Field = "nosuch").ToTemplate();

    // source: §14.7 first paragraph; ValidationRun.cs:149-162, :252-261, :119-123; §14.4.1 Validate adds only Info issues
    [Test]
    public void Validate_RepathsTemplateIssues()
    {
        var goodTemplate = Chart.Line(DeliveryJson.Default.Sale).X(r => r.Month).Y(r => r.Revenue).ToTemplate();
        var brokenTemplate = BrokenTemplate();
        var good = new ChartHandle<Sale>("good", goodTemplate);
        var broken = new ChartHandle<Sale>("broken", brokenTemplate);
        var group = new ChartGroup("g", good, broken);

        var result = group.Validate();
        Assert.That(result.Issues, Has.Some.Matches<ValidationIssue>(i => i.Code == "rows_not_inspected" && i.Path == "charts.good.input.data"));
        Assert.That(result.Issues, Has.Some.Matches<ValidationIssue>(i => i.Code == "rows_not_inspected" && i.Path == "charts.broken.input.data"));
        var unknown = result.Issues.Single(i => i.Code == "unknown_field");
        Assert.That(unknown.Path, Is.EqualTo("charts.broken.input.chart_spec.encodings.color.field"));
        Assert.That(unknown.Channel, Is.EqualTo(Channel.Color));
        Assert.That(unknown.Field, Is.EqualTo("nosuch"));

        static (IssueSeverity, string, string, string, Channel?, string?) Key(ValidationIssue i, string prefix) =>
            (i.Severity, i.Code, i.Message, prefix + i.Path, i.Channel, i.Field);
        var expected = goodTemplate.Validate().Issues.Select(i => Key(i, "charts.good.input."))
            .Concat(brokenTemplate.Validate().Issues.Select(i => Key(i, "charts.broken.input.")));
        Assert.That(result.Issues.Where(i => !i.Code.StartsWith("fixed_", StringComparison.Ordinal)).Select(i => Key(i, "")), Is.EqualTo(expected));
        Assert.That(result.IsValid, Is.EqualTo(goodTemplate.Validate().IsValid && brokenTemplate.Validate().IsValid));
        Assert.That(new ChartGroup("ok", good).Validate().IsValid, Is.True);

        var doughnutTemplate = Chart.Of(ChartType.DoughnutChart, DeliveryJson.Default.Sale)
            .Encode(Channel.Color, r => r.Region).Encode(Channel.Y, r => r.Revenue).ToTemplate();
        var doughnut = new ChartHandle<Sale>("d", doughnutTemplate);
        var onEcharts = new ChartGroup("e", doughnut).Validate(FlintBackend.ECharts);
        var notOnBackend = onEcharts.Issues.Single(i => i.Code == "chart_type_not_on_backend");
        Assert.That(notOnBackend.Path, Is.EqualTo("charts.d.input.chart_spec.chartType"));
        Assert.That(notOnBackend.Severity, Is.EqualTo(IssueSeverity.Error));
        Assert.That(onEcharts.IsValid, Is.False);
        Assert.That(onEcharts.IsValid, Is.EqualTo(doughnutTemplate.Validate(FlintBackend.ECharts).IsValid));
    }
}
