using System.Text.Json;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Serialization;

public sealed class SemanticAnnotationConverterTests
{
    private static SemanticAnnotation Read(string json) => JsonSerializer.Deserialize<SemanticAnnotation>(json)!;

    // source: d.ts T:2916-3027 (semantic_types: Record<string, string | SemanticAnnotation>) and design §5.2
    // (a trivial annotation is written as the string form)
    [Test]
    public void Write_SemanticTypeOnly_IsTheStringForm()
    {
        SemanticAnnotation annotation = SemanticType.YearMonth;
        Assert.That(JsonSerializer.Serialize(annotation), Is.EqualTo("\"YearMonth\""));
    }

    // source: d.ts T:612-644 (semanticType is the camelCase key; unit?) and design §10.4 test 9
    [Test]
    public void Write_WithUnit_IsTheObjectFormWithCamelCaseKey()
    {
        var annotation = new SemanticAnnotation { Type = SemanticType.Amount, Unit = "USD" };
        Assert.That(JsonSerializer.Serialize(annotation), Is.EqualTo("""{"semanticType":"Amount","unit":"USD"}"""));
    }

    // source: design §2 (an unregistered name is kept verbatim) and the 44-entry registry in ir/flint-ir.json
    [Test]
    public void Read_UnregisteredString_IsKeptVerbatimAndNotRegistered()
    {
        var annotation = Read("\"Revenue\"");
        Assert.That(annotation.Type.Name, Is.EqualTo("Revenue"));
        Assert.That(annotation.Type.IsRegistered, Is.False);
        Assert.That(JsonSerializer.Serialize(annotation), Is.EqualTo("\"Revenue\""));
    }

    // source: d.ts T:612-644 (intrinsicDomain?: [number, number]) and design §10.4 test 9
    [Test]
    public void Read_ObjectWithIntrinsicDomain_RoundTrips()
    {
        const string json = """{"semanticType":"Percentage","intrinsicDomain":[0,100]}""";
        var annotation = Read(json);
        Assert.That(annotation.Type, Is.EqualTo(SemanticType.Percentage));
        Assert.That(annotation.Type.IsRegistered, Is.True);
        Assert.That(annotation.IntrinsicDomain, Is.EqualTo(new[] { 0d, 100d }));
        Assert.That(JsonSerializer.Serialize(annotation), Is.EqualTo(json));
    }

    // source: d.ts T:612-644 (semanticType, intrinsicDomain?, unit?, divergingMidpoint?, sortOrder?) and design §5.2
    // (fixed declaration order)
    [Test]
    public void Object_WithEveryMember_RoundTripsInDeclarationOrder()
    {
        const string json = """{"semanticType":"Sentiment","intrinsicDomain":[-1,1],"unit":"score","divergingMidpoint":0,"sortOrder":["low","high"]}""";
        var annotation = Read(json);
        Assert.That(annotation.DivergingMidpoint, Is.EqualTo(0));
        Assert.That(annotation.SortOrder, Is.EqualTo(new[] { "low", "high" }));
        Assert.That(JsonSerializer.Serialize(annotation), Is.EqualTo(json));
    }

    // source: design §2 (semanticType is the only key read; semantic_type is read nowhere), so an object without it
    // is malformed
    [TestCase("""{"semantic_type":"Amount"}""", TestName = "Read_SnakeCaseKey_ThrowsJsonException")]
    [TestCase("""{"unit":"USD"}""", TestName = "Read_MissingSemanticType_ThrowsJsonException")]
    [TestCase("\"\"", TestName = "Read_EmptyString_ThrowsJsonException")]
    [TestCase("42", TestName = "Read_Number_ThrowsJsonException")]
    public void Read_Malformed_ThrowsJsonException(string json)
    {
        Assert.That((Action)(() => Read(json)), Throws.InstanceOf<JsonException>());
    }

    // source: flint-chart 1.1.1 src/core/field-semantics.ts:93 (divergingNeutral?: number | false) and :119
    // (percentScale?: 'fraction' | 'percent'), declared after divergingMidpoint and polarity; design §5.2 (fixed
    // declaration order)
    [Test]
    public void Object_With111Members_RoundTripsInDeclarationOrder()
    {
        const string json = """{"semanticType":"Percentage","divergingMidpoint":50,"divergingNeutral":2.5,"polarity":"higher-is-better","percentScale":"percent"}""";
        var annotation = Read(json);
        Assert.That(annotation.DivergingNeutral!.Width, Is.EqualTo(2.5));
        Assert.That(annotation.PercentScale, Is.EqualTo(PercentScale.Percent));
        Assert.That(annotation.Extensions, Is.Null);
        Assert.That(JsonSerializer.Serialize(annotation), Is.EqualTo(json));
    }

