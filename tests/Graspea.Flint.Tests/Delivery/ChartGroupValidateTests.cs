using Graspea.Flint.Delivery;
using Graspea.Flint.Serialization;
using Graspea.Flint.Validation;

namespace Graspea.Flint.Tests.Delivery;

public sealed class ChartGroupValidateTests
{
    private const string Base = "charts.c.input.chart_spec.";

    private static LineChartBuilder<Sale> Builder() => Chart.Line(DeliveryJson.Default.Sale);

    private static IReadOnlyList<ValidationIssue> Fixed(ChartTemplate<Sale> template, string id = "c") =>
        new ChartGroup("g", new ChartHandle<Sale>(id, template)).Validate().Issues.Where(i => i.Code.StartsWith("fixed_", StringComparison.Ordinal)).ToList();

    private static IReadOnlyList<ValidationIssue> Fixed(LineChartBuilder<Sale> builder) => Fixed(builder.ToTemplate());

    // source: §14.7 table; flint-chart 1.0.1 src/core/decisions.ts:442-453, src/core/resolve-semantics.ts:549-550
    [Test]
    public void TypeMissing_BoundWithoutType()
    {
        var issues = Fixed(Builder().Encode(Channel.X, "Month").Encode(Channel.Y, "Revenue"));
        var x = issues.Single(i => i.Code == "fixed_type_missing" && i.Channel == Channel.X);
        Assert.That(x.Path, Is.EqualTo(Base + "encodings.x.type"));
        Assert.That(x.Field, Is.EqualTo("Month"));
        Assert.That(x.Severity, Is.EqualTo(IssueSeverity.Info));
        Assert.That(x.Message, Does.EndWith("."));

        var typed = Fixed(Builder().Encoding(Channel.X, e => { e.Field = "Month"; e.Type = EncodingType.Quantitative; }));
        Assert.That(typed.Where(i => i.Code == "fixed_type_missing"), Is.Empty);

        var unbound = Fixed(Builder().Encoding(Channel.Color, e => e.Reverse = true));
        Assert.That(unbound.Where(i => i.Code == "fixed_type_missing"), Is.Empty);
    }

    // source: ValidationRun.cs:827-828 (an empty encoding key has no Channel)
    [Test]
    public void EmptyEncodingKey_ReportsWithoutThrowing()
    {
        var spec = FlintJson.Deserialize("{\"chart_spec\":{\"chartType\":\"Line Chart\",\"encodings\":{\"\":{\"field\":\"month\"},\"y\":{\"field\":\"revenue\"}}}}");
        var template = ChartTemplate.Create(spec, DeliveryJson.Default.Sale);
        ValidationResult result = null!;
        Assert.That(() => result = new ChartGroup("g", new ChartHandle<Sale>("c", template)).Validate(), Throws.Nothing);
        Assert.That(result.Issues.Single(i => i.Code == "unsupported_channel").Path, Is.EqualTo("charts.c.input.chart_spec.encodings."));
        var missing = result.Issues.Single(i => i.Code == "fixed_type_missing" && i.Field == "month");
        Assert.That(missing.Path, Is.EqualTo("charts.c.input.chart_spec.encodings..type"));
        Assert.That(missing.Channel, Is.Null);
    }

    // source: §14.7 table; resolve-semantics.ts:109-165 (only array strings with category values survive)
    [TestCase(null)]
    [TestCase("x")]
    [TestCase("color")]
    [TestCase("Revenue")]
    [TestCase("[]")]
    [TestCase("[null]")]
    [TestCase("not json")]
    [TestCase("  ")]
    public void OrderMissing_NominalWithoutArray(string? sortBy)
    {
        var issues = Fixed(Builder().Encoding(Channel.X, e => { e.Field = "Region"; e.Type = EncodingType.Nominal; e.SortBy = sortBy; }));
        var issue = issues.Single(i => i.Code == "fixed_order_missing");
        Assert.That(issue.Path, Is.EqualTo(Base + "encodings.x.sortBy"));
        Assert.That(issue.Channel, Is.EqualTo(Channel.X));
        Assert.That(issue.Field, Is.EqualTo("Region"));
    }

