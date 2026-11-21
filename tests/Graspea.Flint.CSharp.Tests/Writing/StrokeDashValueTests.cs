using System.Text.Json;
using Graspea.Flint.CSharp;

namespace Graspea.Flint.CSharp.Tests.Writing;

/// <summary>The hand-written <c>StrokeDash</c> of flint-chart 1.1.1 in the conversion core: the C# each JSON form is written as,
/// and the shape check against the library's converter. No generated member holds it before the 1.1.1 sync, so it is named
/// here. D: = the flint-chart 1.1.1 d.ts, dist/types-DFtJpR3x.d.ts.</summary>
public sealed class StrokeDashValueTests
{
    private const string Dash = "Graspea.Flint.StrokeDash";

    private static string? Write(string json) => new ValueWriter(new SortedSet<string>()).Typed(JsonReader.Parse(json), Dash);

    // source: the implicit conversions of StrokeDash (string, double[]); D:246-247 (strokeDash?: readonly number[] | 'dash' |
    // 'dashed' | 'dot' | 'dotted')
    [TestCase("\"dot\"", "\"dot\"")]
    [TestCase("\"dashed\"", "\"dashed\"")]
    [TestCase("\"zigzag\"", "\"zigzag\"")]
    [TestCase("[4,2]", "new double[] { 4, 2 }")]
    [TestCase("[6,3,1.5]", "new double[] { 6, 3, 1.5 }")]
    [TestCase("[]", "new double[] { }")]
    public void Typed_WritesTheForm(string json, string expected)
    {
        Assert.That(Write(json), Is.EqualTo(expected));
    }

    // source: design §12.3 (fidelity rule: a value the type cannot write exactly is left to the caller's fallback; a number
    // double cannot hold is one)
    [TestCase("4")]
    [TestCase("true")]
    [TestCase("{}")]
    [TestCase("[\"4\"]")]
    [TestCase("[4,null]")]
    [TestCase("[1e400]")]
    public void Typed_OtherShapes_AreNotWritten(string json)
    {
        Assert.That(Write(json), Is.Null);
    }

    // source: the library's converter (JsonSerializer.Deserialize of StrokeDash) decides each shape
    [TestCase("\"dot\"")]
    [TestCase("\"\"")]
    [TestCase("[4,2]")]
    [TestCase("[]")]
    [TestCase("null")]
    [TestCase("4")]
    [TestCase("true")]
    [TestCase("{}")]
    [TestCase("[\"4\"]")]
    [TestCase("[4,null]")]
    [TestCase("[[4]]")]
    [TestCase("[1e400]")]
    public void Shape_AgreesWithTheLibrary(string json)
    {
        Assert.That(ShapeRejects(json), Is.EqualTo(Rejects(() => JsonSerializer.Deserialize<StrokeDash>(json))));
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

    private static bool ShapeRejects(string json)
    {
        try
        {
            SpecShape.Typed(JsonReader.Parse(json), Dash, "x");
            return false;
        }
        catch (Exception e) when (e is not InvalidOperationException)
        {
            return true;
        }
    }
}
