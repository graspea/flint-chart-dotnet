using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Support;
using Graspea.Flint.Theming;

namespace Graspea.Flint.Tests.Generated;

[JsonSerializable(typeof(AssembleOptions))]
[JsonSerializable(typeof(ThemeSpec))]
internal sealed partial class HostContext : JsonSerializerContext;

/// <summary>Generated options and theming DTOs keep their wire shape under any host options (design §5.1, §7.4).</summary>
public sealed class GeneratedDtoTests
{
    private static AssembleOptions Options() => new()
    {
        AddTooltips = true,
        MaxStretch = 1.5,
        ContinuousMarkCrossSection = 12.5,
        FacetFixedPadding = new ChartSize(10, 20),
    };

    // source: d.ts T:3032-3300 (wire names) and design §5.2 (absent members are omitted)
    [Test]
    public void AssembleOptions_Serialize_WritesWireNamesAndOmitsAbsentMembers()
    {
        JsonAssert.Equal(
            """{"addTooltips":true,"continuousMarkCrossSection":12.5,"facetFixedPadding":{"width":10,"height":20},"maxStretch":1.5}""",
            JsonSerializer.Serialize(Options()));
    }

    // source: design §5.1 (host-options guarantee, including WriteAsString and a host source-generated context)
    [Test]
    public void AssembleOptions_UnderHostileOptions_IsIdentical()
    {
        const string expected = """{"addTooltips":true,"continuousMarkCrossSection":12.5,"facetFixedPadding":{"width":10,"height":20},"maxStretch":1.5}""";
        Assert.That(JsonSerializer.Serialize(Options(), HostileOptions.Create()), Is.EqualTo(expected));
        Assert.That(JsonSerializer.Serialize(Options(), HostContext.Default.AssembleOptions), Is.EqualTo(expected));
    }

    // source: design §4.1 (options.background stays reachable through Extensions) and §5.2 (extensions written last)
    [Test]
    public void AssembleOptions_UnknownMember_RoundTripsThroughExtensions()
    {
        const string json = """{"addTooltips":false,"background":"#fff"}""";
        var read = JsonSerializer.Deserialize<AssembleOptions>(json)!;
        Assert.That(read.Extensions!["background"].GetString(), Is.EqualTo("#fff"));
        Assert.That(JsonSerializer.Serialize(read), Is.EqualTo(json));
    }

    // source: design §4.1 (map keys are written verbatim under any DictionaryKeyPolicy; chartDefaults values pass through)
    [Test]
    public void ThemeSpec_ChartDefaults_KeepsKeysVerbatimUnderHostileOptions()
    {
        var spec = new ThemeSpec
        {
            ChartDefaults = new()
            {
                ["Line Chart"] = new() { ["showPoints"] = true, ["logScale_x"] = null, ["strokeWidth"] = 1.5 },
            },
        };
        const string expected = """{"chartDefaults":{"Line Chart":{"showPoints":true,"logScale_x":null,"strokeWidth":1.5}}}""";
        Assert.That(JsonSerializer.Serialize(spec), Is.EqualTo(expected));
        Assert.That(JsonSerializer.Serialize(spec, HostileOptions.Create()), Is.EqualTo(expected));
        Assert.That(JsonSerializer.Serialize(spec, HostContext.Default.ThemeSpec), Is.EqualTo(expected));
    }

    // source: design §4.1 (the verbatim-key converter reads every map back unchanged)
    [Test]
    public void ThemeSpec_ChartDefaults_ReadsBackUnderHostileOptions()
    {
        const string json = """{"chartDefaults":{"*":{"showPoints":true,"fiscalYear":null}}}""";
        var read = JsonSerializer.Deserialize<ThemeSpec>(json, HostileOptions.Create())!;
        Assert.That(read.ChartDefaults!["*"].Keys, Is.EqualTo(new[] { "showPoints", "fiscalYear" }));
        Assert.That(read.ChartDefaults["*"]["fiscalYear"], Is.Null);
        Assert.That(JsonNode.DeepEquals(read.ChartDefaults["*"]["showPoints"], JsonValue.Create(true)), Is.True);
        Assert.That(JsonSerializer.Serialize(read), Is.EqualTo(json));
    }

    // source: d.ts T:963-971 (TypeRole.size: SizeToken = string | number, mapped to JsonNode by overrides.json)
    [Test]
    public void TypeRole_Size_AcceptsBothWireForms()
    {
        var role = JsonSerializer.Deserialize<TypeRole>("""{"size":"text.100"}""")!;
        Assert.That(role.Size!.GetValue<string>(), Is.EqualTo("text.100"));
        Assert.That(JsonSerializer.Serialize(new TypeRole { Size = 12 }, HostileOptions.Create()), Is.EqualTo("""{"size":12}"""));
        // source: design §5.1 (the same guarantee under a host source-generated context) and §10.4 test 7
        var spec = new ThemeSpec { Type = new() { Annotation = new() { Size = 12 } } };
        Assert.That(JsonSerializer.Serialize(spec, HostContext.Default.ThemeSpec), Is.EqualTo("""{"type":{"annotation":{"size":12}}}"""));
    }

    // source: design §5.1 (map values are written through the library's own source-generated metadata, so every
    // map value type, of generated and hand-written members alike, must be resolvable whatever the caller's options are)
    [Test]
    public void EveryMapMember_SerialisesUnderHostileOptions()
    {
        var members = typeof(AssembleOptions).Assembly.GetExportedTypes()
            .SelectMany(t => t.GetProperties().Select(p => (Owner: t, Property: p)))
            .Where(m => m.Property.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType is { IsGenericType: true } c
                && c.GetGenericTypeDefinition() == typeof(VerbatimKeyDictionaryConverter<>))
            .ToList();
        Assert.That(members, Is.Not.Empty);
        foreach (var (owner, property) in members)
        {
            var map = (IDictionary)Activator.CreateInstance(property.PropertyType)!;
            map["key"] = Sample(property.PropertyType.GetGenericArguments()[1]);
            var instance = Owner(owner);
            property.SetValue(instance, map);
            Assert.That((Action)(() => JsonSerializer.Serialize(instance, owner, HostileOptions.Create())), Throws.Nothing, $"{owner.Name}.{property.Name}");
        }
    }

    private static object Owner(Type type) =>
        type == typeof(ChartSpec) ? new ChartSpec { ChartType = ChartType.LineChart }
        : type == typeof(ChartAssemblyInput) ? new ChartAssemblyInput { Data = ChartData.FromJson([]), ChartSpec = new() { ChartType = ChartType.LineChart } }
        : Activator.CreateInstance(type)!;

    private static object Sample(Type type) =>
        type == typeof(string) ? "value"
        : type == typeof(SemanticAnnotation) ? new SemanticAnnotation { Type = SemanticType.Amount }
        : type == typeof(EncodingValue) ? (EncodingValue)"field"
        : type == typeof(double) ? 1.0
        : type == typeof(bool) ? true
        : type == typeof(JsonNode) ? JsonValue.Create(1)
        : Activator.CreateInstance(type)!;
}