    // source: §14.7 table (fixed_order_missing covers nominal and ordinal)
    [Test]
    public void OrderMissing_OrdinalWithoutArray()
    {
        var issues = Fixed(Builder().Encoding(Channel.X, e => { e.Field = "Region"; e.Type = EncodingType.Ordinal; }));
        Assert.That(issues.Single(i => i.Code == "fixed_order_missing").Path, Is.EqualTo(Base + "encodings.x.sortBy"));
    }

    // source: §14.7 table; resolve-semantics.ts:109-165 (an array of non-string categories counts as an order)
    [TestCase("[1]")]
    [TestCase("[true]")]
    public void OrderMissing_NonStringCategoriesGiveNone(string sortBy)
    {
        var issues = Fixed(Builder().Encoding(Channel.X, e => { e.Field = "Region"; e.Type = EncodingType.Nominal; e.SortBy = sortBy; }));
        Assert.That(issues.Where(i => i.Code == "fixed_order_missing"), Is.Empty);
    }

    // source: §14.7 table (a count aggregate binds without a field)
    [Test]
    public void TypeMissing_CountWithoutField()
    {
        var issues = Fixed(Builder().Encoding(Channel.Y, e => e.Aggregate = Aggregate.Count));
        Assert.That(issues.Single(i => i.Code == "fixed_type_missing").Path, Is.EqualTo(Base + "encodings.y.type"));
    }

    // source: §14.7 table (fixed_order_missing)
    [Test]
    public void OrderMissing_ExplicitOrderAndQuantitativeGiveNone()
    {
        var explicitOrder = Fixed(Builder().Encoding(Channel.X, e =>
        {
            e.Field = "Region";
            e.Type = EncodingType.Nominal;
            e.SortBy = SortBy.ExplicitOrder("North", "South");
        }));
        Assert.That(explicitOrder.Where(i => i.Code == "fixed_order_missing"), Is.Empty);

        var quantitative = Fixed(Builder().Encoding(Channel.Y, e => { e.Field = "Revenue"; e.Type = EncodingType.Quantitative; }));
        Assert.That(quantitative.Where(i => i.Code == "fixed_order_missing"), Is.Empty);
    }

    // source: §14.7 table (series entries are indexed)
    [Test]
    public void OrderMissing_SeriesEntryIsIndexed()
    {
        var template = Builder().Encode(Channel.Y, EncodingValue.Series(
            new ChartEncoding { Field = "Revenue", Type = EncodingType.Quantitative },
            new ChartEncoding { Field = "Region", Type = EncodingType.Nominal })).ToTemplate();
        var issue = Fixed(template).Single(i => i.Code == "fixed_order_missing");
        Assert.That(issue.Path, Is.EqualTo(Base + "encodings.y[1].sortBy"));
    }

    // source: §14.7 table; src/core/aggregate.ts:104
    [Test]
    public void Aggregate_Reported()
    {
        var issues = Fixed(Builder().Encoding(Channel.Y, e => { e.Field = "Revenue"; e.Type = EncodingType.Quantitative; e.Aggregate = Aggregate.Sum; }));
        var issue = issues.Single(i => i.Code == "fixed_aggregate");
        Assert.That(issue.Path, Is.EqualTo(Base + "encodings.y.aggregate"));
        Assert.That(issue.Channel, Is.EqualTo(Channel.Y));
        Assert.That(issue.Field, Is.EqualTo("Revenue"));

        var none = Fixed(Builder().Encoding(Channel.Y, e => { e.Field = "Revenue"; e.Type = EncodingType.Quantitative; }));
        Assert.That(none.Where(i => i.Code == "fixed_aggregate"), Is.Empty);
    }

