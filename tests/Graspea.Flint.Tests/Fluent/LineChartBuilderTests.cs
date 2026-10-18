using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Fluent;

public sealed record Sale(DateOnly Month, decimal Revenue, decimal Cost, string Region);

/// <summary>The generated <c>LineChartBuilder&lt;TRow&gt;</c> and the <c>Chart.Line</c> entries (design §4.2).</summary>
public sealed class LineChartBuilderTests
{
    private static readonly Sale[] Sales = [new(new DateOnly(2024, 1, 1), 1200.50m, 800.00m, "EU")];

    // source: design §4.2 usage (the first chain) and its "Output of the first chain" document, and §4.1 (the same
    // document built from the model types)
    [Test]
    public void Chain_EqualsHandBuiltModel()
    {
        var chained = Chart.Line(Sales)
            .X(r => r.Month, SemanticType.YearMonth)
            .Y(r => r.Revenue, SemanticType.Amount)
            .Color(r => r.Region)
            .Interpolate(LineChartInterpolate.Monotone)
            .Title("Revenue by region")
            .BaseSize(800, 400)
            .Theme(ThemePreset.Economist)
            .Build();

        var handBuilt = new ChartAssemblyInput
        {
            Data = ChartData.FromRows(Sales),
            SemanticTypes = new() { ["month"] = SemanticType.YearMonth, ["revenue"] = SemanticType.Amount },
            ChartSpec = new ChartSpec
            {
                ChartType = ChartType.LineChart,
                Title = "Revenue by region",
                Encodings = { ["x"] = "month", ["y"] = "revenue", ["color"] = "region" },
                BaseSize = new ChartSize(800, 400),
                ChartProperties = new() { ["interpolate"] = "monotone" },
            },
            ThemeSpec = ThemePreset.Economist,
        };

        const string expected = """
            {"data":{"values":[{"month":"2024-01-01","revenue":1200.50,"cost":800.00,"region":"EU"}]},
             "semantic_types":{"month":"YearMonth","revenue":"Amount"},
             "chart_spec":{"chartType":"Line Chart","title":"Revenue by region",
               "encodings":{"x":{"field":"month"},"y":{"field":"revenue"},"color":{"field":"region"}},
               "baseSize":{"width":800,"height":400},
               "chartProperties":{"interpolate":"monotone"}},
             "theme_spec":"economist"}
            """;
        JsonAssert.Equal(expected, FlintJson.SerializeToNode(chained));
        JsonAssert.Equal(FlintJson.Serialize(handBuilt), FlintJson.SerializeToNode(chained));
    }

    // source: design §4.2 usage (the second chain: a static series from selectors, an explicit sort order, and
    // Validate(FlintBackend.VegaLite).ThrowIfInvalid() passing)
    [Test]
    public void SeriesChain_ValidatesOnVegaLite()
    {
        var builder = Chart.Line(Sales)
            .X(r => r.Month, SemanticType.YearMonth)
            .Series(Channel.Y, r => r.Revenue, r => r.Cost)
            .Encoding(Channel.X, e => e.SortBy = SortBy.ExplicitOrder("Jan", "Feb"));
        Assert.That((Action)(() => builder.Validate(FlintBackend.VegaLite).ThrowIfInvalid()), Throws.Nothing);
        JsonAssert.Equal(
            """{"x":{"field":"month","sortBy":"[\"Jan\",\"Feb\"]"},"y":[{"field":"revenue"},{"field":"cost"}]}""",
            FlintJson.SerializeToNode(builder.Build())["chart_spec"]!["encodings"]);
    }

