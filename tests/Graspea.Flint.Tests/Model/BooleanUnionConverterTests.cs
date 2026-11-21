using System.Text.Json;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Model;

// D: = the flint-chart 1.1.1 d.ts, dist/types-DFtJpR3x.d.ts. The members that hold these types (AssembleOptions and
// ThemeCompileDefaults sparseFill and titleWrap, InteractionSpec.defaults) are generated from flint-chart 1.1.1 on.
public sealed class BooleanUnionConverterTests
{
    // source: D:5734-5744 (sparseFill?: boolean | {share?: number; barCap?: number; pointCap?: number})
    [Test]
    public void SparseFill_BooleanAndObject_RoundTrip()
    {
        SparseFill on = true;
        Assert.That(JsonSerializer.Serialize(on), Is.EqualTo("true"));
        Assert.That(on.Enabled, Is.True);
        Assert.That(JsonSerializer.Serialize((SparseFill)false), Is.EqualTo("false"));
        var tuned = new SparseFill { Share = 0.9, BarCap = 160, PointCap = 300 };
        Assert.That(tuned.Enabled, Is.Null);
        JsonAssert.Equal("""{"barCap":160,"pointCap":300,"share":0.9}""", JsonSerializer.Serialize(tuned));
        var read = JsonSerializer.Deserialize<SparseFill>("""{"share":0.5}""")!;
        Assert.That((read.Enabled, read.Share, read.BarCap, read.PointCap), Is.EqualTo(((bool?)null, (double?)0.5, (double?)null, (double?)null)));
        Assert.That(JsonSerializer.Deserialize<SparseFill>("false")!.Enabled, Is.False);
        Assert.That(JsonSerializer.Deserialize<SparseFill>("true")!.Enabled, Is.True);
        Assert.That(JsonSerializer.Serialize(JsonSerializer.Deserialize<SparseFill>("{}")), Is.EqualTo("{}"), "the object form stays an object");
    }

    // source: D:5745-5755 (titleWrap?: boolean | {floor?: number; minWidth?: number; overhang?: number})
    [Test]
    public void TitleWrap_BooleanAndObject_RoundTrip()
    {
        TitleWrap on = true;
        Assert.That(JsonSerializer.Serialize(on), Is.EqualTo("true"));
        Assert.That(on.Enabled, Is.True);
        Assert.That(JsonSerializer.Serialize((TitleWrap)false), Is.EqualTo("false"));
        var tuned = new TitleWrap { Floor = 0.5, MinWidth = 280, Overhang = 1.2 };
        Assert.That(tuned.Enabled, Is.Null);
        JsonAssert.Equal("""{"floor":0.5,"minWidth":280,"overhang":1.2}""", JsonSerializer.Serialize(tuned));
        var read = JsonSerializer.Deserialize<TitleWrap>("""{"minWidth":300}""")!;
        Assert.That((read.Enabled, read.Floor, read.MinWidth, read.Overhang), Is.EqualTo(((bool?)null, (double?)null, (double?)300, (double?)null)));
        Assert.That(JsonSerializer.Deserialize<TitleWrap>("false")!.Enabled, Is.False);
        Assert.That(JsonSerializer.Serialize(JsonSerializer.Deserialize<TitleWrap>("{}")), Is.EqualTo("{}"), "the object form stays an object");
    }

    // source: D:3495-3506 (defaults?: InteractionDefaults; InteractionDefaults = boolean | {tooltip?: boolean; legend?: boolean})
    [Test]
    public void InteractionDefaults_BooleanAndObject_RoundTrip()
    {
        InteractionDefaults off = false;
        Assert.That(JsonSerializer.Serialize(off), Is.EqualTo("false"));
        Assert.That(off.Enabled, Is.False);
        Assert.That(JsonSerializer.Serialize((InteractionDefaults)true), Is.EqualTo("true"));
        var some = new InteractionDefaults { Tooltip = false, Legend = true };
        Assert.That(some.Enabled, Is.Null);
        JsonAssert.Equal("""{"legend":true,"tooltip":false}""", JsonSerializer.Serialize(some));
        var read = JsonSerializer.Deserialize<InteractionDefaults>("""{"tooltip":false}""")!;
        Assert.That((read.Enabled, read.Tooltip, read.Legend), Is.EqualTo(((bool?)null, (bool?)false, (bool?)null)));
        Assert.That(JsonSerializer.Deserialize<InteractionDefaults>("true")!.Enabled, Is.True);
        Assert.That(JsonSerializer.Serialize(JsonSerializer.Deserialize<InteractionDefaults>("{}")), Is.EqualTo("{}"), "the object form stays an object");
    }

