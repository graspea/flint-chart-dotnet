using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Serialization;

/// <summary>A host source-generated context with every setting that would alter an options-based wire rule (design §5.1).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DictionaryKeyPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    IgnoreReadOnlyProperties = true,
    NumberHandling = JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(ChartAssemblyInput))]
internal sealed partial class HostileHostContext : JsonSerializerContext;

public sealed class FlintJsonTests
{
    private static ChartAssemblyInput SemanticContext0() => new()
    {
        Data = ChartData.FromJson(new JsonArray(new JsonObject { ["city"] = "Seattle", ["revenue"] = 501479 })),
        ChartSpec = new ChartSpec
        {
            ChartType = ChartType.BarChart,
            Encodings =
            {
                ["x"] = new ChartEncoding { Field = "city" },
                ["y"] = new ChartEncoding { Field = "revenue" },
            },
        },
    };

    // source: design §10.4 test 6 (Semantic Context #0 without annotations); wire names d.ts T:2916-3027, T:1994-2001;
    // absent members omitted: design §5.2
    [Test]
    public void Serialize_MinimalInput_EmitsFlintWireNamesAndOmitsAbsentMembers()
    {
        JsonAssert.Equal(
            """{"data":{"values":[{"city":"Seattle","revenue":501479}]},"chart_spec":{"chartType":"Bar Chart","encodings":{"x":{"field":"city"},"y":{"field":"revenue"}}}}""",
            FlintJson.Serialize(SemanticContext0()));
    }

    private static ChartAssemblyInput PolicySensitive() => new()
    {
        Data = ChartData.FromJson(new JsonArray(new JsonObject { ["fiscalYear"] = 2024, ["GDP per capita"] = 1.5, ["region"] = null })),
        SemanticTypes = new()
        {
            ["fiscalYear"] = new SemanticAnnotation { Type = new SemanticType("Year") },
            ["GDP per capita"] = new SemanticAnnotation { Type = new SemanticType("Amount"), Unit = "USD" },
        },
        ChartSpec = new ChartSpec
        {
            ChartType = ChartType.LineChart,
            Title = "GDP",
            Encodings = { ["x"] = "fiscalYear", ["y"] = "GDP per capita", ["strokeDash"] = "region" },
            BaseSize = new ChartSize(800, 400.5),
            ChartProperties = new() { ["logScale_x"] = true, ["strokeWidth"] = 1.5, ["interpolate"] = null },
        },
        Options = new AssembleOptions { MaxStretch = 1.5 },
        FieldDisplayNames = new() { ["GDP per capita"] = "GDP per capita (USD)", ["fiscalYear"] = "Fiscal year" },
    };

    // source: design §10.4 test 7 and §5.1 (keys a naming policy would alter stay verbatim; numbers stay numbers;
    // absent members stay absent; map null values are written); wire names d.ts T:2916-3027, T:612-644
    private const string PolicySensitiveJson =
        """
        {"data":{"values":[{"fiscalYear":2024,"GDP per capita":1.5,"region":null}]},
         "semantic_types":{"fiscalYear":"Year","GDP per capita":{"semanticType":"Amount","unit":"USD"}},
         "chart_spec":{"chartType":"Line Chart","title":"GDP",
           "encodings":{"x":{"field":"fiscalYear"},"y":{"field":"GDP per capita"},"strokeDash":{"field":"region"}},
           "baseSize":{"width":800,"height":400.5},
           "chartProperties":{"logScale_x":true,"strokeWidth":1.5,"interpolate":null}},
         "options":{"maxStretch":1.5},
         "field_display_names":{"GDP per capita":"GDP per capita (USD)","fiscalYear":"Fiscal year"}}
        """;

    // source: design §10.4 test 7 (hostile reflection options and a host source-generated context give the same
    // document as FlintJson.Serialize)
    [Test]
    public void Serialize_UnderHostileHostOptions_IsIdenticalToFlintJson()
    {
        var canonical = FlintJson.Serialize(PolicySensitive());
        JsonAssert.Equal(PolicySensitiveJson, canonical);
        Assert.That(JsonSerializer.Serialize(PolicySensitive(), HostileOptions.Create()), Is.EqualTo(canonical));
        Assert.That(JsonSerializer.Serialize(PolicySensitive(), HostileHostContext.Default.ChartAssemblyInput), Is.EqualTo(canonical));
    }

    // source: design §5.1 (reading under hostile host options is lossless)
    [Test]
    public void Deserialize_UnderHostileHostOptions_ReadsTheSameDocument()
    {
        var canonical = FlintJson.Serialize(PolicySensitive());
        Assert.That(FlintJson.Serialize(JsonSerializer.Deserialize<ChartAssemblyInput>(canonical, HostileOptions.Create())!), Is.EqualTo(canonical));
        Assert.That(FlintJson.Serialize(JsonSerializer.Deserialize(canonical, HostileHostContext.Default.ChartAssemblyInput)!), Is.EqualTo(canonical));
    }