    // source: design §4.2 (enum Default = omit the key; a discrete property maps to its value table) and spike (a) change 2
    // (setting a property to its default removes the key)
    [Test]
    public void DiscreteProperty_Default_RemovesTheKey()
    {
        var builder = Chart.Line(Sales).Interpolate(LineChartInterpolate.StepAfter).ShowPoints(true);
        JsonAssert.Equal("""{"interpolate":"step-after","showPoints":true}""", FlintJson.SerializeToNode(builder.Build())["chart_spec"]!["chartProperties"]);
        builder.Interpolate(LineChartInterpolate.Default);
        JsonAssert.Equal("""{"showPoints":true}""", FlintJson.SerializeToNode(builder.Build())["chart_spec"]!["chartProperties"]);
        Assert.That((Action)(() => builder.Interpolate((LineChartInterpolate)999)), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    // source: design §4.2 entry point (Line(ChartData) for pre-built values or {url}; Line(rows, JsonTypeInfo) for
    // source-generated metadata, through which selectors resolve)
    [Test]
    public void Line_DataAndTypeInfoEntries()
    {
        var url = Chart.Line(ChartData.FromUrl("https://example.com/sales.json")).X("month").Y("revenue").Build();
        JsonAssert.Equal(
            """{"data":{"url":"https://example.com/sales.json"},"chart_spec":{"chartType":"Line Chart","encodings":{"x":{"field":"month"},"y":{"field":"revenue"}}}}""",
            FlintJson.SerializeToNode(url));

        var typed = Chart.Line(Sales, SaleContext.Default.Sale).X(r => r.Month).Build();
        Assert.That(typed.ChartSpec.Encodings["x"].Encodings[0].Field, Is.EqualTo("month"));
        Assert.That(typed.Data!.FirstRowKeys, Is.EqualTo(new[] { "month", "revenue", "cost", "region" }));
    }

    // source: spike (a) change 1 (Chart.Line("https://…") compiled as LineChartBuilder<char>; the error-marked Line(string)
    // overload wins overload resolution and turns the call into CS0619). A compile probe needs a compiler, so the test
    // pins the attribute that produces CS0619 (design §10.4 "Chart_Line_StringArgument_DoesNotCompile")
    [Test]
    public void Chart_Line_StringArgument_DoesNotCompile()
    {
        var line = typeof(Chart).GetMethod(nameof(Chart.Line), [typeof(string)]);
        Assert.That(line, Is.Not.Null);
        var obsolete = line!.GetCustomAttribute<ObsoleteAttribute>();
        Assert.That(obsolete, Is.Not.Null);
        Assert.That(obsolete!.IsError, Is.True);
        Assert.That(obsolete.Message, Is.EqualTo("A string is IEnumerable<char>; use Chart.Line(ChartData.FromUrl(url))."));
        Assert.That(line!.ReturnType, Is.EqualTo(typeof(LineChartBuilder<object>)));
    }

    // source: design §4.2 ("a literal null or default second argument is ambiguous between RowOptions? and
    // JsonTypeInfo<TRow> and does not compile (CS0121, spike); documented and accepted"): both overloads take a reference
    // type second, so null converts to either; the documentation says to omit the argument
    [Test]
    public void Line_NullSecondArgument_IsAmbiguousAndDocumented()
    {
        var seconds = typeof(Chart).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(Chart.Line) && m.IsGenericMethodDefinition && m.GetParameters().Length == 2)
            .Select(m => m.GetParameters()[1].ParameterType)
            .Select(t => t.IsGenericType ? t.GetGenericTypeDefinition() : t)
            .ToArray();
        Assert.That(seconds, Is.EquivalentTo(new[] { typeof(RowOptions), typeof(JsonTypeInfo<>) }));
        Assert.That(seconds.All(t => !t.IsValueType), Is.True);

        var generated = File.ReadAllText(Path.Combine(CommittedIr.RepositoryRoot(), "src", "Graspea.Flint", "Generated", "Entries", "Chart.Line.g.cs"));
        Assert.That(generated, Does.Contain("A literal <c>null</c> here does not compile"));
    }
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
[System.Text.Json.Serialization.JsonSerializable(typeof(Sale))]
internal sealed partial class SaleContext : System.Text.Json.Serialization.JsonSerializerContext;
