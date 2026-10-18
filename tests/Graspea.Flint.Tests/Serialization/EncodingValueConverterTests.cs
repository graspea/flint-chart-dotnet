using System.Text.Json;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Serialization;

public sealed class EncodingValueConverterTests
{
    private static EncodingValue Read(string json) => JsonSerializer.Deserialize<EncodingValue>(json)!;

    // source: d.ts T:1994-2001 (raw encoding value: a string becomes {field}) and design §5.2 (written as {"field":…})
    [Test]
    public void Read_String_IsSingleEncodingWrittenAsObject()
    {
        var value = Read("\"month\"");
        Assert.That(value.IsSeries, Is.False);
        Assert.That(value.Encodings, Has.Count.EqualTo(1));
        Assert.That(value.Encodings[0].Field, Is.EqualTo("month"));
        Assert.That(JsonSerializer.Serialize(value), Is.EqualTo("""{"field":"month"}"""));
    }

    // source: d.ts T:1994-2001 (field, aggregate, sortOrder) and design §10.4 test 8
    [Test]
    public void Read_Object_ReadsEveryMember()
    {
        var value = Read("""{"field":"m","aggregate":"sum","sortOrder":"descending"}""");
        Assert.That(value.IsSeries, Is.False);
        var e = value.Encodings[0];
        Assert.That(e.Field, Is.EqualTo("m"));
        Assert.That(e.Aggregate, Is.EqualTo(Aggregate.Sum));
        Assert.That(e.SortOrder, Is.EqualTo(SortOrder.Descending));
        Assert.That(e.Type, Is.Null);
    }

    // source: d.ts T:1994-2001 (all six members, in declaration order) and design §5.2 (fixed declaration order)
    [Test]
    public void Object_WithEveryMember_RoundTripsInDeclarationOrder()
    {
        const string json = """{"field":"x","type":"temporal","aggregate":"count","sortOrder":"ascending","sortBy":"y","scheme":"default"}""";
        var value = Read(json);
        Assert.That(value.Encodings[0].Type, Is.EqualTo(EncodingType.Temporal));
        Assert.That(value.Encodings[0].SortBy, Is.EqualTo("y"));
        Assert.That(value.Encodings[0].Scheme, Is.EqualTo("default"));
        Assert.That(JsonSerializer.Serialize(value), Is.EqualTo(json));
    }

    // source: design §2 (static series: an array of encodings or strings) and §5.1 table (arrays with mixed items)
    [Test]
    public void Read_MixedArray_IsSeriesOfTwo()
    {
        var value = Read("""[{"field":"a"},"b"]""");
        Assert.That(value.IsSeries, Is.True);
        Assert.That(value.Encodings.Select(e => e.Field), Is.EqualTo(new[] { "a", "b" }));
        Assert.That(JsonSerializer.Serialize(value), Is.EqualTo("""[{"field":"a"},{"field":"b"}]"""));
    }

    // source: design §5.1 table (array when IsSeries, even with one entry)
    [Test]
    public void Write_OneEntrySeries_IsArray()
    {
        var value = EncodingValue.Series(new ChartEncoding { Field = "a" });
        Assert.That(value.IsSeries, Is.True);
        Assert.That(JsonSerializer.Serialize(value), Is.EqualTo("""[{"field":"a"}]"""));
    }

    // source: d.ts T:1994-2001 (aggregate: count | sum | average | mean; mean added in 0.1.3)
    [Test]
    public void Read_AggregateMean_IsTheMeanConstant()
    {
        Assert.That(Read("""{"field":"v","aggregate":"mean"}""").Encodings[0].Aggregate, Is.EqualTo(Aggregate.Mean));
    }

    // source: design §5.2 (unknown literals round-trip verbatim because they are string-backed)
    [Test]
    public void Read_UnknownAggregate_RoundTripsVerbatim()
    {
        var value = Read("""{"field":"v","aggregate":"median"}""");
        Assert.That(value.Encodings[0].Aggregate, Is.EqualTo(new Aggregate("median")));
        Assert.That(JsonSerializer.Serialize(value), Is.EqualTo("""{"field":"v","aggregate":"median"}"""));
    }

    // source: design §5.1 (converters ignore the caller's options)
    [Test]
    public void Write_UnderHostileOptions_IsIdentical()
    {
        var value = EncodingValue.Series(new ChartEncoding { Field = "a", SortBy = "x" }, new ChartEncoding { Field = "b", Type = EncodingType.Quantitative });
        Assert.That(JsonSerializer.Serialize(value, HostileOptions.Create()), Is.EqualTo("""[{"field":"a","sortBy":"x"},{"field":"b","type":"quantitative"}]"""));
    }

    // source: design §5.1 (malformed input is a JsonException)
    [TestCase("42", TestName = "Read_Number_ThrowsJsonException")]
    [TestCase("true", TestName = "Read_Boolean_ThrowsJsonException")]
    [TestCase("[null]", TestName = "Read_NullArrayItem_ThrowsJsonException")]
    [TestCase("[1]", TestName = "Read_NumberArrayItem_ThrowsJsonException")]
    public void Read_OtherShapes_ThrowJsonException(string json)
    {
        Assert.That((Action)(() => Read(json)), Throws.InstanceOf<JsonException>());
    }

    // source: design §4.1 (Series takes encodings; a null entry is an argument error)
    [Test]
    public void Series_NullEntry_ThrowsArgumentNullException()
    {
        Assert.That((Action)(() => EncodingValue.Series(null!)), Throws.ArgumentNullException);
        Assert.That((Action)(() => EncodingValue.Series(new ChartEncoding(), null!)), Throws.ArgumentNullException);
    }
}
