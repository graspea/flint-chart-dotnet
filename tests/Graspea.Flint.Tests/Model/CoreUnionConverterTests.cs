using System.Text.Json;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Model;

// D: = the flint-chart 1.0.0 d.ts, dist/types-B9fiOPP4.d.ts.
public sealed class CoreUnionConverterTests
{
    // source: D:2168-2172 (ChartChrome = false | {header?, footer?, logos?})
    [Test]
    public void ChartChrome_FalseAndObject_RoundTrip()
    {
        Assert.That(JsonSerializer.Serialize(ChartChrome.None), Is.EqualTo("false"));
        Assert.That(JsonSerializer.Deserialize<ChartChrome>("false")!.IsNone, Is.True);
        var parts = new ChartChrome { Header = false, Logos = true };
        Assert.That(JsonSerializer.Serialize(parts), Is.EqualTo("""{"header":false,"logos":true}"""));
        var read = JsonSerializer.Deserialize<ChartChrome>("""{"footer":false}""")!;
        Assert.That((read.IsNone, read.Header, read.Footer, read.Logos), Is.EqualTo((false, (bool?)null, (bool?)false, (bool?)null)));
    }

    // source: D:4831-4835 (source?: string | {text: string; href?: string})
    [Test]
    public void ChartSource_TextAndObject_RoundTrip()
    {
        ChartSource text = "Survey of 2024";
        Assert.That(JsonSerializer.Serialize(text), Is.EqualTo("\"Survey of 2024\""));
        var linked = new ChartSource { Text = "Open data", Href = "https://example.com/data" };
        Assert.That(JsonSerializer.Serialize(linked), Is.EqualTo("""{"text":"Open data","href":"https://example.com/data"}"""));
        var read = JsonSerializer.Deserialize<ChartSource>("""{"text":"Open data"}""")!;
        Assert.That((read.Text, read.Href), Is.EqualTo(("Open data", (string?)null)));
        Assert.That(JsonSerializer.Serialize(read), Is.EqualTo("""{"text":"Open data"}"""), "the object form stays an object");
        Assert.That(JsonSerializer.Deserialize<ChartSource>("\"Survey\"")!.Text, Is.EqualTo("Survey"));
    }

    // source: D:3644 (title?: string | null: a name in place of the field's, null prints none, absent keeps the field's)
    [Test]
    public void ChannelTitle_TextAndNone_RoundTrip()
    {
        ChannelTitle named = "Revenue";
        Assert.That(JsonSerializer.Serialize(named), Is.EqualTo("\"Revenue\""));
        Assert.That(JsonSerializer.Serialize(ChannelTitle.None), Is.EqualTo("null"));
        Assert.That(JsonSerializer.Deserialize<ChannelTitle>("\"Revenue\"")!.Text, Is.EqualTo("Revenue"));
        var none = JsonSerializer.Deserialize<ChannelTitle>("null");
        Assert.That(none, Is.Not.Null);
        Assert.That(none!.Text, Is.Null);
    }

    // source: D:3644 and design §5.2 (absent members are omitted): an unset title is omitted, ChannelTitle.None is written
    // as null, and both read back as they were
    [Test]
    public void ChartEncoding_Title_KeepsAbsentNullAndText()
    {
        Assert.That(JsonSerializer.Serialize(new ChartEncoding { Field = "a" }), Is.EqualTo("""{"field":"a"}"""));
        Assert.That(JsonSerializer.Serialize(new ChartEncoding { Field = "a", Title = ChannelTitle.None }), Is.EqualTo("""{"field":"a","title":null}"""));
        Assert.That(JsonSerializer.Serialize(new ChartEncoding { Field = "a", Title = "A" }), Is.EqualTo("""{"field":"a","title":"A"}"""));
        Assert.That(JsonSerializer.Deserialize<ChartEncoding>("""{"field":"a"}""")!.Title, Is.Null);
        var none = JsonSerializer.Deserialize<ChartEncoding>("""{"field":"a","title":null}""")!;
        Assert.That(none.Title?.Text, Is.Null);
        Assert.That(none.Title, Is.Not.Null);
        Assert.That(JsonSerializer.Serialize(none), Is.EqualTo("""{"field":"a","title":null}"""));
    }

