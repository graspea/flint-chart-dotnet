using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Model;

/// <summary>Generated struct constants against the committed IR (design §4.1 struct table, §7.6).</summary>
public sealed class GeneratedConstantsTests
{
    // source: ir/flint-ir.json catalog.chartTypes (names) and design §4.1 (LineChart, KpiCard, DonutChart, DoughnutChart, Map)
    [TestCase("Line Chart", "LineChart")]
    [TestCase("KPI Card", "KpiCard")]
    [TestCase("Donut Chart", "DonutChart")]
    [TestCase("Doughnut Chart", "DoughnutChart")]
    [TestCase("Map", "Map")]
    public void ChartType_Constant_HasTheUpstreamName(string upstream, string member)
    {
        var value = (ChartType)typeof(ChartType).GetField(member)!.GetValue(null)!;
        Assert.That(value.Name, Is.EqualTo(upstream));
    }

    // source: ir/flint-ir.json catalog.chartTypes (48 on 0.5.1, counted from the IR, not assumed)
    [Test]
    public void ChartType_HasOneConstantPerIrChartType()
    {
        var names = CommittedIr.Catalog["chartTypes"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToArray();
        Assert.That(StaticValues<ChartType>().Select(v => v.Name), Is.EquivalentTo(names));
    }

    // source: ir/flint-ir.json catalog.channels (the union includes theta and not text)
    [Test]
    public void Channel_ConstantsEqualTheIrChannelUnion()
    {
        Assert.That(StaticValues<Channel>().Select(v => v.Name), Is.EquivalentTo(CommittedIr.Strings(CommittedIr.Catalog["channels"])));
        Assert.That(Channel.Theta.Name, Is.EqualTo("theta"));
        Assert.That(typeof(Channel).GetField("Text"), Is.Null);
    }

    // source: ir/flint-ir.json catalog.semanticTypes (verbatim names) and generator/overrides.json renames (Name -> NameType)
    [Test]
    public void SemanticType_ConstantsAreVerbatimExceptTheReviewedRename()
    {
        var names = CommittedIr.Catalog["semanticTypes"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToArray();
        Assert.That(StaticValues<SemanticType>().Select(v => v.Name), Is.EquivalentTo(names));
        Assert.That(SemanticType.YearMonth.Name, Is.EqualTo("YearMonth"));
        Assert.That(SemanticType.ID.Name, Is.EqualTo("ID"));
        Assert.That(SemanticType.NameType.Name, Is.EqualTo("Name"));
    }

    // source: design §10.4 test 9 (an unregistered name reads with IsRegistered == false)
    [Test]
    public void SemanticType_IsRegistered_FollowsTheRegistry()
    {
        Assert.That(SemanticType.Amount.IsRegistered, Is.True);
        Assert.That(new SemanticType("Revenue").IsRegistered, Is.False);
        Assert.That(default(SemanticType).IsRegistered, Is.False);
    }

    // source: ir/flint-ir.json catalog.themePresets (ids; Pascal names per design §7.6 rule 5) and generator/overrides.json
    // renames (owner decision 2026-10-17: powerbi -> PowerBi, powerbi-light -> PowerBiLight)
    [Test]
    public void ThemePreset_ConstantsEqualTheIrPresets()
    {
        var ids = CommittedIr.Catalog["themePresets"]!.AsArray().Select(t => t!["id"]!.GetValue<string>()).ToArray();
        Assert.That(StaticValues<ThemePreset>().Select(v => v.Name), Is.EquivalentTo(ids));
        Assert.That(ThemePreset.PowerBi.Name, Is.EqualTo("powerbi"));
        Assert.That(ThemePreset.PowerBiLight.Name, Is.EqualTo("powerbi-light"));
        Assert.That(typeof(ThemePreset).GetField("Powerbi"), Is.Null);
        Assert.That(typeof(ThemePreset).GetField("PowerbiLight"), Is.Null);
        Assert.That(ThemePreset.Economist.Name, Is.EqualTo("economist"));
    }

    // source: d.ts T:1994-2001 (ChartEncoding literal unions) and T:3233-3254 (seriesCountAxis)
    [Test]
    public void LiteralStructs_ConstantsEqualTheIrLiteralUnions()
    {
        Assert.That(StaticValues<EncodingType>().Select(v => v.Name), Is.EquivalentTo(new[] { "quantitative", "nominal", "ordinal", "temporal" }));
        Assert.That(StaticValues<Aggregate>().Select(v => v.Name), Is.EquivalentTo(new[] { "count", "sum", "average", "mean" }));
        Assert.That(StaticValues<SortOrder>().Select(v => v.Name), Is.EquivalentTo(new[] { "ascending", "descending" }));
        Assert.That(StaticValues<SeriesCountAxis>().Select(v => v.Name), Is.EquivalentTo(new[] { "x", "y", "auto" }));
        Assert.That(Aggregate.Mean.Name, Is.EqualTo("mean"));
        Assert.That(SeriesCountAxis.Auto.Name, Is.EqualTo("auto"));
    }

    // source: ir/flint-ir.json upstream.version (FlintUpstream.Version is generated, design §4.1)
    [Test]
    public void FlintUpstream_Version_EqualsTheIrVersion()
    {
        Assert.That(FlintUpstream.Version, Is.EqualTo(CommittedIr.Root["upstream"]!["version"]!.GetValue<string>()));
    }

    private static IEnumerable<T> StaticValues<T>() =>
        typeof(T).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(T))
            .Select(f => (T)f.GetValue(null)!);
}
