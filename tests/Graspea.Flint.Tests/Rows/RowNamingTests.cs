using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Graspea.Flint.Tests.Rows;

public class BaseRow
{
    public string? Region { get; set; }
}

public sealed class DerivedRow : BaseRow
{
    [JsonPropertyName("GDP per capita")]
    public double Gdp { get; set; }

    public int Value { get; set; }

    [JsonIgnore]
    public int Hidden { get; set; }
}

public sealed record GdpRow([property: JsonPropertyName("GDP per capita")] double Gdp, string Country);

public sealed record AlwaysWrittenRow([property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Note, [property: JsonIgnore] int Hidden);

public sealed record NullDroppingRow([property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note, int Value);

public sealed record NoteRow(string City, string? Note);

/// <summary>A host source-generated context; on net8 it leaves JsonPropertyInfo.AttributeProvider null (design §5.3).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DerivedRow))]
[JsonSerializable(typeof(GdpRow))]
[JsonSerializable(typeof(NullDroppingRow))]
[JsonSerializable(typeof(AlwaysWrittenRow))]
internal sealed partial class RowContext : JsonSerializerContext;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GdpRow))]
internal sealed partial class NullDroppingContext : JsonSerializerContext;

public sealed class RowNamingTests
{
    private static DerivedRow Derived() => new() { Region = "EU", Gdp = 1.5, Value = 2, Hidden = 3 };

    /// <summary>The field a selector resolves to through the production path: the builder's Encode (design §5.3 rule 1).</summary>
    private static string FieldName<TRow>(Expression<Func<TRow, object?>> selector, RowOptions? options = null) =>
        Chart.Of(ChartType.BarChart, Array.Empty<TRow>(), options).Encode(Channel.X, selector).Build().ChartSpec.Encodings["x"].Encodings[0].Field!;

    private static string FieldName<TRow>(JsonTypeInfo<TRow> typeInfo, Expression<Func<TRow, object?>> selector) =>
        Chart.Of(ChartType.BarChart, Array.Empty<TRow>(), typeInfo).Encode(Channel.X, selector).Build().ChartSpec.Encodings["x"].Encodings[0].Field!;

    // source: design §4.1 decision table ("[JsonPropertyName] honoured") and §5.3 (camelCase otherwise)
    [Test]
    public void FromRows_JsonPropertyNameOnRowType_IsUsedAsKey()
    {
        Assert.That(ChartData.FromRows([new GdpRow(1.5, "CZ")]).FirstRowKeys, Is.EqualTo(new[] { "GDP per capita", "country" }));
        Assert.That(ChartData.FromRows([Derived()]).FirstRowKeys, Is.EquivalentTo(new[] { "GDP per capita", "value", "region" }));
    }

    // source: design §5.3 (the JsonTypeInfo overload writes rows through the caller's metadata)
    [Test]
    public void FromRows_SourceGeneratedTypeInfo_WritesTheSameKeys()
    {
        var data = ChartData.FromRows([new GdpRow(1.5, "CZ")], RowContext.Default.GdpRow);
        Assert.That(data.Values!.ToJsonString(), Is.EqualTo("""[{"GDP per capita":1.5,"country":"CZ"}]"""));
    }

    // source: design §5.3 rule 1 (the name comes from STJ metadata: [JsonPropertyName], a positional record's
    // [property:] attribute, inherited members and the naming policy, exactly as the rows are written)
    [Test]
    public void FieldName_Reflection_ResolvesLikeTheRows()
    {
        Assert.That(FieldName<DerivedRow>(r => r.Gdp), Is.EqualTo("GDP per capita"));
        Assert.That(FieldName<DerivedRow>(r => r.Value), Is.EqualTo("value"));
        Assert.That(FieldName<DerivedRow>(r => r.Region), Is.EqualTo("region"));
        Assert.That(FieldName<GdpRow>(r => r.Gdp), Is.EqualTo("GDP per capita"));
        Assert.That(FieldName<GdpRow>(r => r.Country, new RowOptions { PropertyNamingPolicy = null }), Is.EqualTo("Country"));
    }

    // source: design §10.4 test 12 and §5.3 rule 1 (a source-generated JsonTypeInfo; on net8 AttributeProvider is null
    // and the MemberInfo fallback reads [JsonPropertyName], including on an inherited member)
    [Test]
    public void FromRows_SourceGeneratedTypeInfo_ResolvesLambdaName()
    {
        Assert.That(FieldName(RowContext.Default.DerivedRow, r => r.Gdp), Is.EqualTo("GDP per capita"));
        Assert.That(FieldName(RowContext.Default.DerivedRow, r => r.Value), Is.EqualTo("value"));
        Assert.That(FieldName(RowContext.Default.DerivedRow, r => r.Region), Is.EqualTo("region"));
        Assert.That(FieldName(RowContext.Default.GdpRow, r => r.Gdp), Is.EqualTo("GDP per capita"));
        Assert.That(FieldName(RowContext.Default.GdpRow, r => r.Country), Is.EqualTo("country"));
        Assert.That(ChartData.FromRows([Derived()], RowContext.Default.DerivedRow).FirstRowKeys,
            Does.Contain(FieldName(RowContext.Default.DerivedRow, r => r.Gdp)));
    }