    // source: design §5.2 (the string form only when nothing but the type is set): each 1.1.1 member alone keeps the object form
    [Test]
    public void Write_Only111Member_IsTheObjectForm()
    {
        Assert.That(JsonSerializer.Serialize(new SemanticAnnotation { Type = SemanticType.Percentage, PercentScale = PercentScale.Fraction }),
            Is.EqualTo("""{"semanticType":"Percentage","percentScale":"fraction"}"""));
        Assert.That(JsonSerializer.Serialize(new SemanticAnnotation { Type = SemanticType.Profit, DivergingNeutral = DivergingNeutral.Break }),
            Is.EqualTo("""{"semanticType":"Profit","divergingNeutral":false}"""));
    }

    // source: flint-chart 1.1.1 src/core/resolve-semantics.ts:737-739 (a number >= 0 is the neutral half-width, a test of
    // `=== undefined` makes null a break like false) and the maintainer's decision (null reads as Break, which writes false;
    // a negative number is kept for the validator to report)
    [TestCase("false", null, true, "false")]
    [TestCase("null", null, true, "false")]
    [TestCase("0", 0d, false, "0")]
    [TestCase("0.1", 0.1, false, "0.1")]
    [TestCase("-0.1", -0.1, false, "-0.1")]
    public void Read_DivergingNeutral_KeepsTheNumberOrTheBreak(string value, double? width, bool isBreak, string written)
    {
        var annotation = Read($$"""{"semanticType":"Correlation","divergingNeutral":{{value}}}""");
        Assert.That(annotation.DivergingNeutral, Is.Not.Null);
        Assert.That((annotation.DivergingNeutral!.Width, annotation.DivergingNeutral.IsBreak), Is.EqualTo((width, isBreak)));
        Assert.That(JsonSerializer.Serialize(annotation), Is.EqualTo($$"""{"semanticType":"Correlation","divergingNeutral":{{written}}}"""));
    }

    // source: design §5.1 (a value of another type is malformed input: JsonException, numbers strict); flint-chart 1.1.1
    // src/core/field-semantics.ts:93, :119 declare only these types; CHANGELOG [Unreleased], Changed (modelling the members
    // makes off-type values throw on read, where they were kept in Extensions)
    [TestCase("""{"semanticType":"Percentage","percentScale":1}""")]
    [TestCase("""{"semanticType":"Percentage","percentScale":""}""")]
    [TestCase("""{"semanticType":"Percentage","percentScale":["fraction"]}""")]
    [TestCase("""{"semanticType":"Correlation","divergingNeutral":true}""")]
    [TestCase("""{"semanticType":"Correlation","divergingNeutral":"0.1"}""")]
    [TestCase("""{"semanticType":"Correlation","divergingNeutral":{}}""")]
    [TestCase("""{"semanticType":"Correlation","divergingNeutral":[0.1]}""")]
    public void Read_Off111Type_ThrowsJsonException(string json)
    {
        Assert.That((Action)(() => Read(json)), Throws.InstanceOf<JsonException>());
    }

    // source: CHANGELOG [Unreleased], Changed (before 1.1.1 the members were kept in Extensions): a value set there is
    // written as before and read back into the typed member
    [Test]
    public void Extensions_Holding111Members_ReadBackTyped()
    {
        var annotation = new SemanticAnnotation
        {
            Type = SemanticType.Percentage,
            Extensions = new()
            {
                ["percentScale"] = JsonDocument.Parse("\"fraction\"").RootElement.Clone(),
                ["divergingNeutral"] = JsonDocument.Parse("0.5").RootElement.Clone(),
            },
        };
        var json = JsonSerializer.Serialize(annotation);
        Assert.That(json, Is.EqualTo("""{"semanticType":"Percentage","percentScale":"fraction","divergingNeutral":0.5}"""));
        var read = Read(json);
        Assert.That((read.PercentScale, read.DivergingNeutral?.Width, read.Extensions), Is.EqualTo(((PercentScale?)PercentScale.Fraction, (double?)0.5, (Dictionary<string, JsonElement>?)null)));
    }

    // source: design §5.1 (converters ignore the caller's options)
    [Test]
    public void Write_UnderHostileOptions_IsIdentical()
    {
        var annotation = new SemanticAnnotation { Type = SemanticType.Amount, DivergingMidpoint = 0.5, IntrinsicDomain = [0, 1], DivergingNeutral = 0.25, PercentScale = PercentScale.Fraction };
        Assert.That(JsonSerializer.Serialize(annotation, HostileOptions.Create()),
            Is.EqualTo("""{"semanticType":"Amount","intrinsicDomain":[0,1],"divergingMidpoint":0.5,"divergingNeutral":0.25,"percentScale":"fraction"}"""));
    }
}