    // source: design §5.1 (Options: no naming or key policy, the library's own resolver, read-only after construction)
    [Test]
    public void Options_AreReadOnlyAndCarryNoNamingPolicy()
    {
        var options = FlintJson.Options;
        Assert.That(options.IsReadOnly, Is.True);
        Assert.That(options.PropertyNamingPolicy, Is.Null);
        Assert.That(options.DictionaryKeyPolicy, Is.Null);
        Assert.That(JsonSerializer.Serialize(PolicySensitive(), options), Is.EqualTo(FlintJson.Serialize(PolicySensitive())));
    }

    // source: design §5.1 (every Serialize overload writes the same document; indented only adds whitespace)
    [Test]
    public void SerializeOverloads_WriteTheSameDocument()
    {
        var canonical = FlintJson.Serialize(PolicySensitive());
        var indented = FlintJson.Serialize(PolicySensitive(), indented: true);
        Assert.That(indented, Does.Contain("\n"));
        JsonAssert.Equal(canonical, indented);
        Assert.That(System.Text.Encoding.UTF8.GetString(FlintJson.SerializeToUtf8Bytes(PolicySensitive())), Is.EqualTo(canonical));
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            FlintJson.Serialize(writer, PolicySensitive());
        }

        Assert.That(System.Text.Encoding.UTF8.GetString(buffer.ToArray()), Is.EqualTo(canonical));
        Assert.That(FlintJson.SerializeToNode(PolicySensitive()).ToJsonString(), Is.EqualTo(canonical));
    }

    // source: design §5.1 (Deserialize reads the canonical document from text and from UTF-8)
    [Test]
    public void Deserialize_CanonicalDocument_RoundTrips()
    {
        var canonical = FlintJson.Serialize(PolicySensitive());
        Assert.That(FlintJson.Serialize(FlintJson.Deserialize(canonical)), Is.EqualTo(canonical));
        Assert.That(FlintJson.Serialize(FlintJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(canonical))), Is.EqualTo(canonical));
    }

    // source: design §5.1 (JsonException on malformed input and on a missing required member: chart_spec, chartType)
    [TestCase("""{"data":{"values":[]}""", TestName = "Deserialize_Malformed_ThrowsJsonException")]
    [TestCase("""{"data":{"values":[]}}""", TestName = "Deserialize_MissingChartSpec_ThrowsJsonException")]
    [TestCase("""{"data":{"values":[]},"chart_spec":{"encodings":{}}}""", TestName = "Deserialize_MissingChartType_ThrowsJsonException")]
    [TestCase("""{"data":{"values":[]},"chart_spec":{"chartType":"","encodings":{}}}""", TestName = "Deserialize_EmptyChartType_ThrowsJsonException")]
    [TestCase("null", TestName = "Deserialize_JsonNull_ThrowsJsonException")]
    public void Deserialize_InvalidDocument_ThrowsJsonException(string json)
    {
        Assert.That((Action)(() => FlintJson.Deserialize(json)), Throws.InstanceOf<JsonException>());
        Assert.That((Action)(() => FlintJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(json))), Throws.InstanceOf<JsonException>());
    }

    // source: design §4.1 (Encodings = new(): an absent member reads as {}) and probe "encodings missing" / "encodings
    // empty" (absent throws everywhere, {} only on Excel); the default is kept, so only an explicit null stays missing
    [Test]
    public void Deserialize_AbsentEncodings_ReadsAsEmpty_ExplicitNullStaysMissing()
    {
        var absent = FlintJson.Deserialize("""{"data":{"values":[{"a":1}]},"chart_spec":{"chartType":"Bar Chart"}}""");
        Assert.That(absent.ChartSpec.Encodings, Is.Empty);
        Assert.That(FlintJson.SerializeToNode(absent)["chart_spec"]!["encodings"]!.ToJsonString(), Is.EqualTo("{}"));
        Assert.That(Graspea.Flint.Validation.ChartValidator.Validate(absent).Issues.Select(i => i.Code), Has.Member("encodings_empty").And.No.Member("missing_encodings"));

        var explicitNull = FlintJson.Deserialize("""{"data":{"values":[{"a":1}]},"chart_spec":{"chartType":"Bar Chart","encodings":null}}""");
        Assert.That(explicitNull.ChartSpec.Encodings, Is.Null);
        Assert.That(Graspea.Flint.Validation.ChartValidator.Validate(explicitNull).Issues.Select(i => i.Code), Has.Member("missing_encodings"));
    }
}
