using System.Text.Json;
using System.Text.Json.Nodes;
using Graspea.Flint.ContractTests.Support;
using Graspea.Flint.Serialization;

namespace Graspea.Flint.CSharp.Tests.Support;

/// <summary>A document of the round-trip corpus: an id and its JSON text.</summary>
public sealed record SpecCase(string Id, string Json);

/// <summary>
/// The documents the compile round-trip converts (design §12.10): every fixture and probe case of the pinned flint-chart,
/// every sample output (<c>samples/out/*.json</c>, written by running the samples) and the README quick-start JSON and the crafted
/// edge cases, each kept only when the tool's double read accepts it (the core reader and <c>FlintJson.Deserialize</c>).
/// </summary>
public static class SpecCorpus
{
    private static readonly Lazy<(IReadOnlyList<SpecCase> Accepted, IReadOnlyList<string> Rejected)> Corpus = new(Read);

    public static IReadOnlyList<SpecCase> Accepted => Corpus.Value.Accepted;

    /// <summary>The ids of the documents the double read refuses, with the reason.</summary>
    public static IReadOnlyList<string> Rejected => Corpus.Value.Rejected;

    private static (IReadOnlyList<SpecCase>, IReadOnlyList<string>) Read()
    {
        var all = new List<SpecCase>();
        var version = FlintUpstream.Version;
        all.AddRange(Fixtures.Cases(version).Values.Select(c => new SpecCase(c.Id, c.InputJson)));
        all.AddRange(Fixtures.ProbeCases(version).Values.Select(c => new SpecCase($"{c.Id} {c.Title}", c.InputJson)));
        var samples = Repository.Path("samples", "out");
        if (Directory.Exists(samples))
        {
            all.AddRange(Directory.GetFiles(samples, "*.json").Order(StringComparer.Ordinal).Select(f => new SpecCase($"sample {Path.GetFileName(f)}", File.ReadAllText(f))));
        }

        all.Add(new SpecCase("README quick start", ReadmeQuickStart()));
        all.AddRange(EdgeCases());

        var accepted = new List<SpecCase>();
        var rejected = new List<string>();
        foreach (var c in all)
        {
            try
            {
                JsonReader.Parse(c.Json);
                FlintJson.Deserialize(c.Json);
                accepted.Add(c);
            }
            catch (Exception e) when (e is JsonException or JsonReadException)
            {
                rejected.Add($"{c.Id}: {e.Message}");
            }
        }

        return (accepted, rejected);
    }

    /// <summary>
    /// The crafted documents of <c>Contract/edge-cases.jsonl</c>: one per mapping row of design §12.4 that the fixtures do
    /// not reach (nulls and unknown keys at every level, channels and templates outside the catalog, series forms, every
    /// property kind, row values of every kind, characters that need escapes). The input is taken as raw text, so numbers
    /// keep their spelling.
    /// </summary>
    public static IEnumerable<SpecCase> EdgeCases() =>
        File.ReadLines(Repository.Path("tests", "Graspea.Flint.CSharp.Tests", "Contract", "edge-cases.jsonl"))
            .Where(line => line.Length > 0)
            .Select(line =>
            {
                var item = JsonReader.Parse(line);
                var input = item.Get("input")!;
                return new SpecCase($"edge {item.Get("id")!.Value}", line[input.Start.Offset..input.End]);
            });

    /// <summary>The ```json block after "## Quick start" in README.md, as tests/contract-node/readme.test.js reads it.</summary>
    public static string ReadmeQuickStart()
    {
        var readme = File.ReadAllText(Repository.Path("README.md")).Replace("\r\n", "\n");
        var quickStart = readme[readme.IndexOf("\n## Quick start\n", StringComparison.Ordinal)..];
        var start = quickStart.IndexOf("```json\n", StringComparison.Ordinal) + "```json\n".Length;
        return quickStart[start..quickStart.IndexOf("\n```", start, StringComparison.Ordinal)];
    }

    /// <summary>The canonical form of a document (the contract canonicaliser, design §5.2).</summary>
    public static JsonNode? Canonicalise(string json) => Canonical.Of(JsonNode.Parse(json));
}