    // source: design §5.2 (unknown keys round-trip through Extensions and are written last)
    [Test]
    public void ObjectForms_UnknownKeys_RoundTripAfterKnownMembers()
    {
        var fill = JsonSerializer.Deserialize<SparseFill>("""{"future":1,"share":0.8}""")!;
        Assert.That(fill.Extensions!["future"].GetInt32(), Is.EqualTo(1));
        Assert.That(JsonSerializer.Serialize(fill), Is.EqualTo("""{"share":0.8,"future":1}"""));
        Assert.That(JsonSerializer.Serialize(JsonSerializer.Deserialize<TitleWrap>("""{"future":[],"floor":0.5}""")), Is.EqualTo("""{"floor":0.5,"future":[]}"""));
        Assert.That(JsonSerializer.Serialize(JsonSerializer.Deserialize<InteractionDefaults>("""{"zoom":false,"tooltip":false}""")), Is.EqualTo("""{"tooltip":false,"zoom":false}"""));
    }

    // source: design §5.1 (host-options guarantee; converters ignore the caller's options)
    [Test]
    public void Serialize_UnderHostileOptions_WritesTheSameDocument()
    {
        object[] values =
        [
            (SparseFill)true, new SparseFill { Share = 0.85, BarCap = 200, PointCap = 360 }, (TitleWrap)false,
            new TitleWrap { Floor = 0.6, MinWidth = 320, Overhang = 1.15 }, (InteractionDefaults)false,
            new InteractionDefaults { Tooltip = false, Legend = false },
        ];
        foreach (var value in values)
        {
            Assert.That(JsonSerializer.Serialize(value, value.GetType(), HostileOptions.Create()), Is.EqualTo(JsonSerializer.Serialize(value, value.GetType())));
        }
    }

    // source: D:5740-5755 (the tuning members are numbers), D:3503-3506 (the toggles are booleans) and design §5.1 (numbers
    // strict, whatever the caller's options allow)
    [TestCase(typeof(SparseFill), """{"share":"0.9"}""")]
    [TestCase(typeof(SparseFill), """{"barCap":true}""")]
    [TestCase(typeof(TitleWrap), """{"minWidth":"320"}""")]
    [TestCase(typeof(TitleWrap), """{"overhang":"NaN"}""")]
    [TestCase(typeof(InteractionDefaults), """{"tooltip":"false"}""")]
    [TestCase(typeof(InteractionDefaults), """{"legend":0}""")]
    public void ObjectForms_MemberOfAnotherKind_ThrowsUnderAnyOptions(Type type, string json)
    {
        Assert.That((Action)(() => JsonSerializer.Deserialize(json, type)), Throws.InstanceOf<JsonException>());
        Assert.That((Action)(() => JsonSerializer.Deserialize(json, type, HostileOptions.Create())), Throws.InstanceOf<JsonException>());
    }

    // source: design §5.1 (a value of another shape is malformed input: JsonException); D:5740 and D:5751 (sparseFill and
    // titleWrap of AssembleOptions and ThemeCompileDefaults, so the message names the member without a parent), D:3500
    // (interaction_spec.defaults); the wording is the converters' own, pinned here
    [TestCase(typeof(SparseFill), "1", "sparseFill must be a boolean or an object.")]
    [TestCase(typeof(SparseFill), "\"on\"", "sparseFill must be a boolean or an object.")]
    [TestCase(typeof(SparseFill), "[]", "sparseFill must be a boolean or an object.")]
    [TestCase(typeof(TitleWrap), "0", "titleWrap must be a boolean or an object.")]
    [TestCase(typeof(TitleWrap), "[true]", "titleWrap must be a boolean or an object.")]
    [TestCase(typeof(InteractionDefaults), "\"off\"", "interaction_spec.defaults must be a boolean or an object.")]
    [TestCase(typeof(InteractionDefaults), "[]", "interaction_spec.defaults must be a boolean or an object.")]
    public void Deserialize_OtherShapes_ThrowJsonExceptionNamingTheMember(Type type, string json, string message)
    {
        Assert.That((Action)(() => JsonSerializer.Deserialize(json, type)), Throws.InstanceOf<JsonException>().With.Message.EqualTo(message));
    }
}
