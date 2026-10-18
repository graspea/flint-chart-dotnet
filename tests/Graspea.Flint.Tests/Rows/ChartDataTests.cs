using System.Text.Json;
using System.Text.Json.Nodes;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Rows;

public sealed record Sale(DateOnly Month, decimal Revenue, string? Region);

public sealed record Row<T>(T Value);

public sealed record Point(double X, double Y);

public enum Region
{
    NorthEast,
    South,
}

public sealed partial class ChartDataTests
{
    // source: design §10.4 test 10 (camelCase by default, decimal written exactly, null written) and §5.3
    [Test]
    public void FromRows_Record_WritesCamelCaseKeysAndExplicitNulls()
    {
        var data = ChartData.FromRows([new Sale(new DateOnly(2024, 1, 1), 1200.50m, null)]);
        Assert.That(data.Values!.ToJsonString(), Is.EqualTo("""[{"month":"2024-01-01","revenue":1200.50,"region":null}]"""));
        Assert.That(data.FirstRowKeys, Is.EqualTo(new[] { "month", "revenue", "region" }));
        Assert.That(data.Url, Is.Null);
    }

    // source: design §5.3 (verbatim names through new RowOptions { PropertyNamingPolicy = null })
    [Test]
    public void FromRows_WithoutNamingPolicy_WritesMemberNamesVerbatim()
    {
        var data = ChartData.FromRows([new Sale(new DateOnly(2024, 1, 1), 1, "EU")], new RowOptions { PropertyNamingPolicy = null });
        Assert.That(data.FirstRowKeys, Is.EqualTo(new[] { "Month", "Revenue", "Region" }));
    }

    // source: design §4.1 (RowOptions.Default is camelCase)
    [Test]
    public void RowOptions_Default_IsCamelCase()
    {
        Assert.That(RowOptions.Default.PropertyNamingPolicy, Is.SameAs(JsonNamingPolicy.CamelCase));
        Assert.That(new RowOptions().PropertyNamingPolicy, Is.SameAs(JsonNamingPolicy.CamelCase));
    }

    // source: design §4.1 (FirstRowKeys is empty when there are no rows or the first row is not an object)
    [Test]
    public void FirstRowKeys_NoObjectFirstRow_IsEmpty()
    {
        Assert.That(ChartData.FromJson([]).FirstRowKeys, Is.Empty);
        Assert.That(ChartData.FromJson([1, 2]).FirstRowKeys, Is.Empty);
        Assert.That(ChartData.FromUrl("https://example.org/data.json").FirstRowKeys, Is.Empty);
    }

    // source: ECMA-262 §10.1.11.1 OrdinaryOwnPropertyKeys (Object.keys lists array-index keys first, ascending, then
    // string keys in insertion order) and design §4.1 (FirstRowKeys is what Object.keys(data[0]) sees)
    [Test]
    public void FirstRowKeys_IntegerLikeKeys_FollowObjectKeysOrder()
    {
        var data = ChartData.FromJson([new JsonObject { ["b"] = 1, ["10"] = 2, ["2"] = 3, ["01"] = 4, ["a"] = 5 }]);
        Assert.That(data.FirstRowKeys, Is.EqualTo(new[] { "2", "10", "b", "01", "a" }));
    }

    // source: d.ts T:2916-3027 (data: {values} | {url}) and design §5.1 table
    [Test]
    public void FromUrl_WritesTheUrlForm()
    {
        var data = ChartData.FromUrl("https://example.org/data.json");
        Assert.That(data.Values, Is.Null);
        Assert.That(JsonSerializer.Serialize(data), Is.EqualTo("""{"url":"https://example.org/data.json"}"""));
    }

    // source: design §5.1 table (whichever is set; both if both present) and §5.2 (extensions written last)
    [TestCase("""{"values":[{"a":1}]}""", TestName = "ReadWrite_Values_RoundTrips")]
    [TestCase("""{"url":"u"}""", TestName = "ReadWrite_Url_RoundTrips")]
    [TestCase("""{"values":[],"url":"u"}""", TestName = "ReadWrite_ValuesAndUrl_RoundTrips")]
    [TestCase("""{"values":[{"a":null}],"format":{"type":"csv"}}""", TestName = "ReadWrite_UnknownMember_RoundTripsLast")]
    public void ReadWrite_EveryForm_RoundTrips(string json)
    {
        var data = JsonSerializer.Deserialize<ChartData>(json)!;
        Assert.That(JsonSerializer.Serialize(data), Is.EqualTo(json));
        Assert.That(JsonSerializer.Serialize(data, HostileOptions.Create()), Is.EqualTo(json));
    }

