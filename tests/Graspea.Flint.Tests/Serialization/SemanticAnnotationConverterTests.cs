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

    // source: design §5.1 (converters ignore the caller's options)
    [Test]
    public void Write_UnderHostileOptions_IsIdentical()
    {
        var annotation = new SemanticAnnotation { Type = SemanticType.Amount, DivergingMidpoint = 0.5, IntrinsicDomain = [0, 1] };
        Assert.That(JsonSerializer.Serialize(annotation, HostileOptions.Create()), Is.EqualTo("""{"semanticType":"Amount","intrinsicDomain":[0,1],"divergingMidpoint":0.5}"""));
    }
}
