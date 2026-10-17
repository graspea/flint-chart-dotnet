using System.Text.Json.Nodes;

namespace Graspea.Flint.Tests.Support;

/// <summary>The committed IR (generator/ir/flint-ir.json), an admissible source of expected values (design §10.1).</summary>
public static class CommittedIr
{
    private static readonly Lazy<JsonNode> Ir = new(() => JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "generator", "ir", "flint-ir.json")))!);

    public static JsonNode Root => Ir.Value;

    public static JsonNode Catalog => Root["catalog"]!;

    public static string[] Strings(JsonNode? array) => array!.AsArray().Select(n => n!.GetValue<string>()).ToArray();

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
