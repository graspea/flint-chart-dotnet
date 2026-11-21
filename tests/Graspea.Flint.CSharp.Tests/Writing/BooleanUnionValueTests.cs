using System.Text.Json;
using Graspea.Flint.CSharp;

namespace Graspea.Flint.CSharp.Tests.Writing;

/// <summary>The hand-written <c>boolean | object</c> unions of flint-chart 1.1.1 in the conversion core: the C# each JSON form
/// is written as, and the shape check against the library's converters. No generated member holds them before the 1.1.1
/// sync, so they are named here. D: = the flint-chart 1.1.1 d.ts, dist/types-DFtJpR3x.d.ts.</summary>
public sealed class BooleanUnionValueTests
{
    private const string Fill = "Graspea.Flint.SparseFill";
    private const string Wrap = "Graspea.Flint.TitleWrap";
    private const string Defaults = "Graspea.Flint.InteractionDefaults";

    private static string? Write(string type, string json) => new ValueWriter(new SortedSet<string>()).Typed(JsonReader.Parse(json), type);

    // source: the implicit conversion from bool and the object initialiser of SparseFill, TitleWrap and InteractionDefaults;
    // D:5734-5755 (sparseFill, titleWrap) and D:3495-3506 (InteractionDefaults)
    [TestCase(Fill, "true", "true")]
    [TestCase(Fill, "false", "false")]
    [TestCase(Fill, "{\"share\":0.9,\"barCap\":160}", "new SparseFill { Share = 0.9, BarCap = 160 }")]
    [TestCase(Fill, "{\"pointCap\":360}", "new SparseFill { PointCap = 360 }")]
    [TestCase(Fill, "{}", "new SparseFill()")]
    [TestCase(Fill, "{\"future\":1}", "new SparseFill { Extensions = new() { [\"future\"] = Json(\"1\") } }")]
    [TestCase(Wrap, "true", "true")]
    [TestCase(Wrap, "{\"minWidth\":280,\"overhang\":1.2,\"floor\":0.5}", "new TitleWrap { MinWidth = 280, Overhang = 1.2, Floor = 0.5 }")]
    [TestCase(Wrap, "{\"floor\":\"0.5\"}", "new TitleWrap { Extensions = new() { [\"floor\"] = Json(\"\\\"0.5\\\"\") } }")]
    [TestCase(Defaults, "false", "false")]
    [TestCase(Defaults, "{\"tooltip\":false,\"legend\":true}", "new InteractionDefaults { Tooltip = false, Legend = true }")]
    [TestCase(Defaults, "{\"tooltip\":null}", "new InteractionDefaults { Extensions = new() { [\"tooltip\"] = Json(\"null\") } }")]
    public void Typed_WritesTheForm(string type, string json, string expected)
    {
        Assert.That(Write(type, json), Is.EqualTo(expected));
    }

    // source: design §12.3 (fidelity rule: a value the type cannot write exactly is left to the caller's fallback)
    [TestCase(Fill, "1")]
    [TestCase(Fill, "\"on\"")]
    [TestCase(Wrap, "[]")]
    [TestCase(Wrap, "0")]
    [TestCase(Defaults, "\"off\"")]
    [TestCase(Defaults, "[false]")]
    public void Typed_OtherShapes_AreNotWritten(string type, string json)
    {
        Assert.That(Write(type, json), Is.Null);
    }

    public static IEnumerable<TestCaseData> Shapes() => new[]
    {
        (typeof(SparseFill), Fill, "true"), (typeof(SparseFill), Fill, "false"), (typeof(SparseFill), Fill, "{}"),
        (typeof(SparseFill), Fill, "{\"share\":0.9,\"barCap\":200,\"pointCap\":360,\"x\":[]}"), (typeof(SparseFill), Fill, "{\"share\":\"0.9\"}"),
        (typeof(SparseFill), Fill, "{\"barCap\":null}"), (typeof(SparseFill), Fill, "{\"pointCap\":true}"), (typeof(SparseFill), Fill, "1"),
        (typeof(SparseFill), Fill, "\"on\""), (typeof(SparseFill), Fill, "[]"), (typeof(SparseFill), Fill, "null"),
        (typeof(TitleWrap), Wrap, "true"), (typeof(TitleWrap), Wrap, "{\"floor\":0.6,\"minWidth\":320,\"overhang\":1.15}"),
        (typeof(TitleWrap), Wrap, "{\"floor\":true}"), (typeof(TitleWrap), Wrap, "{\"minWidth\":\"320\"}"), (typeof(TitleWrap), Wrap, "0"),
        (typeof(TitleWrap), Wrap, "[true]"),
        (typeof(InteractionDefaults), Defaults, "false"), (typeof(InteractionDefaults), Defaults, "{\"tooltip\":false,\"zoom\":1}"),
        (typeof(InteractionDefaults), Defaults, "{\"legend\":\"no\"}"), (typeof(InteractionDefaults), Defaults, "{\"legend\":0}"),
        (typeof(InteractionDefaults), Defaults, "{\"tooltip\":null}"), (typeof(InteractionDefaults), Defaults, "\"off\""),
        (typeof(InteractionDefaults), Defaults, "{}"),
    }.Select(c => new TestCaseData(c.Item1, c.Item2, c.Item3).SetName($"Shape_AgreesWithTheLibrary({c.Item2}, {c.Item3})"));

    // source: the library's converters (JsonSerializer.Deserialize of the type) decide each shape
    [TestCaseSource(nameof(Shapes))]
    public void Shape_AgreesWithTheLibrary(Type library, string type, string json)
    {
        Assert.That(ShapeRejects(json, type), Is.EqualTo(Rejects(() => JsonSerializer.Deserialize(json, library))));
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
