using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace Graspea.Flint.ContractTests.Support;

/// <summary>One line of cases.jsonl or probe-cases.jsonl (design §10.3 fixture formats).</summary>
public sealed record FixtureCase(string Id, string Title, string ChartType, string InputJson)
{
    /// <summary>The generator group: the id without its <c>|#index</c> suffix.</summary>
    public string Group => Id[..Id.LastIndexOf("|#", StringComparison.Ordinal)];

    /// <summary>The test name of design §10.1 rule 5: <c>&lt;group&gt;|&lt;title&gt;</c>.</summary>
    public string Name => $"{Group}|{Title}";
}

/// <summary>The stored fixture sets, tests/fixtures/flint-chart@&lt;version&gt;/ (design §10.2), read once per run.</summary>
public static class Fixtures
{
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, FixtureCase>> CaseCache = new();
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, JsonNode>> ExpectationCache = new();
    private static readonly Lazy<string> Root = new(RepositoryRoot);

    /// <summary>Every stored set, as its upstream version.</summary>
    public static IEnumerable<string> Versions() =>
        Directory.GetDirectories(Path.Combine(Root.Value, "tests", "fixtures"), "flint-chart@*")
            .Select(d => Path.GetFileName(d)["flint-chart@".Length..])
            .Order(StringComparer.Ordinal);

    /// <summary>The cases mapped from upstream's TEST_GENERATORS (cases.jsonl).</summary>
    public static IReadOnlyDictionary<string, FixtureCase> Cases(string version) => ReadCases(version, "cases.jsonl");

    /// <summary>The recorded outcomes of <see cref="Cases"/> (expectations.jsonl).</summary>
    public static IReadOnlyDictionary<string, JsonNode> Expectations(string version) => ReadExpectations(version, "expectations.jsonl");

    /// <summary>The crafted cases, one per validator rule (probe-cases.jsonl).</summary>
    public static IReadOnlyDictionary<string, FixtureCase> ProbeCases(string version) => ReadCases(version, "probe-cases.jsonl");

    /// <summary>The recorded outcomes of <see cref="ProbeCases"/> (probe-expectations.jsonl).</summary>
    public static IReadOnlyDictionary<string, JsonNode> ProbeExpectations(string version) => ReadExpectations(version, "probe-expectations.jsonl");

    /// <summary>manifest.json of the set.</summary>
    public static JsonNode Manifest(string version) => JsonNode.Parse(File.ReadAllText(Path.Combine(Folder(version), "manifest.json")))!;

    private static IReadOnlyDictionary<string, FixtureCase> ReadCases(string version, string file) => CaseCache.GetOrAdd($"{version}/{file}", _ =>
        File.ReadLines(Path.Combine(Folder(version), file))
            .Select(line => JsonNode.Parse(line)!)
            .Select(n => new FixtureCase(n["id"]!.GetValue<string>(), n["title"]!.GetValue<string>(), n["chartType"]!.GetValue<string>(), n["input"]!.ToJsonString()))
            .ToDictionary(c => c.Id, StringComparer.Ordinal));

    private static IReadOnlyDictionary<string, JsonNode> ReadExpectations(string version, string file) => ExpectationCache.GetOrAdd($"{version}/{file}", _ =>
        File.ReadLines(Path.Combine(Folder(version), file))
            .Select(line => JsonNode.Parse(line)!)
            .ToDictionary(n => n["id"]!.GetValue<string>(), n => n["backends"]!, StringComparer.Ordinal));

    /// <summary>The ids in known-failures.txt: one per line; blank lines and lines starting with <c>#</c> are ignored.</summary>
    public static IReadOnlySet<string> KnownFailures(string version) =>
        File.ReadLines(Path.Combine(Folder(version), "known-failures.txt"))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);

    private static string Folder(string version) => Path.Combine(Root.Value, "tests", "fixtures", $"flint-chart@{version}");

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Graspea.Flint.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Cannot find the repository root (Graspea.Flint.slnx).");
    }
}
