using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Graspea.Flint.CSharp;
using Graspea.Flint.CSharp.Tests.Support;

namespace Graspea.Flint.CSharp.Tests.Writing;

/// <summary>The hand-written <c>UpdateTarget</c> of flint-chart 1.1.1 in the conversion core: the C# each JSON form is written as,
/// the shape check against the library's converters, and the written C# compiled and serialised back. No generated member
/// holds it before the 1.1.1 sync, so it is named here. D: = the flint-chart 1.1.1 d.ts, dist/types-DFtJpR3x.d.ts.</summary>
public sealed class UpdateTargetValueTests
{
    private const string Target = "Graspea.Flint.UpdateTarget";

    private static string? Write(string json) => new ValueWriter(new SortedSet<string>()).Typed(JsonReader.Parse(json), Target);

    // source: UpdateTarget.Key and UpdateTarget.Where for a selector with nothing else, the object initialisers of UpdateTarget,
    // TargetVisual, SemanticElement, TargetSelector and FilterValue for every other shape; D:12-22, D:43-89, D:487-495
    [TestCase("""{"select":{"key":{"region":"Asia","year":2024}}}""", """UpdateTarget.Key(new() { ["region"] = "Asia", ["year"] = 2024 })""")]
    [TestCase("""{"select":{"key":{"region":"Asia"},"visual":{"kind":"path"}}}""",
        """UpdateTarget.Key(new() { ["region"] = "Asia" }, new TargetVisual { Kind = "path" })""")]
    [TestCase("""{"select":{"key":{}}}""", "UpdateTarget.Key(new())")]
    [TestCase("""{"select":{"where":{"region":{"in":["Asia","Europe"]},"month":{"range":["2025-03","2025-06"]}}}}""",
        """UpdateTarget.Where(new() { ["region"] = new FilterValue { In = ["Asia", "Europe"] }, ["month"] = new FilterValue { Range = ["2025-03", "2025-06"] } })""")]
    [TestCase("""{"select":{"where":{"sales":{"range":[null,100]}},"visual":{"kind":"mark","role":"bar"}}}""",
        """UpdateTarget.Where(new() { ["sales"] = new FilterValue { Range = [null, 100] } }, new TargetVisual { Kind = "mark", Role = "bar" })""")]
    [TestCase("""{"select":{"where":{"region":null}}}""", """UpdateTarget.Where(new() { ["region"] = null! })""")]
    [TestCase("""{"select":{"key":{"a":null,"b":[1,"x"],"c":{"d":true},"e":false}}}""",
        """"UpdateTarget.Key(new() { ["a"] = null, ["b"] = JsonNode.Parse("""[1,"x"]"""), ["c"] = JsonNode.Parse("""{"d":true}"""), ["e"] = false })"""")]
    [TestCase("""{"visual":{"kind":"mark","role":"bar"},"elements":[{"value":{"region":"Asia"},"records":[{"region":"Asia","sales":120}]}]}""",
        """new UpdateTarget { Visual = new TargetVisual { Kind = "mark", Role = "bar" }, Elements = [new SemanticElement { Value = new() { ["region"] = "Asia" }, Records = [new() { ["region"] = "Asia", ["sales"] = 120 }] }] }""")]
    [TestCase("""{"elements":[null,{"records":[null,{}]}]}""", """new UpdateTarget { Elements = [null!, new SemanticElement { Records = [null!, new()] }] }""")]
    [TestCase("{}", "new UpdateTarget()")]
    [TestCase("""{"select":{}}""", "new UpdateTarget { Select = new TargetSelector() }")]
    [TestCase("""{"select":{"key":{"region":"Asia"},"where":{}}}""", """new UpdateTarget { Select = new TargetSelector { Key = new() { ["region"] = "Asia" }, Where = new() } }""")]
    [TestCase("""{"select":{"key":{"region":"Asia"},"limit":1}}""",
        """new UpdateTarget { Select = new TargetSelector { Key = new() { ["region"] = "Asia" }, Extensions = new() { ["limit"] = Json("1") } } }""")]
    [TestCase("""{"select":{"key":{"region":"Asia"}},"priority":1}""",
        """new UpdateTarget { Select = new TargetSelector { Key = new() { ["region"] = "Asia" } }, Extensions = new() { ["priority"] = Json("1") } }""")]
    [TestCase("""{"select":{"key":{"region":"Asia"},"visual":null}}""",
        """new UpdateTarget { Select = new TargetSelector { Key = new() { ["region"] = "Asia" }, Extensions = new() { ["visual"] = Json("null") } } }""")]
    [TestCase("""{"select":{"key":null}}""", """new UpdateTarget { Select = new TargetSelector { Extensions = new() { ["key"] = Json("null") } } }""")]
    [TestCase("""{"select":{"where":{"x":{"in":[null,1],"open":true}}}}""",
        """UpdateTarget.Where(new() { ["x"] = new FilterValue { In = [null, 1], Extensions = new() { ["open"] = Json("true") } } })""")]
    [TestCase("""{"visual":{"kind":"glyph","role":7}}""", """new UpdateTarget { Visual = new TargetVisual { Kind = "glyph", Extensions = new() { ["role"] = Json("7") } } }""")]
    public void Typed_WritesTheForm(string json, string expected)
    {
        Assert.That(Write(json), Is.EqualTo(expected));
    }

    // source: design §12.3 (fidelity rule: a value the type cannot write exactly is left to the caller's fallback)
    [TestCase("\"Asia\"")]
    [TestCase("1")]
    [TestCase("true")]
    [TestCase("[]")]
    [TestCase("""[{"select":{"key":{}}}]""")]
    public void Typed_OtherShapes_AreNotWritten(string json)
    {
        Assert.That(Write(json), Is.Null);
    }

    // source: design §12.3 (an array item that is null is written as null!, as for the other reference types)
    [Test]
    public void Typed_TargetArray_WritesEachItem()
    {
        var written = new ValueWriter(new SortedSet<string>()).Typed(JsonReader.Parse("""[{"select":{"key":{"region":"Asia"}}},null]"""), Target + "[]");
        Assert.That(written, Is.EqualTo("""[UpdateTarget.Key(new() { ["region"] = "Asia" }), null!]"""));
    }

    public static IEnumerable<TestCaseData> Shapes() => new[]
    {
        "{}", "null", "\"Asia\"", "1", "[]", "[{}]",
        """{"visual":{"kind":"mark","role":"bar"},"elements":[{"value":{"region":"Asia"},"records":[{"region":"Asia"}]}]}""",
        """{"visual":null,"elements":null,"select":null}""", """{"visual":{"kind":null,"role":null}}""", """{"visual":{"kind":1}}""",
        """{"visual":{"role":true}}""", """{"visual":"mark"}""", """{"visual":[]}""", """{"elements":{}}""", """{"elements":[null]}""",
        """{"elements":[1]}""", """{"elements":[{"value":null,"records":null}]}""", """{"elements":[{"value":[]}]}""",
        """{"elements":[{"value":{"a":[1,{"b":null}]}}]}""", """{"elements":[{"records":[null,{}]}]}""", """{"elements":[{"records":[[]]}]}""",
        """{"elements":[{"records":{}}]}""", """{"elements":[{"x":1}],"y":2}""",
        """{"select":{"key":{"region":"Asia"}}}""", """{"select":{}}""", """{"select":"Asia"}""", """{"select":[]}""", """{"select":{"key":[]}}""",
        """{"select":{"key":"Asia"}}""", """{"select":{"key":null,"where":null,"visual":null}}""", """{"select":{"visual":{"kind":2}}}""",
        """{"select":{"visual":"mark"}}""", """{"select":{"where":{"r":{"in":["Asia"]},"m":{"range":[1,2]}}}}""", """{"select":{"where":[]}}""",
        """{"select":{"where":{"r":null}}}""", """{"select":{"where":{"r":["Asia"]}}}""", """{"select":{"where":{"r":"Asia"}}}""",
        """{"select":{"where":{"r":{"in":"Asia"}}}}""", """{"select":{"where":{"r":{"in":null,"range":null}}}}""",
        """{"select":{"where":{"r":{"range":{}}}}}""", """{"select":{"where":{"r":{"range":[1,2,3],"in":[[],{}]}}}}""",
        """{"select":{"where":{"r":{}},"x":1},"y":[]}""",
    }.Select(json => new TestCaseData(json).SetName($"Shape_AgreesWithTheLibrary({json})"));

    // source: the library's converters (JsonSerializer.Deserialize of UpdateTarget) decide each shape
    [TestCaseSource(nameof(Shapes))]
    public void Shape_AgreesWithTheLibrary(string json)
    {
        Assert.That(ShapeRejects(json, Target), Is.EqualTo(Rejects(() => JsonSerializer.Deserialize<UpdateTarget>(json))));
    }

    private static readonly string[] Written =
    [
        """{"select":{"key":{"region":"Asia","year":2024,"rank":12345678901,"share":0.25}}}""",
        """{"select":{"key":{"region":"Asia"},"visual":{"kind":"path","role":"line"}}}""",
        """{"select":{"key":{"a":null,"b":[1,"x"],"c":{"d":true},"e":false}}}""",
        """{"select":{"where":{"region":{"in":["Asia","Europe"]},"month":{"range":["2025-03","2025-06"]}},"visual":{"role":"bar"}}}""",
        """{"select":{"where":{"sales":{"range":[null,100]},"region":null,"x":{"in":[null,[1],{"k":"v"}],"open":true}}}}""",
        """{"visual":{"kind":"mark","role":"bar"},"elements":[{"value":{"region":"Asia"},"records":[{"region":"Asia","sales":120},null]},null]}""",
        """{"visual":{"kind":"legend","role":"color","z":1},"elements":[{"value":{"domain":{"kind":"value","value":"Asia"}},"rank":2}],"note":"n"}""",
        """{"select":{"key":{"region":"Asia"},"where":{},"visual":null,"limit":1},"visual":{"kind":"mark"},"elements":[],"priority":1}""",
        """{"select":{}}""",
        "{}",
    ];

    // source: design §12.3 (fidelity rule) and §12.10 (compile round-trip): the C# written for each target compiles without
    // warnings (C# 12, net8.0 reference pack) and the library serialises the value it builds as the input JSON
    [Test]
    public void Written_CompilesAndSerialisesAsTheInput()
    {
        var usings = new SortedSet<string>(StringComparer.Ordinal);
        var values = new ValueWriter(usings);
        var methods = Written.Select((json, i) => $"    public static Graspea.Flint.UpdateTarget? V{i}() => {values.Typed(JsonReader.Parse(json), Target)};\n").ToList();
        var source = string.Concat(usings.Prepend("System.Text.Json").Distinct().Select(u => $"using {u};\n")) +
            "public static class Targets\n{\n" + string.Concat(methods) +
            "    private static JsonElement Json(string json)\n    {\n        using var document = JsonDocument.Parse(json);\n        return document.RootElement.Clone();\n    }\n}\n";

        var compilation = Net8Compiler.Compile("UpdateTargets", [("Targets.cs", source)]);
        Assert.That(Net8Compiler.Problems(compilation).Select(p => p.ToString()), Is.Empty, source);
        var (assembly, context) = Net8Compiler.Load(compilation);
        try
        {
            var type = assembly.GetType("Targets")!;
            for (var i = 0; i < Written.Length; i++)
            {
                var value = type.GetMethod($"V{i}", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
                var json = JsonSerializer.Serialize(value, typeof(UpdateTarget));
                Assert.That(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(Written[i])), Is.True, $"{Written[i]}\nwritten as {json}");
            }
        }
        finally
        {
            context.Unload();
        }
    }

    private static bool Rejects(Action read)
    {
        try
        {
            read();
            return false;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static bool ShapeRejects(string json, string type)
    {
        try
        {
            SpecShape.Typed(JsonReader.Parse(json), type, "x");
            return false;
        }
        catch (Exception e) when (e is not InvalidOperationException)
        {
            return true;
        }
    }
}
