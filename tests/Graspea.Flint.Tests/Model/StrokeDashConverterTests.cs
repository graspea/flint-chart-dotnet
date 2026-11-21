using System.Text.Json;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Model;

// D: = the flint-chart 1.1.1 d.ts, dist/types-DFtJpR3x.d.ts. The member that holds a StrokeDash (OverlayStyleSpec.strokeDash)
// is generated from flint-chart 1.1.1 on.
public sealed class StrokeDashConverterTests
{
    // source: D:246-247 (strokeDash?: readonly number[] | 'dash' | 'dashed' | 'dot' | 'dotted': dash and gap lengths in px,
    // or a word)
    [Test]
    public void KeywordAndLengths_RoundTrip()
    {
        StrokeDash word = "dot";
        StrokeDash lengths = new double[] { 4, 2 };
        Assert.That(JsonSerializer.Serialize(word), Is.EqualTo("\"dot\""));
        Assert.That((word.Keyword, word.Lengths), Is.EqualTo(("dot", (double[]?)null)));
        Assert.That(JsonSerializer.Serialize(lengths), Is.EqualTo("[4,2]"));
        Assert.That(lengths.Keyword, Is.Null);
        Assert.That(lengths.Lengths, Is.EqualTo(new double[] { 4, 2 }));

        var read = JsonSerializer.Deserialize<StrokeDash>("[6,3,1.5]")!;
        Assert.That(read.Keyword, Is.Null);
        Assert.That(read.Lengths, Is.EqualTo(new[] { 6, 3, 1.5 }));
        Assert.That(JsonSerializer.Deserialize<StrokeDash>("\"dashed\"")!.Keyword, Is.EqualTo("dashed"));
        Assert.That(JsonSerializer.Serialize(JsonSerializer.Deserialize<StrokeDash>("[]")), Is.EqualTo("[]"), "an empty array stays an array");
        Assert.That(JsonSerializer.Serialize(JsonSerializer.Deserialize<StrokeDash>("\"zigzag\"")), Is.EqualTo("\"zigzag\""), "any other word is kept");
    }

    // source: the implicit conversions take a null string or array as no value, as TypeWeight and LabelOverflow do
    [Test]
    public void FromNull_IsNull()
    {
        string? noWord = null;
        double[]? noLengths = null;
        StrokeDash? dash = noWord;
        Assert.That(dash, Is.Null);
        dash = noLengths;
        Assert.That(dash, Is.Null);
    }

    // source: design §5.1 (host-options guarantee; converters ignore the caller's options)
    [Test]
    public void Serialize_UnderHostileOptions_WritesTheSameDocument()
    {
        object[] values = [(StrokeDash)"dash", (StrokeDash)new double[] { 4, 2.5 }, (StrokeDash)Array.Empty<double>()];
        foreach (var value in values)
        {
            Assert.That(JsonSerializer.Serialize(value, value.GetType(), HostileOptions.Create()), Is.EqualTo(JsonSerializer.Serialize(value, value.GetType())));
        }
    }

    // source: D:247 (the lengths are numbers) and design §5.1 (numbers strict, whatever the caller's options allow)
    [TestCase("""["4",2]""")]
    [TestCase("[4,null]")]
    [TestCase("[true]")]
    [TestCase("[[4,2]]")]
    public void Lengths_ItemOfAnotherKind_ThrowsUnderAnyOptions(string json)
    {
        const string message = "The items of strokeDash must be numbers.";
        Assert.That((Action)(() => JsonSerializer.Deserialize<StrokeDash>(json)), Throws.InstanceOf<JsonException>().With.Message.EqualTo(message));
        Assert.That((Action)(() => JsonSerializer.Deserialize<StrokeDash>(json, HostileOptions.Create())), Throws.InstanceOf<JsonException>().With.Message.EqualTo(message));
    }

    // source: design §5.1 (a value of another shape is malformed input: JsonException); D:247 (OverlayStyleSpec.strokeDash);
    // the wording is the converter's own, pinned here
    [TestCase("4")]
    [TestCase("true")]
    [TestCase("{}")]
    public void Deserialize_OtherShapes_ThrowJsonExceptionNamingTheMember(string json)
    {
        Assert.That((Action)(() => JsonSerializer.Deserialize<StrokeDash>(json)),
            Throws.InstanceOf<JsonException>().With.Message.EqualTo("strokeDash must be a string or an array of numbers."));
    }
}
