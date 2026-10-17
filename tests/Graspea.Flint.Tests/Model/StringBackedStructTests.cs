using System.Text.Json;

namespace Graspea.Flint.Tests.Model;

/// <summary>The eight string-backed structs share one contract (design §4.1 "String-backed structs", §5.1).</summary>
public sealed class StringBackedStructTests
{
    public sealed record StructCase(Type Type, Func<string, object> Create, Func<object, string?> Name)
    {
        public override string ToString() => Type.Name;
    }

    public static IEnumerable<TestCaseData> Cases()
    {
        yield return Case(n => new ChartType(n), v => ((ChartType)v).Name);
        yield return Case(n => new Channel(n), v => ((Channel)v).Name);
        yield return Case(n => new SemanticType(n), v => ((SemanticType)v).Name);
        yield return Case(n => new ThemePreset(n), v => ((ThemePreset)v).Name);
        yield return Case(n => new EncodingType(n), v => ((EncodingType)v).Name);
        yield return Case(n => new Aggregate(n), v => ((Aggregate)v).Name);
        yield return Case(n => new SortOrder(n), v => ((SortOrder)v).Name);
        yield return Case(n => new SeriesCountAxis(n), v => ((SeriesCountAxis)v).Name);
    }

    private static TestCaseData Case<T>(Func<string, T> create, Func<object, string?> name) where T : struct =>
        new TestCaseData(new StructCase(typeof(T), n => create(n), name)).SetArgDisplayNames(typeof(T).Name);

    // source: design §4.1 (the constructor throws ArgumentException on a null or empty string)
    [TestCaseSource(nameof(Cases))]
    public void Constructor_NullOrEmpty_ThrowsArgumentException(StructCase c)
    {
        Assert.That((Action)(() => c.Create("")), Throws.InstanceOf<ArgumentException>());
        Assert.That((Action)(() => c.Create(null!)), Throws.InstanceOf<ArgumentException>());
    }

    // source: design §4.1 (explicit constructor and a get-only property, so `with { Name = null! }` cannot compile)
    [TestCaseSource(nameof(Cases))]
    public void Name_IsGetOnly(StructCase c)
    {
        var name = c.Type.GetProperty("Name");
        Assert.That(name, Is.Not.Null);
        Assert.That(name!.SetMethod, Is.Null);
    }

    // source: design §4.1 (the converter writes the string verbatim; ToString() => Name)
    [TestCaseSource(nameof(Cases))]
    public void Serialize_Value_WritesTheStringVerbatim(StructCase c)
    {
        var value = c.Create("Image Chart");
        Assert.That(JsonSerializer.Serialize(value, c.Type), Is.EqualTo("\"Image Chart\""));
        Assert.That(value.ToString(), Is.EqualTo("Image Chart"));
    }

    // source: design §4.1 (an unknown value round-trips untouched; structs compare by value)
    [TestCaseSource(nameof(Cases))]
    public void Deserialize_UnknownValue_RoundTripsVerbatim(StructCase c)
    {
        var value = JsonSerializer.Deserialize("\"Some Future Value\"", c.Type);
        Assert.That(value, Is.EqualTo(c.Create("Some Future Value")));
        Assert.That(c.Name(value!), Is.EqualTo("Some Future Value"));
    }

    // source: design §5.1 (reading "" throws JsonException; the struct's ArgumentException is wrapped)
    [TestCaseSource(nameof(Cases))]
    public void Deserialize_EmptyString_ThrowsJsonExceptionWrappingArgumentException(StructCase c)
    {
        Assert.That((Action)(() => JsonSerializer.Deserialize("\"\"", c.Type)),
            Throws.InstanceOf<JsonException>().With.InnerException.InstanceOf<ArgumentException>());
    }

    // source: design §4.1 and §10.4 test 9 (reading 42 throws JsonException)
    [TestCaseSource(nameof(Cases))]
    public void Deserialize_NonString_ThrowsJsonException(StructCase c)
    {
        Assert.That((Action)(() => JsonSerializer.Deserialize("42", c.Type)), Throws.InstanceOf<JsonException>());
        Assert.That((Action)(() => JsonSerializer.Deserialize("null", c.Type)), Throws.InstanceOf<JsonException>());
    }

    // source: design §4.1 (default(T) cannot be serialised; the converter throws JsonException)
    [TestCaseSource(nameof(Cases))]
    public void Serialize_Default_ThrowsJsonException(StructCase c)
    {
        Assert.That((Action)(() => JsonSerializer.Serialize(Activator.CreateInstance(c.Type), c.Type)), Throws.InstanceOf<JsonException>());
    }

    // source: design §4.1 ({"type":null} reads as null for a nullable member)
    [Test]
    public void Deserialize_NullIntoNullable_IsNull()
    {
        Assert.That(JsonSerializer.Deserialize<EncodingType?>("null"), Is.Null);
    }

    // source: design §4.1 (Channel has an implicit operator to string)
    [Test]
    public void Channel_ConvertsImplicitlyToString()
    {
        string name = new Channel("strokeDash");
        Assert.That(name, Is.EqualTo("strokeDash"));
    }
}
