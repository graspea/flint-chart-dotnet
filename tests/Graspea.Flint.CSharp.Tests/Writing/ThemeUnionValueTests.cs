using System.Text.Json;
using Graspea.Flint.CSharp;
using Graspea.Flint.Serialization;
using Graspea.Flint.Theming;

namespace Graspea.Flint.CSharp.Tests.Writing;

/// <summary>The theme unions in the conversion core: the C# each JSON form is written as, and the shape check against the
/// library's converters. The hand-written unions are named here; the generated open literal unions (<c>'a' | string</c>)
/// and integer-keyed maps are read from the name tables, so those cases exist only on a pin that has them (flint-chart
/// 1.0.0: <c>PointInk</c>, <c>ThemeInk.series.categoricalByCount</c>). D: = the flint-chart 1.0.0 d.ts,
/// dist/types-B9fiOPP4.d.ts.</summary>
public sealed class ThemeUnionValueTests
{
    private const string Weight = "Graspea.Flint.Theming.TypeWeight";
    private const string Length = "Graspea.Flint.Theming.ThemeLength";
    private const string Overflow = "Graspea.Flint.Theming.LabelOverflow";
    private const string Rule = "Graspea.Flint.Theming.RuleSpec";
    private const string Minor = "Graspea.Flint.Theming.MinorGridSpec";

    private static string? Write(string type, string json) => new ValueWriter(new SortedSet<string>()).Typed(JsonReader.Parse(json), type);

    // source: the implicit conversions of TypeWeight (string, double), ThemeLength (double, string), LabelOverflow
    // (string, string[]), RuleSpec and MinorGridSpec (string), and their object initialisers; D:420, D:434-461, D:989-1025
    [TestCase(Weight, "\"bold\"", "\"bold\"")]
    [TestCase(Weight, "700", "700")]
    [TestCase(Length, "12", "12")]
    [TestCase(Length, "\"40%\"", "\"40%\"")]
    [TestCase(Overflow, "\"wrap\"", "\"wrap\"")]
    [TestCase(Overflow, "[\"wrap\",\"rotate90\"]", "new string[] { \"wrap\", \"rotate90\" }")]
    [TestCase(Rule, "\"quiet\"", "\"quiet\"")]
    [TestCase(Rule, "{\"presence\":\"full\",\"dash\":[4,2],\"weight\":1.5}", "new RuleSpec { Presence = \"full\", Dash = [4, 2], Weight = 1.5 }")]
    [TestCase(Rule, "{\"future\":1}", "new RuleSpec { Extensions = new() { [\"future\"] = Json(\"1\") } }")]
    [TestCase(Minor, "\"hairline\"", "\"hairline\"")]
    [TestCase(Minor, "{\"divisions\":4,\"on\":[\"measure\"]}", "new MinorGridSpec { Divisions = 4, On = [\"measure\"] }")]
    public void Typed_WritesTheForm(string type, string json, string expected)
    {
        Assert.That(Write(type, json), Is.EqualTo(expected));
    }

    // source: design §12.3 (fidelity rule: a value the type cannot write exactly is left to the caller's fallback)
    [TestCase(Weight, "true")]
    [TestCase(Length, "{}")]
    [TestCase(Overflow, "[1]")]
    [TestCase(Rule, "2")]
    [TestCase(Minor, "[]")]
    public void Typed_OtherShapes_AreNotWritten(string type, string json)
    {
        Assert.That(Write(type, json), Is.Null);
    }

    private static readonly System.Reflection.Assembly Library = typeof(Chart).Assembly;

    // source: maintainer decision on the flint-chart 1.0.0 sync (Record<number, T> is Dictionary<int, T>, its keys JSON
    // strings in the integer's invariant form)
    [TestCase("{\"2\":[\"#a\"],\"10\":[]}", "new() { [2] = [\"#a\"], [10] = [] }")]
    [TestCase("{\"-1\":[]}", "new() { [-1] = [] }")]
    [TestCase("{}", "new()")]
    public void Typed_IntKeyMap_WritesIntegerKeys(string json, string expected)
    {
        Assert.That(Write("Dictionary<int, string[]>", json), Is.EqualTo(expected));
    }

    // source: design §12.3 (fidelity rule) and IntKeyDictionaryConverter, which refuses a key that does not write back as
    // itself
    [TestCase("{\"02\":[]}")]
    [TestCase("{\"2.5\":[]}")]
    [TestCase("{\"x\":[]}")]
    [TestCase("{\"2\":[1]}")]
    [TestCase("[]")]
    public void Typed_IntKeyMap_OtherShapes_AreNotWritten(string json)
    {
        Assert.That(Write("Dictionary<int, string[]>", json), Is.Null);
    }

