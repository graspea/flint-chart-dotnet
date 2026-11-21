using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Support;
using Graspea.Flint.Validation.Rules;

namespace Graspea.Flint.Tests.Parity;

/// <summary>
/// The table values the Excel rules read (<c>FieldTypes.TableValues</c>, which decides the temporal key and converts it
/// through <c>TemporalParse.Converted</c>; compiled in from src/Graspea.Flint/Validation/Rules/), against what
/// <c>convertTemporalData</c> of the pinned flint-chart wrote for the generator's grid of values and semantic types
/// (tests/fixtures/flint-chart@&lt;pin&gt;/probes.json <c>temporalConversion</c>; generator/src/probes.ts): the first row of
/// <c>[{ f: value }, { f: companion }]</c> under <c>{ f: type }</c>, <c>untyped</c> for no semantic type. A cell whose
/// answer needs V8's <c>Date.parse</c> may be undecided, and only one listed in <see cref="UndecidedHere"/>.
/// </summary>
public sealed class TemporalConversionTests
{
    private static JsonNode Record => CommittedIr.Probes["temporalConversion"]!;

    /// <summary>The cells the validator leaves undecided, (value, type) with the reason.</summary>
    private static readonly Dictionary<(string Value, string Type), string> UndecidedHere = new()
    {
    };

    public static IEnumerable<TestCaseData> Cells() =>
        from value in Record["converted"]!.AsObject()
        from type in value.Value!.AsObject()
        select new TestCaseData(value.Key, type.Key).SetName($"ProbedConversion_MatchesTheValidator({Shown(value.Key)}, {type.Key})");

    /// <summary>The value as a JSON string, its control characters escaped, in single quotes.</summary>
    private static string Shown(string value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).Replace('"', '\'');

    private static (JsonNode? Value, bool Present)[]? Table(string value, string type)
    {
        var doc = new JsonObject
        {
            ["data"] = new JsonObject { ["values"] = new JsonArray(new JsonObject { ["f"] = value }, new JsonObject { ["f"] = Record["companion"]!.DeepClone() }) },
            ["chart_spec"] = new JsonObject { ["chartType"] = "Bar Chart", ["encodings"] = new JsonObject() },
        };
        if (type != "untyped")
        {
            doc["semantic_types"] = new JsonObject { ["f"] = type };
        }

        return new FieldTypes(FlintJson.Deserialize(doc.ToJsonString())).TableValues("f");
    }

    // source: probes.json temporalConversion.converted[value][type] (convertTemporalData of the pinned flint-chart,
    // src/core/resolve-semantics.ts)
    [TestCaseSource(nameof(Cells))]
    public void ProbedConversion_MatchesTheValidator(string value, string type)
    {
        var expected = Record["converted"]![value]![type];
        var table = Table(value, type);
        if (table is null && UndecidedHere.ContainsKey((value, type)))
        {
            return;
        }

        Assert.That(table, Is.Not.Null, "undecided");
        Assert.That(table![0].Present, Is.True);
        Assert.That(JsonNode.DeepEquals(table[0].Value, expected), Is.True, () => $"upstream writes {expected?.ToJsonString()}, the validator reads {table[0].Value?.ToJsonString()}");
    }

    // source: UndecidedHere (each entry is still undecided, so the list names no cell the validator now decides)
    [Test]
    public void UndecidedCells_AreStillUndecided()
    {
        using (Assert.EnterMultipleScope())
        {
            foreach (var (value, type) in UndecidedHere.Keys)
            {
                Assert.That(Record["converted"]![value]?[type], Is.Not.Null, $"{value} as {type} is probed");
                Assert.That(Table(value, type), Is.Null, $"{value} as {type}");
            }
        }
    }
}