    // source: D:3228-3231 and D:3239-3246 (assistedTargeting?: boolean | AssistedTargetingOptions; AssistedTargetingOptions
    // extends TargetFeedbackOptions {indicator?, details?} with maxDistance?)
    [Test]
    public void AssistedTargeting_BooleanAndObject_RoundTrip()
    {
        AssistedTargeting off = false;
        Assert.That(JsonSerializer.Serialize(off), Is.EqualTo("false"));
        Assert.That(off.Enabled, Is.False);
        var tuned = new AssistedTargeting { MaxDistance = 24, Indicator = true, Details = new TargetDetails { Fields = ["region"], MaxRows = 3 } };
        JsonAssert.Equal("""{"details":{"fields":["region"],"maxRows":3},"indicator":true,"maxDistance":24}""", JsonSerializer.Serialize(tuned));
        var read = JsonSerializer.Deserialize<AssistedTargeting>("""{"details":true}""")!;
        Assert.That((read.Enabled, read.Details?.Enabled), Is.EqualTo(((bool?)null, (bool?)true)));
        Assert.That(JsonSerializer.Deserialize<AssistedTargeting>("true")!.Enabled, Is.True);
    }

    // source: D:3220-3227 (details?: boolean | TargetDetailsOptions {fields?: readonly string[]; maxRows?: number})
    [Test]
    public void TargetDetails_BooleanAndObject_RoundTrip()
    {
        TargetDetails on = true;
        Assert.That(JsonSerializer.Serialize(on), Is.EqualTo("true"));
        var read = JsonSerializer.Deserialize<TargetDetails>("""{"maxRows":2}""")!;
        Assert.That((read.Enabled, read.MaxRows, read.Fields), Is.EqualTo(((bool?)null, (double?)2, (string[]?)null)));
    }

    // source: D:2842-2849 (SemanticAnnotation.polarity?: 'higher-is-better' | 'lower-is-better') and design §4.1 (string-backed,
    // unknown values written verbatim)
    [Test]
    public void Polarity_IsWrittenAsItsWireString()
    {
        Assert.That(JsonSerializer.Serialize(Polarity.LowerIsBetter), Is.EqualTo("\"lower-is-better\""));
        Assert.That(JsonSerializer.Deserialize<Polarity>("\"higher-is-better\""), Is.EqualTo(Polarity.HigherIsBetter));
        Assert.That(JsonSerializer.Deserialize<Polarity>("\"sideways\"").Name, Is.EqualTo("sideways"));
    }

    // source: design §5.2 (unknown keys round-trip through Extensions and are written last)
    [Test]
    public void ObjectForms_UnknownKeys_RoundTripAfterKnownMembers()
    {
        Assert.That(JsonSerializer.Serialize(JsonSerializer.Deserialize<ChartChrome>("""{"future":1,"header":true}""")), Is.EqualTo("""{"header":true,"future":1}"""));
        Assert.That(JsonSerializer.Serialize(JsonSerializer.Deserialize<ChartSource>("""{"future":1,"text":"t"}""")), Is.EqualTo("""{"text":"t","future":1}"""));
        Assert.That(JsonSerializer.Serialize(JsonSerializer.Deserialize<AssistedTargeting>("""{"future":1,"indicator":false}""")), Is.EqualTo("""{"indicator":false,"future":1}"""));
        Assert.That(JsonSerializer.Serialize(JsonSerializer.Deserialize<TargetDetails>("""{"future":1,"maxRows":1}""")), Is.EqualTo("""{"maxRows":1,"future":1}"""));
    }

    // source: design §5.1 (host-options guarantee; converters ignore the caller's options)
    [Test]
    public void Serialize_UnderHostileOptions_WritesTheSameDocument()
    {
        object[] values =
        [
            ChartChrome.None, new ChartChrome { Footer = false }, (ChartSource)"s", new ChartSource { Text = "t", Href = "h" },
            (ChannelTitle)"Revenue", ChannelTitle.None, (AssistedTargeting)true, new AssistedTargeting { MaxDistance = 8, Details = false },
            new TargetDetails { Fields = ["a"], MaxRows = 1 }, Polarity.HigherIsBetter,
        ];
        foreach (var value in values)
        {
            Assert.That(JsonSerializer.Serialize(value, value.GetType(), HostileOptions.Create()), Is.EqualTo(JsonSerializer.Serialize(value, value.GetType())));
        }
    }

    // source: design §5.1 (a value of another shape is malformed input: JsonException); D:2168 (chrome is false or an
    // object, never true), D:4831 (the object form of source requires text)
    [TestCase(typeof(ChartChrome), "true")]
    [TestCase(typeof(ChartChrome), "\"none\"")]
    [TestCase(typeof(ChartSource), "1")]
    [TestCase(typeof(ChartSource), """{"href":"h"}""")]
    [TestCase(typeof(ChannelTitle), "1")]
    [TestCase(typeof(AssistedTargeting), "\"off\"")]
    [TestCase(typeof(TargetDetails), "[]")]
    public void Deserialize_OtherShapes_ThrowJsonException(Type type, string json)
    {
        Assert.That((Action)(() => JsonSerializer.Deserialize(json, type)), Throws.InstanceOf<JsonException>());
    }
}
