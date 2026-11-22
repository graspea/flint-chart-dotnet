using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Graspea.Flint.CSharp;
using Graspea.Flint.CSharp.Tests.Support;

namespace Graspea.Flint.CSharp.Tests.Writing;

/// <summary>The generated discriminated unions of flint-chart 1.1.1 (<c>ChartUpdateOp</c>, <c>FreeformOverlayBody</c>) and the
/// <c>ChartUpdate</c> that holds them, in the conversion core: the C# each JSON value is written as, the shape check against
/// the library's converters, and the written C# compiled and serialised back. D: = the flint-chart 1.1.1 d.ts,
/// dist/types-DFtJpR3x.d.ts (ChartUpdateOp at :331-386, ChartUpdate at :389-392).</summary>
public sealed class ChartUpdateValueTests
{
    private static string? Write(string json, string type) => new ValueWriter(new SortedSet<string>()).Typed(JsonReader.Parse(json), type);

    // source: NameTables.Unions (the class each discriminator names, the unknown class for any other) and docs/generator.md
    // "Discriminated unions" (the derived class writes its discriminator itself; a required member whose type admits null is
    // written as null when unset); D:331-392
    [TestCase("ChartUpdateOp", """{"op":"set-order","scope":"category","field":"region","values":["Africa","Asia"]}""",
        """"new SetOrderOp { Scope = "category", Field = "region", Values = JsonNode.Parse("""["Africa","Asia"]""") }"""")]
    [TestCase("ChartUpdateOp", """{"target":{"select":{"key":{"region":"Asia"}}},"op":"annotate-text","value":{"text":"Peak","style":{"fontWeight":600}}}""",
        """new AnnotateTextOp { Target = UpdateTarget.Key(new() { ["region"] = "Asia" }), Value = new AnnotationSpec { Text = "Peak", Style = new NoteTextStyle { FontWeight = 600 } } }""")]
    [TestCase("ChartUpdateOp", """{"op":"set-annotation","target":{"select":{"key":{"region":"Europe"}}},"value":null}""",
        """new SetAnnotationOp { Target = UpdateTarget.Key(new() { ["region"] = "Europe" }) }""")]
    [TestCase("ChartUpdateOp", """{"op":"set-overlay","name":"goal","value":{"mark":"rule","style":{"strokeDash":"dot","stroke":"#777777"}}}""",
        """new SetOverlayOp { Name = "goal", Value = new ChartOverlaySpec { Mark = "rule", Style = new OverlayStyleSpec { StrokeDash = "dot", Stroke = "#777777" } } }""")]
    [TestCase("ChartUpdateOp", """{"op":"set-style","targets":[{"select":{"key":{"region":"Asia"}}},null],"value":{"state":"emphasized"},"rank":1}""",
        """new SetStyleOp { Targets = [UpdateTarget.Key(new() { ["region"] = "Asia" }), null!], Value = new StyleSpec { State = "emphasized" }, Extensions = new() { ["rank"] = Json("1") } }""")]
    [TestCase("ChartUpdateOp", """{"op":"set-viewport","axes":null,"value":{"y":[0,150]}}""",
        """"new SetViewportOp { Value = new SetViewportOpValue { Y = JsonNode.Parse("""[0,150]""") }, Extensions = new() { ["axes"] = Json("null") } }"""")]
    [TestCase("ChartUpdateOp", """{"op":"set-highlight","target":"Asia"}""",
        """new UnknownChartUpdateOp("set-highlight") { Extensions = new() { ["target"] = Json("\"Asia\"") } }""")]
    [TestCase("ChartUpdateOp", """{"op":""}""", """new UnknownChartUpdateOp("")""")]
    [TestCase("FreeformOverlayBody", """{"type":"svg","content":"<g/>"}""", """new FreeformSvgBody { Content = "<g/>" }""")]
    [TestCase("FreeformOverlayBody", """{"type":"clone","opacity":0.4}""", """new FreeformCloneBody { Opacity = 0.4 }""")]
    [TestCase("FreeformOverlayBody", """{"type":"glow","radius":3}""", """new UnknownFreeformOverlayBody("glow") { Extensions = new() { ["radius"] = Json("3") } }""")]
    [TestCase("Graspea.Flint.ChartUpdate", """{"id":"focus","ops":[{"op":"zoom"},null]}""", """new ChartUpdate { Id = "focus", Ops = [new UnknownChartUpdateOp("zoom"), null!] }""")]
    [TestCase("Graspea.Flint.ChartUpdate", """{"id":"stamp","ops":[{"op":"set-freeform-overlay","name":"s","value":{"body":[{"type":"svg","content":"<g/>"}]}}]}""",
        """new ChartUpdate { Id = "stamp", Ops = [new SetFreeformOverlayOp { Name = "s", Value = new FreeformOverlaySpec { Body = [new FreeformSvgBody { Content = "<g/>" }] } }] }""")]
    public void Typed_WritesTheClassItsDiscriminatorNames(string type, string json, string expected)
    {
        Assert.That(Write(json, type), Is.EqualTo(expected));
    }

    // source: design §12.3 (fidelity rule): a derived class always writes its required members whose type admits null, so an
    // op that lacks one, or holds a value the member cannot hold, is not written as that class; the ops then go into the
    // update's Extensions whole
    [TestCase("""{"op":"annotate-region","name":"spring"}""")]
    [TestCase("""{"op":"annotate-region","name":"spring","value":5}""")]
    [TestCase("""{"op":"set-overlay","value":{"mark":"rule"},"name":"goal"}""", false)]
    public void Typed_RequiredNullableMember_IsWrittenOnlyWhenTheClassWritesIt(string json, bool unwritable = true)
    {
        Assert.That(Write(json, "ChartUpdateOp") is null, Is.EqualTo(unwritable));
        if (unwritable)
        {
            Assert.That(Write($$"""{"id":"u","ops":[{{json}}]}""", "Graspea.Flint.ChartUpdate"),
                Is.EqualTo($$""""new ChartUpdate { Id = "u", Extensions = new() { ["ops"] = Json("""[{{json}}]""") } }""""));
        }
    }

    // source: design §12.3 (a value the type cannot write exactly is left to the caller's fallback); the library refuses each
    // of these (Shape_AgreesWithTheLibrary)
    [TestCase("ChartUpdateOp", "\"zoom\"")]
    [TestCase("ChartUpdateOp", "1")]
    [TestCase("ChartUpdateOp", "[]")]
    [TestCase("ChartUpdateOp", "{}")]
    [TestCase("ChartUpdateOp", """{"op":1}""")]
    [TestCase("ChartUpdateOp", """{"op":null}""")]
    [TestCase("FreeformOverlayBody", """{"content":"<g/>"}""")]
    public void Typed_OtherShapes_AreNotWritten(string type, string json)
    {
        Assert.That(Write(json, type), Is.Null);
    }

    public static IEnumerable<TestCaseData> Shapes() => new[]
    {
        ("ChartUpdateOp", "{}"), ("ChartUpdateOp", "null"), ("ChartUpdateOp", "\"set-style\""), ("ChartUpdateOp", "[]"), ("ChartUpdateOp", """{"op":1}"""),
        ("ChartUpdateOp", """{"op":null}"""), ("ChartUpdateOp", """{"op":[]}"""), ("ChartUpdateOp", """{"op":""}"""), ("ChartUpdateOp", """{"op":"zoom","x":[1,{"a":null}]}"""),
        ("ChartUpdateOp", """{"op":"set-style","targets":"a"}"""), ("ChartUpdateOp", """{"op":"set-style","targets":[1]}"""),
        ("ChartUpdateOp", """{"op":"set-style","targets":[null],"value":null}"""), ("ChartUpdateOp", """{"op":"set-style","value":{"visible":"yes"}}"""),
        ("ChartUpdateOp", """{"op":"set-style","value":{"opacity":"1"}}"""), ("ChartUpdateOp", """{"op":"set-style","value":[]}"""),
        ("ChartUpdateOp", """{"op":"annotate-text","target":"Asia"}"""), ("ChartUpdateOp", """{"op":"annotate-text","value":{"text":1}}"""),
        ("ChartUpdateOp", """{"op":"annotate-text","value":{"style":{"fontWeight":"bold","fontSize":"1"}}}"""),
        ("ChartUpdateOp", """{"op":"annotate-text","value":{"style":{"fontWeight":true}}}"""), ("ChartUpdateOp", """{"op":"annotate-region","value":{"x":{"a":1},"latitude":[1,"2"]}}"""),
        ("ChartUpdateOp", """{"op":"annotate-region","value":{"x":{"a":1},"latitude":[1,2]}}"""), ("ChartUpdateOp", """{"op":"annotate-enclosure","value":{"targets":{}}}"""),
        ("ChartUpdateOp", """{"op":"set-viewport","value":{"region":{"key":[]}}}"""), ("ChartUpdateOp", """{"op":"set-viewport","value":{"region":{"key":{"a":[1]}},"x":"a"}}"""),
        ("ChartUpdateOp", """{"op":"set-order","values":{}}"""), ("ChartUpdateOp", """{"op":"set-order","scope":1}"""),
        ("ChartUpdateOp", """{"op":"set-overlay","value":{"data":{"values":[1]}}}"""), ("ChartUpdateOp", """{"op":"set-overlay","value":{"data":{"values":[null,{"a":[]}]},"encodings":{"y":{"field":"g"}}}}"""),
        ("ChartUpdateOp", """{"op":"set-overlay","value":{"encodings":{"y":"g"}}}"""), ("ChartUpdateOp", """{"op":"set-overlay","value":{"style":{"strokeDash":[1,"2"]}}}"""),
        ("ChartUpdateOp", """{"op":"set-overlay","value":{"style":{"strokeDash":"dot","fontWeight":600}}}"""), ("ChartUpdateOp", """{"op":"set-freeform-overlay","value":{"body":{}}}"""),
        ("ChartUpdateOp", """{"op":"set-freeform-overlay","value":{"body":[null,{"type":"glow"}]}}"""), ("ChartUpdateOp", """{"op":"set-freeform-overlay","value":{"body":[{"type":2}]}}"""),
        ("ChartUpdateOp", """{"op":"set-data","value":{"rows":[1]}}"""), ("ChartUpdateOp", """{"op":"set-data","value":{"rows":[{"a":[1]},null]},"source":"main"}"""),
        ("FreeformOverlayBody", """{"type":"svg","content":1}"""), ("FreeformOverlayBody", """{"type":"svg","transform":{"scale":"2","translate":{"x":"1"}}}"""),
        ("FreeformOverlayBody", """{"type":"clone","targets":[{"select":{"key":{}}}],"transform":{"rotate":1,"scale":[1,2]}}"""), ("FreeformOverlayBody", """{"type":"clone","opacity":"0.4"}"""),
        ("Graspea.Flint.ChartUpdate", """{"id":1}"""), ("Graspea.Flint.ChartUpdate", """{"ops":{}}"""), ("Graspea.Flint.ChartUpdate", """{"ops":[null,{"op":"zoom"}],"id":null}"""),
        ("Graspea.Flint.ChartUpdate", """{"ops":["set-style"]}"""), ("Graspea.Flint.ChartUpdate", "[]"),
    }.Select(c => new TestCaseData(c.Item1, c.Item2).SetName($"Shape_AgreesWithTheLibrary({c.Item1}, {c.Item2})"));

    // source: the library's converters (JsonSerializer.Deserialize of ChartUpdateOp, FreeformOverlayBody and ChartUpdate) decide
    // each shape
    [TestCaseSource(nameof(Shapes))]
    public void Shape_AgreesWithTheLibrary(string type, string json)
    {
        var library = type switch
        {
            "ChartUpdateOp" => typeof(ChartUpdateOp),
            "FreeformOverlayBody" => typeof(FreeformOverlayBody),
            _ => typeof(ChartUpdate),
        };
        Assert.That(ShapeRejects(json, type), Is.EqualTo(Rejects(() => JsonSerializer.Deserialize(json, library))));
    }

    private static readonly string[] Written =
    [
        """{"op":"set-style","targets":[{"select":{"key":{"region":"Asia"}}},{"select":{"where":{"sales":{"range":[80,null]}},"visual":{"kind":"mark","role":"bar"}}},null],"value":{"state":"emphasized","mutedOpacity":0.3,"visible":true}}""",
        """{"op":"annotate-text","target":{"visual":{"kind":"mark","role":"bar"},"elements":[{"value":{"region":"Asia"}}]},"value":{"text":"Peak","anchor":"top","style":{"fontWeight":"bold","fontSize":12},"highlightStyle":{"color":"#c0392b","width":2}}}""",
        """{"op":"set-annotation","target":{"select":{"key":{"region":"Europe"}}},"value":null}""",
        """{"op":"annotate-region","name":"spring","value":{"x":["Asia","Europe"],"y":null,"latitude":[10,20],"text":"Spring","opacity":0.2}}""",
        """{"op":"annotate-enclosure","name":"pair","value":{"targets":[{"select":{"key":{"region":"Asia"}}},{"select":{"key":{"region":"Europe"}}}],"text":"Both","width":2}}""",
        """{"op":"set-viewport","axes":"y","value":{"y":[0,150],"region":{"key":{"region":"Asia","n":null}}}}""",
        """{"op":"set-order","scope":"category","field":"region","values":["Africa",1,null,{"k":true}]}""",
        """{"op":"set-overlay","name":"goal","value":{"data":{"values":[{"goal":100},null]},"encodings":{"y":{"field":"goal"},"x":null},"mark":"rule","interactive":false,"style":{"strokeDash":[4,2],"stroke":"#777777","fontWeight":600}}}""",
        """{"op":"set-freeform-overlay","name":"stamp","value":{"coordinateSpace":"plot","body":[{"type":"svg","content":"<circle r=\"4\"/>","transform":{"rotate":45,"scale":[1,2],"translate":{"x":4,"y":-2}}},{"type":"clone","targets":[{"select":{"key":{"region":"Asia"}}}],"opacity":0.4},{"type":"glow","radius":3},null]}}""",
        """{"op":"set-data","source":"main","value":{"rows":[{"region":"Asia","month":"2025-06","sales":130},null]}}""",
        """{"op":"set-highlight","target":"Asia","options":{"n":[1,null]}}""",
        """{"op":"annotate-text","target":null,"value":{"text":"Peak"},"rank":1}""",
        """{"op":"annotate-region","name":"spring"}""",
    ];

    // source: design §12.3 (fidelity rule) and §12.10 (compile round-trip): the C# written for each op compiles without warnings
    // (C# 12, net8.0 reference pack) and the library serialises the value it builds as the input JSON; an op no class writes
    // exactly is held by the update's Extensions
    [Test]
    public void Written_CompilesAndSerialisesAsTheInput()
    {
        var usings = new SortedSet<string>(StringComparer.Ordinal);
        var values = new ValueWriter(usings);
        var methods = Written.Select((json, i) =>
            $"    public static Graspea.Flint.ChartUpdate? V{i}() => {values.Typed(JsonReader.Parse($$"""{"id":"u{{i}}","ops":[{{json}}]}"""), "Graspea.Flint.ChartUpdate")};\n").ToList();
        var source = string.Concat(usings.Prepend("System.Text.Json").Distinct().Select(u => $"using {u};\n")) +
            "public static class Updates\n{\n" + string.Concat(methods) +
            "    private static JsonElement Json(string json)\n    {\n        using var document = JsonDocument.Parse(json);\n        return document.RootElement.Clone();\n    }\n}\n";

        var compilation = Net8Compiler.Compile("ChartUpdates", [("Updates.cs", source)]);
        Assert.That(Net8Compiler.Problems(compilation).Select(p => p.ToString()), Is.Empty, source);
        var (assembly, context) = Net8Compiler.Load(compilation);
        try
        {
            var type = assembly.GetType("Updates")!;
            for (var i = 0; i < Written.Length; i++)
            {
                var value = type.GetMethod($"V{i}", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
                var json = JsonSerializer.Serialize(value, typeof(ChartUpdate));
                var expected = $$"""{"id":"u{{i}}","ops":[{{Written[i]}}]}""";
                Assert.That(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(expected)), Is.True, $"{expected}\nwritten as {json}");
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
