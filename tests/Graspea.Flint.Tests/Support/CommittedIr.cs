using System.Text.Json.Nodes;

namespace Graspea.Flint.Tests.Support;

/// <summary>The committed IR (generator/ir/flint-ir.json), an admissible source of expected values (design §10.1).</summary>
public static class CommittedIr
{
    private static readonly Lazy<JsonNode> Ir = new(() => JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "generator", "ir", "flint-ir.json")))!);

    public static JsonNode Root => Ir.Value;

    public static JsonNode Catalog => Root["catalog"]!;

    public static string[] Strings(JsonNode? array) => array!.AsArray().Select(n => n!.GetValue<string>()).ToArray();

    private static readonly Lazy<JsonArray> Ledger = new(() => JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "generator", "retired.json")))!.AsArray());

    private static readonly Lazy<(JsonNode Catalog, Dictionary<string, string> Obsolete)> Facade = new(BuildFacade);

    /// <summary>
    /// The catalog the facades are generated from (docs/generator.md, "Retired members"): the IR catalog with every
    /// generator/retired.json entry put back that is still obsolete, that is, retired in the pin's breaking line (design
    /// part 3 §8.4: <c>0.minor</c> before 1.0, <c>major</c> after it) and without <c>deletedIn</c>.
    /// </summary>
    public static JsonNode FacadeCatalog => Facade.Value.Catalog;

    /// <summary>The obsolete ledger paths with the version that retired them.</summary>
    public static IReadOnlyDictionary<string, string> Obsolete => Facade.Value.Obsolete;

    /// <summary>The version that retired the node at an IR path or the template or backend it belongs to, or null.</summary>
    public static string? ObsoleteIn(string chartType, string? backend = null, string? member = null)
    {
        var chain = new List<string> { $"catalog.chartTypes[{chartType}]" };
        if (backend is not null)
        {
            chain.Add($"{chain[0]}.backends.{backend}");
        }

        if (member is not null)
        {
            // An option belongs to its property: properties[k].options[=v] is also retired with properties[k].
            var option = member.IndexOf(".options[", StringComparison.Ordinal);
            if (option >= 0)
            {
                chain.Add($"{chain[1]}.{member[..option]}");
            }

            chain.Add($"{chain[1]}.{member}");
        }

        return chain.Select(p => Obsolete.GetValueOrDefault(p)).FirstOrDefault(v => v is not null);
    }

    /// <summary>The version that retired the member at an IR path, from any generator/retired.json entry (obsolete or removed).</summary>
    public static string? RetiredIn(string path) =>
        Ledger.Value.FirstOrDefault(e => e!["path"]!.GetValue<string>() == path)?["retiredIn"]!.GetValue<string>();

    private static string Name(JsonNode? node, string key) => node![key]!.GetValue<string>();

    /// <summary>Puts a retired node back at its path; false when the IR has it (upstream restored it) or has no parent for it.</summary>
    internal static bool PutBack(JsonNode catalog, string path, JsonNode was)
    {
        var c = System.Text.RegularExpressions.Regex.Match(path, @"^catalog\.(channels|semanticTypes|themePresets)\[([^\]]+)\]$");
        if (c.Success)
        {
            var list = catalog[c.Groups[1].Value]!.AsArray();
            var key = c.Groups[1].Value switch { "channels" => null, "semanticTypes" => "name", _ => "id" };
            if (list.Any(n => (key is null ? n!.GetValue<string>() : Name(n, key)) == c.Groups[2].Value))
            {
                return false;
            }

            list.Add(was.DeepClone());
            return true;
        }

        var m = System.Text.RegularExpressions.Regex.Match(path,
            @"^catalog\.chartTypes\[([^\]]+)\](?:\.backends\.([a-z][a-z-]*)(?:\.channels\[([^\]]+)\]|\.properties\[([^\]]+)\](?:\.options\[(.+)\])?)?)?$");
        if (!m.Success)
        {
            throw new InvalidOperationException($"generator/retired.json: {path} is not a path the facades can keep.");
        }

        var chartTypes = catalog["chartTypes"]!.AsArray();
        var template = chartTypes.SingleOrDefault(t => Name(t, "name") == m.Groups[1].Value);
        if (!m.Groups[2].Success)
        {
            if (template is null)
            {
                chartTypes.Add(was.DeepClone());
            }

            return template is null;
        }

        var backends = template?["backends"]!.AsObject();
        var declared = backends?[m.Groups[2].Value];
        if (!m.Groups[3].Success && !m.Groups[4].Success)
        {
            if (backends is null || declared is not null)
            {
                return false;
            }

            backends[m.Groups[2].Value] = was.DeepClone();
            return true;
        }

        if (declared is null)
        {
            return false;
        }

        if (m.Groups[3].Success)
        {
            var channels = declared["channels"]!.AsArray();
            if (Strings(channels).Contains(m.Groups[3].Value))
            {
                return false;
            }

            channels.Add(was.DeepClone());
            return true;
        }

        var properties = declared["properties"]!.AsArray();
        var property = properties.SingleOrDefault(p => Name(p, "key") == m.Groups[4].Value);
        if (!m.Groups[5].Success)
        {
            if (property is not null)
            {
                return false;
            }

            properties.Add(was.DeepClone());
            return true;
        }

        // An option is keyed by "=" and the JSON text of its value, or "omitted" (generator/src/diff.ts).
        var options = property?["options"]?.AsArray();
        if (options is null || options.Any(o => (o!["omitted"] is not null ? "omitted" : "=" + o["value"]!.ToJsonString()) == m.Groups[5].Value))
        {
            return false;
        }

        options.Add(was.DeepClone());
        return true;
    }

    private static string BreakingLine(string version)
    {
        var parts = version.Split('.');
        return parts[0] == "0" ? $"0.{parts[1]}" : parts[0];
    }

    private static (JsonNode, Dictionary<string, string>) BuildFacade()
    {
        var catalog = Catalog.DeepClone();
        var obsolete = new Dictionary<string, string>(StringComparer.Ordinal);
        var line = BreakingLine(Root["upstream"]!["version"]!.GetValue<string>());
        var entries = Ledger.Value.Select(e => e!)
            .Where(e => e["deletedIn"] is null && BreakingLine(e["retiredIn"]!.GetValue<string>()) == line)
            .OrderBy(e => e["path"]!.GetValue<string>().Length);
        foreach (var e in entries)
        {
            var path = e["path"]!.GetValue<string>();
            if (PutBack(catalog, path, e["was"]!))
            {
                obsolete[path] = e["retiredIn"]!.GetValue<string>();
            }
        }

        return (catalog, obsolete);
    }

    public static string RepositoryRoot()
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