    // source: design §5.1 (malformed input is a JsonException)
    [TestCase("""{"values":{}}""", TestName = "Read_ValuesNotArray_ThrowsJsonException")]
    [TestCase("""{"url":1}""", TestName = "Read_UrlNotString_ThrowsJsonException")]
    [TestCase("[]", TestName = "Read_Array_ThrowsJsonException")]
    public void Read_Malformed_ThrowsJsonException(string json)
    {
        Assert.That((Action)(() => JsonSerializer.Deserialize<ChartData>(json)), Throws.InstanceOf<JsonException>());
    }

    private static TestCaseData Value<T>(string label, T value, string expectedValue) =>
        new TestCaseData((Func<ChartData>)(() => ChartData.FromRows([new Row<T>(value)])), $$"""[{"value":{{expectedValue}}}]""")
            .SetArgDisplayNames(label);

    public static IEnumerable<TestCaseData> DocumentedValues()
    {
        var offset = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.FromHours(2));
        var local = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Local);
        yield return Value("bool", true, "true");
        yield return Value("byte", (byte)255, "255");
        yield return Value("sbyte", (sbyte)-128, "-128");
        yield return Value("short", (short)-32768, "-32768");
        yield return Value("ushort", (ushort)65535, "65535");
        yield return Value("int", int.MinValue, "-2147483648");
        yield return Value("uint", uint.MaxValue, "4294967295");
        yield return Value("long 2^53+1", 9007199254740993L, "9007199254740993");
        yield return Value("ulong max", ulong.MaxValue, "18446744073709551615");
        yield return Value("double", 0.1, "0.1");
        yield return Value("float", 1.5f, "1.5");
        yield return Value("double NaN", double.NaN, "null");
        yield return Value("double +Infinity", double.PositiveInfinity, "null");
        yield return Value("double -Infinity", double.NegativeInfinity, "null");
        yield return Value("float +Infinity", float.PositiveInfinity, "null");
        yield return Value("float NaN", float.NaN, "null");
        yield return Value("nullable double NaN", (double?)double.NaN, "null");
        yield return Value("decimal", 1200.50m, "1200.50");
        yield return Value("DateOnly", new DateOnly(2024, 1, 15), "\"2024-01-15\"");
        yield return Value("DateTimeOffset", offset, "\"2024-01-15T10:00:00+02:00\"");
        yield return Value("DateTime Utc", new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc), "\"2024-01-15T10:00:00Z\"");
        yield return Value("DateTime Local", local, $"\"2024-01-15T10:00:00{new DateTimeOffset(local):zzz}\"");
        yield return Value("DateTime Unspecified", new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Unspecified), "\"2024-01-15T10:00:00\"");
        yield return Value("TimeOnly", new TimeOnly(10, 0), "\"10:00:00\"");
        yield return Value("TimeSpan", new TimeSpan(1, 2, 3, 4), "\"1.02:03:04\"");
        yield return Value("enum", Region.NorthEast, "\"NorthEast\"");
        yield return Value("string", "EU", "\"EU\"");
        yield return Value("Guid", new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"), "\"0f8fad5b-d9cb-469f-a165-70867728950e\"");
        yield return Value("Uri", new Uri("https://example.org/a"), "\"https://example.org/a\"");
        yield return Value<string?>("null", null, "null");
        yield return Value<int?>("Nullable<int> null", null, "null");
        yield return Value("nested object", new Point(1, 2.5), """{"x":1,"y":2.5}""");
        yield return Value("collection", new[] { 1, 2 }, "[1,2]");
        yield return Value("JsonElement", JsonDocument.Parse("""{"K":[1,"a"]}""").RootElement, """{"K":[1,"a"]}""");
        yield return Value<JsonNode>("JsonNode", new JsonObject { ["K"] = 1.25 }, """{"K":1.25}""");
    }

    // source: design §5.3 ".NET type -> JSON value" table, one case per row (STJ defaults; NaN/Infinity -> null;
    // enums as names; nulls always written); design §10.4 test 11
    [TestCaseSource(nameof(DocumentedValues))]
    public void FromRows_ValueTypes_WriteDocumentedJson(Func<ChartData> create, string expected)
    {
        // Rendered with the relaxed encoder so that "+02:00" reads as written; the default encoder's "\u002B" is the
        // same JSON string. Numbers are compared as text, so decimal scale and large integers are checked exactly.
        Assert.That(create().Values!.ToJsonString(Relaxed), Is.EqualTo(expected));
    }

    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}