    // source: §14.7 table; src/core/pivot.ts:821-822, :919-930, :964
    [Test]
    public void HostOverride_OnlyThreeKeysWithValues()
    {
        var issues = Fixed(Builder().Property("chartType", "Bar Chart").Property("arrange", "rows").Property("pivot", true)
            .Property("colorScheme", "viridis"));
        var paths = issues.Where(i => i.Code == "fixed_host_override").Select(i => i.Path).ToList();
        Assert.That(paths, Is.EquivalentTo(new[] { Base + "chartProperties.chartType", Base + "chartProperties.arrange", Base + "chartProperties.pivot" }));

        // Property(key, null) removes the key, so put a real JSON null in through Configure.
        var template = Builder().Configure(i => { i.ChartSpec!.ChartProperties ??= new(); i.ChartSpec.ChartProperties["chartType"] = null; }).ToTemplate();
        Assert.That(template.ToJson(), Does.Contain("\"chartType\":null"));
        Assert.That(Fixed(template).Where(i => i.Code == "fixed_host_override"), Is.Empty);
    }

    // source: §14.7 table; src/core/compute-layout.ts:495-546
    [Test]
    public void Size_Reported()
    {
        var issues = Fixed(Builder().BaseSize(300, 200).CanvasSize(600, 400)).Where(i => i.Code == "fixed_size").Select(i => i.Path).ToList();
        Assert.That(issues, Is.EquivalentTo(new[] { Base + "baseSize", Base + "canvasSize" }));
        Assert.That(Fixed(Builder()).Where(i => i.Code == "fixed_size"), Is.Empty);
    }

    // source: §14.7 (Info; IsValid unchanged); §14.4.1 Validate builds on template.Validate
    [Test]
    public void Ordering_TemplateIssuesFirstAndInfoOnly()
    {
        var a = new ChartHandle<Sale>("a", Builder().Encode(Channel.X, "Month").Encode(Channel.Y, "Revenue").Encoding(Channel.Color, e => e.Field = "nosuch").ToTemplate());
        var b = new ChartHandle<Sale>("b", Builder().Encode(Channel.X, "Month").BaseSize(10, 10).ToTemplate());
        var result = new ChartGroup("g", a, b).Validate();
        var codes = result.Issues.Select(i => (i.Path.Split('.')[1], i.Code.StartsWith("fixed_", StringComparison.Ordinal))).ToList();
        Assert.That(codes, Is.Ordered, "per chart: template issues, then fixed ones; charts in group order");
        Assert.That(result.Issues.Where(i => i.Code.StartsWith("fixed_", StringComparison.Ordinal)), Is.Not.Empty.And.All.Property("Severity").EqualTo(IssueSeverity.Info));
        var withoutFixed = result.Issues.Where(i => !i.Code.StartsWith("fixed_", StringComparison.Ordinal)).ToList();
        Assert.That(result.IsValid, Is.EqualTo(withoutFixed.All(i => i.Severity != IssueSeverity.Error)));
    }

    // source: §14.7 (reported from ChartGroup.Validate only); §14.8.5 step 10
    [Test]
    public void Isolation_TemplateAndBindNeverReportFixed()
    {
        var template = Builder().Encode(Channel.X, "Month").Encoding(Channel.Y, e => { e.Field = "Revenue"; e.Aggregate = Aggregate.Sum; })
            .Encoding(Channel.Color, e => { e.Field = "Region"; e.Type = EncodingType.Nominal; })
            .Property("pivot", true).BaseSize(10, 10).ToTemplate();
        Assert.That(Fixed(template).Select(i => i.Code).Distinct(),
            Is.EquivalentTo(new[] { "fixed_type_missing", "fixed_aggregate", "fixed_order_missing", "fixed_host_override", "fixed_size" }));
        Assert.That(template.Validate().Issues.Select(i => i.Code), Has.None.StartWith("fixed_"));
        var bound = ChartValidator.Validate(template.Bind([new Sale(new DateOnly(2025, 1, 1), "North", 1m)]));
        Assert.That(bound.Issues.Select(i => i.Code), Has.None.StartWith("fixed_"));
    }

    // source: §14.4.4 start-up check
    [Test]
    public void ProfileCompliantGroup_ReportsNoFixed()
    {
        var template = Builder()
            .Encoding(Channel.X, e => { e.Field = "Region"; e.Type = EncodingType.Nominal; e.SortBy = SortBy.ExplicitOrder("North", "South"); })
            .Encoding(Channel.Y, e => { e.Field = "Revenue"; e.Type = EncodingType.Quantitative; })
            .ToTemplate();
        Assert.That(Fixed(template), Is.Empty);
    }
}
