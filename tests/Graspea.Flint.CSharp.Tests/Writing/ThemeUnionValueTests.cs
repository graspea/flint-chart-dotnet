using System.Text.Json;
using Graspea.Flint.CSharp;
using Graspea.Flint.Theming;

namespace Graspea.Flint.CSharp.Tests.Writing;

/// <summary>The hand-written theme unions in the conversion core: the C# each JSON form is written as, and the shape
/// check against the library's converters. D: = the flint-chart 1.0.0 d.ts, dist/types-B9fiOPP4.d.ts.</summary>
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
    }.Select(c => new TestCaseData(c.Item1, c.Item2, c.Item3).SetName($"Shape_AgreesWithTheLibrary({c.Item2}, {c.Item3})"));

    // source: the library's converters (JsonSerializer.Deserialize of the type) decide each shape
    [TestCaseSource(nameof(Shapes))]
    public void Shape_AgreesWithTheLibrary(Type library, string type, string json)
    {
        bool libraryRejects;
        try
        {
            JsonSerializer.Deserialize(json, library);
            libraryRejects = false;
        }
        catch (JsonException)
        {
            libraryRejects = true;
        }

        bool shapeRejects;
        try
        {
            SpecShape.Typed(JsonReader.Parse(json), type, "x");
            shapeRejects = false;
        }
        catch (Exception e) when (e is not InvalidOperationException)
        {
            shapeRejects = true;
        }

        Assert.That(shapeRejects, Is.EqualTo(libraryRejects));
    }
}
