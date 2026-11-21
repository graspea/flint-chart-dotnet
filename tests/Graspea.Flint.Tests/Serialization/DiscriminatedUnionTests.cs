using System.Text.Json;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Serialization;

/// <summary>
/// The shared logic of the generated discriminated union converters (<c>DiscriminatedUnion</c>, compiled into this project
/// because no union exists on the pinned flint-chart yet), over test-only types shaped as the options emitter writes them:
/// an abstract base with the discriminator, sealed derived classes, an unknown class and a source-generated context.
/// </summary>
public sealed class DiscriminatedUnionTests
{
    // source: docs/generator.md "Discriminated unions" (the converter picks the derived class by the discriminator; a member
    // the class does not model goes to its Extensions, the discriminator does not)
    [Test]
    public void Read_KnownDiscriminator_ReadsTheDerivedClass()
    {
        var shape = JsonSerializer.Deserialize<TestShape>("""{"radius":2,"kind":"circle","future":true}""");
        Assert.That(shape, Is.TypeOf<TestCircle>());
        var circle = (TestCircle)shape!;
        Assert.That(circle.Radius, Is.EqualTo(2));
        Assert.That(circle.Extensions!.Keys, Is.EqualTo(new[] { "future" }));
    }

    // source: docs/generator.md "Discriminated unions" (written through the source-generated context, the discriminator
    // first, Extensions last) and "Required nullable members" (a required nullable member is written as null when unset)
    [Test]
    public void Write_DerivedClass_WritesTheDiscriminatorFirstAndExtensionsLast()
    {
        var circle = JsonSerializer.Deserialize<TestShape>("""{"future":1,"radius":2,"kind":"circle"}""");
        Assert.That(JsonSerializer.Serialize(circle), Is.EqualTo("""{"kind":"circle","radius":2,"label":null,"future":1}"""));
        TestShape square = new TestSquare { Side = 3 };
        Assert.That(JsonSerializer.Serialize(square), Is.EqualTo("""{"kind":"square","side":3}"""));
    }

    // source: docs/generator.md "Discriminated unions" (a discriminator no derived class has reads as Unknown<Alias> with
    // every other member in Extensions, and writes back the discriminator, then Extensions)
    [Test]
    public void ReadWrite_UnknownDiscriminator_RoundTrips()
    {
        var shape = JsonSerializer.Deserialize<TestShape>("""{"sides":6,"kind":"hexagon","meta":{"a":[1]}}""");
        Assert.That(shape, Is.TypeOf<UnknownTestShape>());
        Assert.That(shape!.Kind, Is.EqualTo("hexagon"));
        Assert.That(((UnknownTestShape)shape).Extensions!.Keys, Is.EqualTo(new[] { "sides", "meta" }));
        Assert.That(JsonSerializer.Serialize(shape), Is.EqualTo("""{"kind":"hexagon","sides":6,"meta":{"a":[1]}}"""));
        var bare = JsonSerializer.Deserialize<TestShape>("""{"kind":"dot"}""")!;
        Assert.That(((UnknownTestShape)bare).Extensions, Is.Null);
        Assert.That(JsonSerializer.Serialize(bare), Is.EqualTo("""{"kind":"dot"}"""));
    }

    // source: RFC 8259 §4 (names within an object should be unique): the discriminator is written once, from the property
    [Test]
    public void Write_UnknownWithTheDiscriminatorInExtensions_WritesItOnce()
    {
        TestShape shape = new UnknownTestShape("hexagon") { Extensions = new() { ["kind"] = JsonDocument.Parse("\"other\"").RootElement } };
        Assert.That(JsonSerializer.Serialize(shape), Is.EqualTo("""{"kind":"hexagon"}"""));
    }

