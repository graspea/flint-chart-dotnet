using System.Text.Json;
using System.Text.Json.Nodes;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Serialization;

public sealed class ExtensionsTests
{
    // A document from a newer upstream: unknown keys at every level, already in canonical order (known keys first).
    private const string Canonical =
        """{"data":{"values":[{"a":1}],"format":"json"},"semantic_types":{"a":{"semanticType":"Amount","currency":"EUR"}},"chart_spec":{"chartType":"Bar Chart","encodings":{"x":{"field":"a","bin":true}},"interactions":["hover"]},"theme_spec":{"extends":"nyt","motion":{"ease":"linear"}},"options":{"addTooltips":true,"background":"#fff"},"interaction_spec":{"hover":true}}""";

    // source: design §5.2 (forward compatibility: a document from a newer upstream round-trips byte-identical through
    // FlintJsonContext and through reflection) and design §10.4 test 13
    [Test]
    public void Deserialize_UnknownKeys_RoundTripAtEveryLevel()
    {
        Assert.That(FlintJson.Serialize(FlintJson.Deserialize(Canonical)), Is.EqualTo(Canonical));
        Assert.That(JsonSerializer.Serialize(JsonSerializer.Deserialize<ChartAssemblyInput>(Canonical)), Is.EqualTo(Canonical));
        Assert.That(JsonSerializer.Serialize(JsonSerializer.Deserialize<ChartAssemblyInput>(Canonical, HostileOptions.Create()), HostileOptions.Create()), Is.EqualTo(Canonical));
    }

    // source: design §5.2 (extensions are written last)
    [Test]
    public void Deserialize_UnknownKeysFirst_AreWrittenAfterTheKnownKeys()
    {
        const string json =
            """{"interaction_spec":{"hover":true},"chart_spec":{"interactions":["hover"],"encodings":{"x":{"bin":true,"field":"a"}},"chartType":"Bar Chart"},"data":{"format":"json","values":[{"a":1}]}}""";
        Assert.That(
            FlintJson.Serialize(FlintJson.Deserialize(json)),
            Is.EqualTo("""{"data":{"values":[{"a":1}],"format":"json"},"chart_spec":{"chartType":"Bar Chart","encodings":{"x":{"field":"a","bin":true}},"interactions":["hover"]},"interaction_spec":{"hover":true}}"""));
    }

    // source: design §5.2 (an empty extension map writes nothing; a trivial annotation stays the string form)
    [Test]
    public void Serialize_EmptyExtensions_WriteNothing()
    {
        var input = new ChartAssemblyInput
        {
            Data = ChartData.FromJson([]),
            SemanticTypes = new() { ["a"] = new SemanticAnnotation { Type = SemanticType.Amount, Extensions = [] } },
            ChartSpec = new ChartSpec { ChartType = ChartType.BarChart, Extensions = [], Encodings = { ["x"] = new ChartEncoding { Field = "a", Extensions = [] } } },
            Options = new AssembleOptions { Extensions = [] },
            Extensions = [],
        };
        input.Data.Extensions = [];
        JsonAssert.Equal(
            """{"data":{"values":[]},"semantic_types":{"a":"Amount"},"chart_spec":{"chartType":"Bar Chart","encodings":{"x":{"field":"a"}}},"options":{}}""",
            FlintJson.Serialize(input));
    }

    // source: design §4.1 (Extensions is a Dictionary<string, JsonElement>, settable from code)
    [Test]
    public void Serialize_ExtensionsSetInCode_AreWrittenLast()
    {
        var input = new ChartAssemblyInput
        {
            Data = ChartData.FromJson([]),
            ChartSpec = new ChartSpec { ChartType = ChartType.BarChart },
            Extensions = new() { ["interaction_spec"] = JsonSerializer.SerializeToElement(new JsonObject { ["hover"] = true }) },
        };
        Assert.That(FlintJson.Serialize(input), Is.EqualTo("""{"data":{"values":[]},"chart_spec":{"chartType":"Bar Chart","encodings":{}},"interaction_spec":{"hover":true}}"""));
    }
}
