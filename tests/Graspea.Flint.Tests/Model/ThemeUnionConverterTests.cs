using System.Text.Json;
using Graspea.Flint.Tests.Support;
using Graspea.Flint.Theming;

namespace Graspea.Flint.Tests.Model;

// D: = the flint-chart 1.0.0 d.ts, dist/types-B9fiOPP4.d.ts.
public sealed class ThemeUnionConverterTests
{
    // source: D:456 (TypeWeight = 'thin' | … | 'black' | 100 | … | 900)
    [Test]
    public void TypeWeight_KeywordAndNumber_RoundTrip()
    {
        TypeWeight keyword = "bold";
        TypeWeight number = 700;
        Assert.That(JsonSerializer.Serialize(keyword), Is.EqualTo("\"bold\""));
        Assert.That(JsonSerializer.Serialize(number), Is.EqualTo("700"));
        var read = JsonSerializer.Deserialize<TypeWeight>("600")!;
        Assert.That((read.Keyword, read.Number), Is.EqualTo(((string?)null, (double?)600)));
        Assert.That(JsonSerializer.Deserialize<TypeWeight>("\"light\"")!.Keyword, Is.EqualTo("light"));
    }

    // source: flint-chart 0.5.1 TypeRole.weight is a string literal union, generated as string? (PublicAPI.Shipped.txt);
    // a string or a null string still assigns
    [Test]
    public void TypeWeight_FromNullString_IsNull()
    {
        string? none = null;
        TypeWeight? weight = none;
        Assert.That(weight, Is.Null);
    }

    // source: D:420 (GapSize = number | `${number}px`) and D:999 (rowLimit?: number | `${number}%`)
    [Test]
    public void ThemeLength_NumberAndText_RoundTrip()
    {
        ThemeLength px = 12;
        ThemeLength share = "40%";
        Assert.That(JsonSerializer.Serialize(px), Is.EqualTo("12"));
        Assert.That(JsonSerializer.Serialize(share), Is.EqualTo("\"40%\""));
        var read = JsonSerializer.Deserialize<ThemeLength>("\"8px\"")!;
        Assert.That((read.Number, read.Text), Is.EqualTo(((double?)null, "8px")));
        Assert.That(JsonSerializer.Deserialize<ThemeLength>("2.5")!.Number, Is.EqualTo(2.5));
    }

    // source: D:1025 (overflow?: LabelFitStep | LabelFitStep[]), D:989 (LabelFitStep)
    [Test]
    public void LabelOverflow_StepAndSteps_RoundTrip()
    {
        LabelOverflow step = "wrap";
        LabelOverflow steps = new[] { "wrap", "rotate90" };
        Assert.That(JsonSerializer.Serialize(step), Is.EqualTo("\"wrap\""));
        Assert.That(JsonSerializer.Serialize(steps), Is.EqualTo("""["wrap","rotate90"]"""));
        var read = JsonSerializer.Deserialize<LabelOverflow>("""["skip"]""")!;
        Assert.That((read.Step, read.Steps), Is.EqualTo(((string?)null, new[] { "skip" })));
        Assert.That(JsonSerializer.Deserialize<LabelOverflow>("\"rotate45\"")!.Step, Is.EqualTo("rotate45"));
    }

    // source: D:434-445 (RuleSpec = Presence | {presence?, style?, dash?, weight?, color?, layer?})
    [Test]
    public void RuleSpec_PresenceAndObject_RoundTrip()
    {
        RuleSpec bare = "quiet";
        Assert.That(JsonSerializer.Serialize(bare), Is.EqualTo("\"quiet\""));
        Assert.That(bare.Presence, Is.EqualTo("quiet"));
        var rule = new RuleSpec { Presence = "full", Style = "dashed", Dash = [4, 2], Weight = 1.5, Color = "#777777", Layer = "front" };
        JsonAssert.Equal("""{"color":"#777777","dash":[4,2],"layer":"front","presence":"full","style":"dashed","weight":1.5}""", JsonSerializer.Serialize(rule));
        var read = JsonSerializer.Deserialize<RuleSpec>("""{"presence":"quiet"}""")!;
        Assert.That(read.Presence, Is.EqualTo("quiet"));
        Assert.That(JsonSerializer.Serialize(read), Is.EqualTo("""{"presence":"quiet"}"""));
    }

    // source: D:447-452 (MinorGridSpec = Presence | (Exclude<RuleSpec, Presence> & {divisions?, on?}))
    [Test]
    public void MinorGridSpec_PresenceAndObject_RoundTrip()
    {
        MinorGridSpec bare = "hairline";
        Assert.That(JsonSerializer.Serialize(bare), Is.EqualTo("\"hairline\""));
        var minor = new MinorGridSpec { Presence = "quiet", Divisions = 4, On = ["measure"], Dash = [1, 3] };
        JsonAssert.Equal("""{"dash":[1,3],"divisions":4,"on":["measure"],"presence":"quiet"}""", JsonSerializer.Serialize(minor));
        var read = JsonSerializer.Deserialize<MinorGridSpec>("""{"divisions":5,"weight":0.5}""")!;
        Assert.That((read.Divisions, read.Weight), Is.EqualTo(((double?)5, (double?)0.5)));
    }

    // source: design §5.2 (unknown keys round-trip through Extensions and are written last)
    [Test]
    public void RuleSpec_UnknownKeys_RoundTripAfterKnownMembers()
    {
        var read = JsonSerializer.Deserialize<RuleSpec>("""{"future":true,"weight":2}""")!;
        Assert.That(read.Extensions, Is.Not.Null);
        Assert.That(JsonSerializer.Serialize(read), Is.EqualTo("""{"weight":2,"future":true}"""));
        var minor = JsonSerializer.Deserialize<MinorGridSpec>("""{"future":1,"divisions":2}""")!;
        Assert.That(JsonSerializer.Serialize(minor), Is.EqualTo("""{"divisions":2,"future":1}"""));
    }

    // source: design §5.1 (host-options guarantee; converters ignore the caller's options)
    [Test]
    public void Serialize_UnderHostileOptions_WritesTheSameDocument()
    {
        object[] values =
        [
            (TypeWeight)"bold", (TypeWeight)700, (ThemeLength)12, (ThemeLength)"40%", (LabelOverflow)new[] { "wrap" },
            new RuleSpec { Presence = "full", Weight = 2 }, new MinorGridSpec { Divisions = 2, On = ["category"] },
        ];
        foreach (var value in values)
        {
            Assert.That(JsonSerializer.Serialize(value, value.GetType(), HostileOptions.Create()), Is.EqualTo(JsonSerializer.Serialize(value, value.GetType())));
        }
    }

    // source: design §5.1 (a value of another shape is malformed input: JsonException)
    [TestCase(typeof(TypeWeight), "true")]
    [TestCase(typeof(TypeWeight), "{}")]
    [TestCase(typeof(ThemeLength), "[1]")]
    [TestCase(typeof(LabelOverflow), "1")]
    [TestCase(typeof(LabelOverflow), "[1]")]
    [TestCase(typeof(RuleSpec), "1")]
    [TestCase(typeof(MinorGridSpec), "[]")]
    public void Deserialize_OtherShapes_ThrowJsonException(Type type, string json)
    {
        Assert.That((Action)(() => JsonSerializer.Deserialize(json, type)), Throws.InstanceOf<JsonException>());
    }
}
