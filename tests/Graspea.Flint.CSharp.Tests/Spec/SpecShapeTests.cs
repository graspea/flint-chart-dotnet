using System.Text.Json;
using Graspea.Flint.CSharp;
using Graspea.Flint.CSharp.Tests.Support;
using Graspea.Flint.Serialization;

namespace Graspea.Flint.CSharp.Tests.Spec;

/// <summary>
/// FLINT103 (design §12.6.7): the JSON is not a <c>ChartAssemblyInput</c>, exactly where <c>FlintJson.Deserialize</c>
/// throws, so the generator refuses what the library refuses and no more.
/// </summary>
public sealed class SpecShapeTests
{
    private const string Valid = """{"data":{"values":[{"a":1}]},"chart_spec":{"chartType":"Bar Chart","encodings":{"x":"a"}}}""";

    private static bool LibraryRejects(string json)
    {
        try
        {
            FlintJson.Deserialize(json);
            return false;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    public static IEnumerable<TestCaseData> Shapes() => new[]
    {
        ("{}", true),
        ("{\"chart_spec\":null}", false),
        ("{\"chart_spec\":[]}", true),
        ("{\"chart_spec\":{}}", true),
        ("{\"chart_spec\":{\"chartType\":\"\"}}", true),
        ("{\"chart_spec\":{\"chartType\":1}}", true),
        ("{\"chart_spec\":{\"chartType\":null}}", true),
        ("{\"chart_spec\":{\"chartType\":\"X\"}}", false),
        ("{\"chart_spec\":{\"chartType\":\"X\",\"encodings\":[]}}", true),
        ("{\"chart_spec\":{\"chartType\":\"X\",\"encodings\":\"x\"}}", true),
        ("{\"chart_spec\":{\"chartType\":\"X\",\"encodings\":{\"x\":1}}}", true),
        ("{\"chart_spec\":{\"chartType\":\"X\",\"encodings\":{\"x\":[1]}}}", true),
        ("{\"chart_spec\":{\"chartType\":\"X\",\"encodings\":{\"x\":[null]}}}", true),
        ("{\"chart_spec\":{\"chartType\":\"X\",\"encodings\":{\"x\":null}}}", false),
        ("{\"chart_spec\":{\"chartType\":\"X\",\"encodings\":{\"x\":{\"field\":1}}}}", true),
        ("{\"chart_spec\":{\"chartType\":\"X\",\"encodings\":{\"x\":{\"type\":\"\"}}}}", true),
        ("{\"chart_spec\":{\"chartType\":\"X\",\"encodings\":{\"x\":{\"type\":null,\"aggregate\":null}}}}", false),
        ("{\"chart_spec\":{\"chartType\":\"X\",\"encodings\":{\"x\":{\"sortBy\":[1]}}}}", true),
        ("{\"chart_spec\":{\"chartType\":\"X\",\"encodings\":{\"x\":{\"anything\":[1]}}}}", false),
        ("{\"chart_spec\":{\"chartType\":\"X\",\"title\":1}}", true),
        ("{\"chart_spec\":{\"chartType\":\"X\",\"subtitle\":null}}", false),
        ("{\"chart_spec\":{\"chartType\":\"X\",\"baseSize\":\"a\"}}", true),
        ("{\"chart_spec\":{\"chartType\":\"X\",\"baseSize\":{\"width\":\"1\"}}}", true),
        ("{\"chart_spec\":{\"chartType\":\"X\",\"baseSize\":{\"width\":null}}}", true),
        ("{\"chart_spec\":{\"chartType\":\"X\",\"baseSize\":{\"width\":1,\"depth\":[]}}}", false),
        ("{\"chart_spec\":{\"chartType\":\"X\",\"canvasSize\":{\"width\":1e400,\"height\":1}}}", false),
        ("{\"chart_spec\":{\"chartType\":\"X\",\"chartProperties\":[]}}", true),
        ("{\"chart_spec\":{\"chartType\":\"X\",\"chartProperties\":{\"a\":{\"b\":[null]}}}}", false),
        ("{\"data\":[],\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"data\":null,\"chart_spec\":{\"chartType\":\"X\"}}", false),
        ("{\"data\":{\"values\":{}},\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"data\":{\"values\":[1,null,\"a\"]},\"chart_spec\":{\"chartType\":\"X\"}}", false),
        ("{\"data\":{\"url\":1},\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"semantic_types\":[],\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"semantic_types\":{\"a\":1},\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"semantic_types\":{\"a\":null},\"chart_spec\":{\"chartType\":\"X\"}}", false),
        ("{\"semantic_types\":{\"a\":\"\"},\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"semantic_types\":{\"a\":{}},\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"semantic_types\":{\"a\":{\"semanticType\":null}},\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"semantic_types\":{\"a\":{\"semanticType\":\"T\",\"unit\":1}},\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"semantic_types\":{\"a\":{\"semanticType\":\"T\",\"intrinsicDomain\":[null]}},\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"semantic_types\":{\"a\":{\"semanticType\":\"T\",\"sortOrder\":[null,\"b\"]}},\"chart_spec\":{\"chartType\":\"X\"}}", false),
        ("{\"theme_spec\":1,\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"theme_spec\":\"\",\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"theme_spec\":[],\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"theme_spec\":{\"extends\":1},\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"theme_spec\":{\"ink\":{\"text\":{\"primary\":1}}},\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"theme_spec\":{\"ink\":{\"unknown\":1}},\"chart_spec\":{\"chartType\":\"X\"}}", false),
        ("{\"options\":[],\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"options\":{\"maxStretch\":\"1\"},\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"options\":{\"addTooltips\":1},\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"options\":{\"continuousMarkCrossSection\":\"a\"},\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"options\":{\"continuousMarkCrossSection\":{\"x\":\"1\"}},\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"options\":{\"continuousMarkCrossSection\":{\"seriesCountAxis\":\"\"}},\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"options\":{\"continuousMarkCrossSection\":{\"x\":1,\"other\":[]}},\"chart_spec\":{\"chartType\":\"X\"}}", false),
        ("{\"field_display_names\":{\"a\":1},\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"field_display_names\":{\"a\":null},\"chart_spec\":{\"chartType\":\"X\"}}", false),
        ("{\"field_display_names\":[],\"chart_spec\":{\"chartType\":\"X\"}}", true),
        ("{\"interaction_spec\":[1,{\"a\":null}],\"chart_spec\":{\"chartType\":\"X\"}}", false),
        ("[]", true),
        ("null", true),
    }.Select(c => new TestCaseData(c.Item1, c.Item2).SetName($"Check_AgreesWithTheLibrary({c.Item1})"));

    // source: FlintJson.Deserialize (System.Text.Json over the typed model: required chart_spec and chartType, string-backed
    // values non-empty strings, numbers strict, unknown keys into Extensions) for each crafted shape; the second argument
    // pins the library's verdict, so a change in the library fails here first
    [TestCaseSource(nameof(Shapes))]
    public void Check_AgreesWithTheLibrary(string json, bool rejected)
    {
        Assert.That(LibraryRejects(json), Is.EqualTo(rejected), "the library's verdict");
        var issue = SpecShape.Check(JsonReader.Parse(json));
        Assert.That(issue is not null, Is.EqualTo(rejected), issue?.Message);
        if (issue is not null)
        {
            Assert.That(issue.Id, Is.EqualTo("FLINT103"));
            Assert.That(issue.Severity, Is.EqualTo(SpecSeverity.Error));
        }
    }

    // source: FlintJson.Deserialize; the shape check names the offending node
    [Test]
    public void Check_WrongKind_PointsAtTheNode()
    {
        var issue = SpecShape.Check(JsonReader.Parse("{\"chart_spec\":{\"chartType\":\"X\",\"encodings\":{\"x\":1}}}"))!;

        Assert.That(issue.Path, Is.EqualTo("chart_spec.encodings.x"));
        Assert.That(issue.Start.Column, Is.EqualTo(49));
        Assert.That(issue.Message, Does.Contain("string, an object or an array"));
    }

    private static readonly string[] Replacements = ["null", "1", "1.5", "\"s\"", "\"\"", "true", "[]", "{}", "[\"s\"]", "[1]"];

    // source: FlintJson.Deserialize, over every node outside data.values of the README quick start and every tenth corpus
    // document, each replaced by every value of Replacements: the shape check rejects exactly what the library rejects
    [Test]
    [Category("Contract")]
    public void Check_EveryMutation_AgreesWithTheLibrary()
    {
        var documents = SpecCorpus.Accepted.Where((_, i) => i % 10 == 0).Select(c => c.Json).Prepend(SpecCorpus.ReadmeQuickStart()).Prepend(Valid);
        var failures = new List<string>();
        var count = 0;
        foreach (var json in documents)
        {
            foreach (var node in Nodes(JsonReader.Parse(json), string.Empty))
            {
                foreach (var replacement in Replacements)
                {
                    var mutated = json[..node.Start.Offset] + replacement + json[node.End..];
                    count++;
                    var expected = LibraryRejects(mutated);
                    var actual = SpecShape.Check(JsonReader.Parse(mutated)) is not null;
                    if (expected != actual)
                    {
                        failures.Add($"{(expected ? "missed" : "false alarm")}: {mutated}");
                    }
                }
            }
        }

        Assert.That(count, Is.GreaterThan(10_000));
        Assert.That(failures, Is.Empty, string.Join("\n", failures.Take(15)));
    }

    /// <summary>Every value of the document except the rows under <c>data.values</c>, which hold any JSON.</summary>
    private static IEnumerable<JsonItem> Nodes(JsonItem item, string path)
    {
        yield return item;
        if (path == "data.values")
        {
            yield break;
        }

        foreach (var member in item.Members)
        {
            foreach (var node in Nodes(member.Value, path.Length == 0 ? member.Key : $"{path}.{member.Key}"))
            {
                yield return node;
            }
        }

        foreach (var element in item.Items)
        {
            foreach (var node in Nodes(element, path + "[]"))
            {
                yield return node;
            }
        }
    }
}