    // source: the library's IntKeyDictionaryConverter (JsonSerializer.Deserialize with it) decides each shape
    [TestCase("{\"2\":\"a\",\"-3\":\"b\"}")]
    [TestCase("{\"2\":null}")]
    [TestCase("{\"02\":\"a\"}")]
    [TestCase("{\"+2\":\"a\"}")]
    [TestCase("{\"2.5\":\"a\"}")]
    [TestCase("{\"99999999999\":\"a\"}")]
    [TestCase("{\"2\":1}")]
    [TestCase("[]")]
    public void IntKeyMap_Shape_AgreesWithTheLibrary(string json)
    {
        var options = new JsonSerializerOptions { Converters = { new IntKeyDictionaryConverter<string>() } };
        Assert.That(ShapeRejects(json, "Dictionary<int, string>"), Is.EqualTo(Rejects(() => JsonSerializer.Deserialize<Dictionary<int, string>>(json, options))));
    }

    public static IEnumerable<TestCaseData> OpenUnionLiterals() =>
        NameTables.StringUnions.SelectMany(u => u.Literals.Select(l =>
            new TestCaseData(u.Name, l.Wire, l.Member).SetName($"Typed_OpenUnion_KnownLiteral_IsItsProperty({u.Name}, {l.Wire})")));

    // source: the generated struct's static readonly field per d.ts literal (maintainer decision on the flint-chart 1.0.0 sync)
    [TestCaseSource(nameof(OpenUnionLiterals))]
    public void Typed_OpenUnion_KnownLiteral_IsItsProperty(string type, string wire, string member)
    {
        Assert.That(Write(type, JsonSerializer.Serialize(wire)), Is.EqualTo($"{NameTables.SimpleName(type)}.{member}"));
    }

    public static IEnumerable<TestCaseData> OpenUnions() =>
        NameTables.StringUnions.Select(u => new TestCaseData(u.Name).SetName($"Typed_OpenUnion_OtherString_IsAStringLiteral({u.Name})"));

    // source: the generated struct's implicit conversion from string (any other string is a value), and design §12.3
    // (a value of another kind is left to the caller's fallback)
    [TestCaseSource(nameof(OpenUnions))]
    public void Typed_OpenUnion_OtherString_IsAStringLiteral(string type)
    {
        Assert.That(Write(type, "\"#333\""), Is.EqualTo("\"#333\""));
        Assert.That(Write(type, "\"\""), Is.EqualTo("\"\""));
        Assert.That(Write(type, "1"), Is.Null);
    }

    public static IEnumerable<TestCaseData> Shapes() => new[]
    {
        (typeof(TypeWeight), Weight, "\"bold\""), (typeof(TypeWeight), Weight, "400"), (typeof(TypeWeight), Weight, "true"),
        (typeof(TypeWeight), Weight, "[]"),
        (typeof(ThemeLength), Length, "\"8px\""), (typeof(ThemeLength), Length, "8"), (typeof(ThemeLength), Length, "{}"),
        (typeof(LabelOverflow), Overflow, "\"skip\""), (typeof(LabelOverflow), Overflow, "[\"wrap\"]"), (typeof(LabelOverflow), Overflow, "[null]"),
        (typeof(LabelOverflow), Overflow, "3"),
        (typeof(RuleSpec), Rule, "\"full\""), (typeof(RuleSpec), Rule, "{\"weight\":1,\"x\":[]}"), (typeof(RuleSpec), Rule, "{\"dash\":[null]}"),
        (typeof(RuleSpec), Rule, "{\"weight\":\"1\"}"), (typeof(RuleSpec), Rule, "1"),
        (typeof(MinorGridSpec), Minor, "\"quiet\""), (typeof(MinorGridSpec), Minor, "{\"on\":[\"measure\"],\"divisions\":2}"),
        (typeof(MinorGridSpec), Minor, "{\"on\":\"measure\"}"), (typeof(MinorGridSpec), Minor, "[]"),
    }.Concat(NameTables.StringUnions.SelectMany(u => new[] { "\"series\"", "\"#333\"", "\"\"", "1", "[]", "{}" }
        .Select(json => (Library.GetType(u.Name, throwOnError: true)!, u.Name, json))))
     .Select(c => new TestCaseData(c.Item1, c.Item2, c.Item3).SetName($"Shape_AgreesWithTheLibrary({c.Item2}, {c.Item3})"));

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