    // source: design §5.1 (a value of another shape is malformed input: JsonException, with the path of the member it is in)
    [TestCase("""[1]""")]
    [TestCase("""1""")]
    [TestCase("""
        "circle"
        """)]
    [TestCase("""{"radius":2}""")]
    [TestCase("""{"kind":1}""")]
    [TestCase("""{"kind":null}""")]
    public void Read_OtherShapes_ThrowJsonExceptionWithTheMemberPath(string json)
    {
        var e = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TestShapeHolder>($$"""{"shapes":[{"kind":"square"},{{json}}]}"""));
        Assert.That(e!.Path, Is.EqualTo("$.shapes[1]"));
    }

    // source: design §5.1 ([JsonNumberHandling(Strict)] per type: a number written as a string is refused)
    [Test]
    public void Read_NumberAsString_Throws()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TestShape>("""{"kind":"circle","radius":"2"}"""));
    }

    // source: design §5.1 (host-options guarantee: the converter ignores the caller's options, so a snake_case policy,
    // nulls written, IgnoreReadOnlyProperties and numbers as strings change nothing)
    [Test]
    public void Serialize_UnderHostileOptions_WritesTheSameDocument()
    {
        TestShape[] values =
        [
            new TestCircle { Radius = 2, Label = "r" },
            new TestSquare { Side = 3 },
            new UnknownTestShape("hexagon") { Extensions = new() { ["sides"] = JsonDocument.Parse("6").RootElement } },
        ];
        foreach (var value in values)
        {
            Assert.That(JsonSerializer.Serialize(value, HostileOptions.Create()), Is.EqualTo(JsonSerializer.Serialize(value)));
        }

        var read = JsonSerializer.Deserialize<TestShape>("""{"kind":"circle","radius":2}""", HostileOptions.Create());
        Assert.That(((TestCircle)read!).Radius, Is.EqualTo(2));
    }

    // source: docs/generator.md "Discriminated unions" (the guarantee holds for a value typed as the base: System.Text.Json
    // reads [JsonConverter] from the declared type only, so a derived class handled as its own type goes through the caller's
    // options; its get-only discriminator carries JsonIgnoreCondition.Never, so IgnoreReadOnlyProperties keeps it, and it is
    // read without checking it)
    [Test]
    public void DerivedClassAsItsOwnType_BypassesTheConverter()
    {
        var square = new TestSquare { Side = 3 };
        Assert.That(JsonSerializer.Serialize<TestShape>(square, HostileOptions.Create()), Is.EqualTo("""{"kind":"square","side":3}"""));
        Assert.That(JsonSerializer.Serialize(square, HostileOptions.Create()), Is.EqualTo("""{"kind":"square","side":3}"""));
        Assert.That(JsonSerializer.Serialize(new UnknownTestShape("hexagon"), HostileOptions.Create()), Is.EqualTo("""{"kind":"hexagon"}"""));
        var read = JsonSerializer.Deserialize<TestSquare>("""{"kind":"circle","side":3}""");
        Assert.That(read!.Kind, Is.EqualTo("square"));
        Assert.That(read.Extensions, Is.Null);
    }
}

/// <summary>A test union told apart by <c>kind</c>, shaped as the options emitter writes a discriminated union.</summary>
[JsonConverter(typeof(TestShapeConverter))]
public abstract class TestShape
{
    private protected TestShape()
    {
    }

    [JsonPropertyName("kind")]
    public abstract string Kind { get; }
}

[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed class TestCircle : TestShape
{
    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public override string Kind => "circle";

    [JsonPropertyName("radius")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Radius { get; set; }

    [JsonPropertyName("label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Label { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed class TestSquare : TestShape
{
    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public override string Kind => "square";

    [JsonPropertyName("side")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Side { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

public sealed class UnknownTestShape : TestShape
{
    public UnknownTestShape(string kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        Kind = kind;
    }

    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public override string Kind { get; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

public sealed class TestShapeHolder
{
    [JsonPropertyName("shapes")]
    public TestShape[]? Shapes { get; set; }
}

public sealed class TestShapeConverter : JsonConverter<TestShape>
{
    public override TestShape Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DiscriminatedUnion.Read<TestShape>(ref reader, "kind", static kind => kind switch
        {
            "circle" => TestUnionJsonContext.Default.TestCircle,
            "square" => TestUnionJsonContext.Default.TestSquare,
            _ => null,
        }, static (kind, extensions) => new UnknownTestShape(kind) { Extensions = extensions });

    public override void Write(Utf8JsonWriter writer, TestShape value, JsonSerializerOptions options)
    {
        if (value is UnknownTestShape unknown)
        {
            DiscriminatedUnion.WriteUnknown(writer, "kind", unknown.Kind, unknown.Extensions);
        }
        else
        {
            DiscriminatedUnion.Write(writer, value, TestUnionJsonContext.Default);
        }
    }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, NumberHandling = JsonNumberHandling.Strict)]
[JsonSerializable(typeof(TestCircle))]
[JsonSerializable(typeof(TestSquare))]
internal sealed partial class TestUnionJsonContext : JsonSerializerContext;