    // source: design §5.3 rule 1 (anything but a member access on the row throws ArgumentException; a member that is
    // not serialised throws)
    [Test]
    public void FieldName_NotASerialisedMember_ThrowsArgumentException()
    {
        Assert.That((Action)(() => FieldName<DerivedRow>(r => r.Region!.Length)), Throws.ArgumentException);
        Assert.That((Action)(() => FieldName<DerivedRow>(r => r.Value + 1)), Throws.ArgumentException);
        Assert.That((Action)(() => FieldName<DerivedRow>(r => r.Hidden)), Throws.ArgumentException);
        Assert.That((Action)(() => FieldName(RowContext.Default.DerivedRow, r => r.Hidden)), Throws.ArgumentException);
        var other = Derived();
        Assert.That((Action)(() => FieldName<DerivedRow>(r => other.Value)), Throws.ArgumentException);
    }

    // source: design §5.3 (null guarantee: a JsonTypeInfo whose DefaultIgnoreCondition is not Never, or with a
    // conditional property, throws ArgumentException)
    [Test]
    public void FromRows_TypeInfoThatDropsNulls_Throws()
    {
        Assert.That((Action)(() => ChartData.FromRows([new GdpRow(1, "CZ")], NullDroppingContext.Default.GdpRow)), Throws.ArgumentException);
        Assert.That((Action)(() => ChartData.FromRows([new NullDroppingRow(null, 1)], RowContext.Default.NullDroppingRow)), Throws.ArgumentException);
    }

    // source: review R1-2 (a contract modifier whose predicate depends on the value: Note is written only when null, so
    // data[0] has no Note key although a probe with a null value passes)
    [Test]
    public void FromRows_ValueDependentShouldSerialize_Throws()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            foreach (var property in info.Properties.Where(p => p.Name == "Note"))
            {
                property.ShouldSerialize = (_, value) => value is null;
            }
        });
        var options = new JsonSerializerOptions { TypeInfoResolver = resolver };
        var typeInfo = (JsonTypeInfo<NoteRow>)options.GetTypeInfo(typeof(NoteRow));

        Assert.That((Action)(() => ChartData.FromRows([new NoteRow("A", "x"), new NoteRow("A", null)], typeInfo)),
            Throws.ArgumentException.With.Message.Contains("'Note'"));
        Assert.That((Action)(() => ChartData.FromRows(new object[] { new NoteRow("A", "x") }, (JsonTypeInfo<object>)options.GetTypeInfo(typeof(object)))),
            Throws.ArgumentException.With.Message.Contains("'Note'"));
    }

    // source: review R1-1 (numbers written as strings are read as nominal or temporal upstream, not quantitative)
    [Test]
    public void FromRows_TypeInfoWritingNumbersAsStrings_Throws()
    {
        var options = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.WriteAsString, TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        var typeInfo = (JsonTypeInfo<GdpRow>)options.GetTypeInfo(typeof(GdpRow));

        Assert.That((Action)(() => ChartData.FromRows([new GdpRow(1, "CZ")], typeInfo)), Throws.ArgumentException.With.Message.Contains("WriteAsString"));
    }

    // source: design §5.3 (the same check on the reflection RowOptions path)
    [Test]
    public void FromRows_RowOptionsWithNullDroppingRow_Throws()
    {
        Assert.That((Action)(() => ChartData.FromRows([new NullDroppingRow(null, 1)])), Throws.ArgumentException);
        Assert.That((Action)(() => ChartData.FromRows([new NullDroppingRow(null, 1)], new RowOptions { PropertyNamingPolicy = null })), Throws.ArgumentException);
    }

    // source: design §5.3 (null guarantee: rows typed as object are written by their runtime type, so the check runs on
    // that type; a dropped key of data[0] breaks key detection the same way)
    [Test]
    public void FromRows_ObjectRowsWithNullDroppingRuntimeType_Throws()
    {
        Assert.That((Action)(() => ChartData.FromRows(new object[] { new NullDroppingRow(null, 1) })), Throws.ArgumentException);
        Assert.That((Action)(() => ChartData.FromRows(new List<object> { new GdpRow(1, "CZ"), new NullDroppingRow(null, 1) })), Throws.ArgumentException);
        Assert.That(ChartData.FromRows(new object[] { new AlwaysWrittenRow(null, 1) }).Values!.ToJsonString(), Is.EqualTo("""[{"note":null}]"""));
    }

    // source: design §5.3 (the null guarantee is about keys that vary between rows: a member marked Never is always
    // written and one ignored always is never a key, so neither can hide a key of data[0])
    [Test]
    public void FromRows_NeverOrAlwaysIgnoredMember_IsAccepted()
    {
        const string expected = """[{"note":null}]""";
        Assert.That(ChartData.FromRows([new AlwaysWrittenRow(null, 1)]).Values!.ToJsonString(), Is.EqualTo(expected));
        Assert.That(ChartData.FromRows([new AlwaysWrittenRow(null, 1)], RowContext.Default.AlwaysWrittenRow).Values!.ToJsonString(), Is.EqualTo(expected));
    }

    // source: design §5.3 rule 3 (dictionary rows: keys literal, no key policy)
    [Test]
    public void FromRows_DictionaryRows_KeepKeysVerbatim()
    {
        IReadOnlyDictionary<string, object?> row = new Dictionary<string, object?> { ["FiscalYear"] = 2024, ["GDP per capita"] = null };
        Assert.That(ChartData.FromRows([row]).Values!.ToJsonString(), Is.EqualTo("""[{"FiscalYear":2024,"GDP per capita":null}]"""));
    }
}
