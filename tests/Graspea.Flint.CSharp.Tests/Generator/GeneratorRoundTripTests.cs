using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Graspea.Flint.CSharp.Tests.Support;
using Graspea.Flint.Serialization;
using Microsoft.CodeAnalysis.CSharp;

namespace Graspea.Flint.CSharp.Tests.Generator;

/// <summary>
/// The generator's fidelity (design §12.10, generator driver): for every document of the corpus, mode A output compiles
/// (C# 12, net8.0 reference pack), and <c>Build</c> over rows read from <c>data.values</c> into the generated <c>Row</c>
/// writes a document that is <c>DeepEquals</c> to the spec with the rows bound: each union-only field as null at the end of
/// every row, the key order of every row kept. <c>WriteAsync</c> writes the same bytes as <c>FlintJson.Serialize</c> of
/// <c>Build</c>'s document.
/// </summary>
[Category("Contract")]
public sealed class GeneratorRoundTripTests
{
    // source: design §12.10 (generator driver: invoking Build on rows from data.values is DeepEquals to the canonicalised
    // spec with union-only fields as null, for every fixture) and §12.6.3 (the data in the spec is never embedded, so the
    // expected data is the bound rows alone); the documents are the corpus of the compile round-trip
    [Test]
    public async Task Build_EveryCorpusDocument_EqualsTheSpecWithTheRowsBound()
    {
        var cases = SpecCorpus.Accepted;
        Assert.That(cases, Has.Count.GreaterThan(900));
        var sources = cases.Select((_, i) => $"namespace Cases;\n[Graspea.Flint.FlintChart(\"cases/Case{i:D4}.flint.json\")]\npublic static partial class Case{i:D4};\n");
        var files = cases.Select((c, i) => ($"cases/Case{i:D4}.flint.json", c.Json));
        var run = GeneratorHarness.Run(sources, files, tracking: false);

        Assert.That(run.Problems().Take(20).Select(p => p.ToString()), Is.Empty);
        var (assembly, context) = Net8Compiler.Load((CSharpCompilation)run.Output);
        var failures = new List<string>();
        var skipped = 0;
        try
        {
            for (var i = 0; i < cases.Count; i++)
            {
                var failure = await Check(assembly.GetType($"Cases.Case{i:D4}")!, cases[i]);
                if (failure == Skipped)
                {
                    skipped++;
                }
                else if (failure is not null)
                {
                    failures.Add($"{cases[i].Id}: {failure}");
                }
            }
        }
        finally
        {
            context.Unload();
        }

        Assert.That(failures, Is.Empty, $"{failures.Count} of {cases.Count} documents fail:\n{string.Join("\n", failures.Take(30))}");
        Assert.That(cases.Count - skipped, Is.GreaterThan(900), "documents checked");
    }

    /// <summary>A document whose rows are not all objects: rows of the inferred Row cannot hold them.</summary>
    private const string Skipped = "skipped";

    private static async Task<string?> Check(Type type, SpecCase c)
    {
        var input = JsonNode.Parse(c.Json)!.AsObject();
        var values = input["data"] is JsonObject data && data["values"] is JsonArray array ? array : new JsonArray();
        if (values.Any(r => r is not JsonObject))
        {
            return Skipped;
        }

        var rowType = type.GetNestedType("Row")!;
        var rows = JsonSerializer.Deserialize(values.ToJsonString(), rowType.MakeArrayType())!;
        var fields = rowType.GetProperties().Select(p => p.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>()!.Name).ToList();
        var spec = FlintJson.Deserialize(c.Json);
        spec.Data = null;
        var expected = FlintJson.SerializeToNode(spec).AsObject();
        expected["data"] = new JsonObject
        {
            ["values"] = new JsonArray(values.Select(r => (JsonNode?)new JsonObject(fields.Select(f => KeyValuePair.Create(f, ((JsonObject)r!)[f]?.DeepClone())))).ToArray()),
        };

        ChartAssemblyInput built;
        try
        {
            built = (ChartAssemblyInput)type.GetMethod("Build")!.Invoke(null, [rows])!;
        }
        catch (TargetInvocationException e)
        {
            return $"Build throws {e.InnerException!.GetType().Name}: {e.InnerException.Message}";
        }

        var actual = FlintJson.SerializeToNode(built);
        if (!JsonNode.DeepEquals(SpecCorpus.Canonicalise(expected.ToJsonString()), SpecCorpus.Canonicalise(actual.ToJsonString())))
        {
            return $"differs\n  expected: {Shorten(expected.ToJsonString())}\n  actual:   {Shorten(actual.ToJsonString())}";
        }

        var actualRows = actual["data"]!["values"]!.AsArray();
        if (actualRows.Any(r => !((JsonObject)r!).Select(p => p.Key).SequenceEqual(fields)))
        {
            return $"row key order differs from {string.Join(",", fields)}";
        }

        using var stream = new MemoryStream();
        var source = typeof(GeneratorRoundTripTests).GetMethod(nameof(Async), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(rowType).Invoke(null, [rows]);
        await (Task)type.GetMethod("WriteAsync")!.Invoke(null, [stream, source, CancellationToken.None])!;
        var streamed = Encoding.UTF8.GetString(stream.ToArray());
        return streamed == FlintJson.Serialize(built) ? null : $"WriteAsync writes other bytes than Build\n  {Shorten(streamed)}";
    }

    private static async IAsyncEnumerable<T> Async<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    private static string Shorten(string text) => text.Length <= 500 ? text : text[..500] + "…";
}
