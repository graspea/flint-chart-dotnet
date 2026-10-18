using Graspea.Flint.CSharp;

namespace Graspea.Flint.CSharp.Tests.Rows;

public sealed class RowInferenceTests
{
    private static string[] Members(string json, bool replay = true)
    {
        var plan = RowInference.Infer(new SpecDocument(JsonReader.Parse(json)), replay);
        return plan.Fields.Select(f => $"{f.Wire}={f.Member}:{f.TypeText}").ToArray();
    }

    private static string Spec(string values, string extra = "") =>
        $"{{\"data\":{{\"values\":{values}}},{extra}\"chart_spec\":{{\"chartType\":\"Bar Chart\",\"encodings\":{{}}}}}}";

    // source: design §12.4.3 type table (booleans -> bool; integral numbers within +-2^53 -> long; other numbers -> double;
    // strings -> string; mixed kinds and nested values -> JsonNode?; a null makes the type nullable)
    [Test]
    public void Infer_TypesEachFieldFromItsValues()
    {
        var members = Members(Spec("[{\"b\":true,\"n\":1,\"d\":1.5,\"s\":\"x\",\"m\":1,\"o\":{\"k\":1},\"z\":null},{\"b\":false,\"n\":2,\"d\":2,\"s\":null,\"m\":\"y\",\"o\":[1],\"z\":null}]"));

        Assert.That(members, Is.EqualTo(new[] { "b=B:bool", "n=N:long", "d=D:double", "s=S:string?", "m=M:JsonNode?", "o=O:JsonNode?", "z=Z:string?" }));
    }

    // source: design §12.4.3 (integral is lexical: 1.0 and 1e3 are not integral; beyond 2^53 a long would not survive
    // JavaScript, so the field is double)
    [TestCase("1.0", "double")]
    [TestCase("1e3", "double")]
    [TestCase("9007199254740992", "long")]
    [TestCase("9007199254740993", "double")]
    [TestCase("-9007199254740993", "double")]
    public void Infer_IntegralIsLexical(string number, string type)
    {
        Assert.That(Members(Spec($"[{{\"v\":{number}}}]")), Is.EqualTo(new[] { $"v=V:{type}" }));
    }

    // source: design §12.4.3 (a number that double cannot represent is kept as JSON)
    [Test]
    public void Infer_NumberBeyondDouble_IsJsonNode()
    {
        Assert.That(Members(Spec("[{\"v\":1e400}]")), Is.EqualTo(new[] { "v=V:JsonNode?" }));
    }

    // source: design §12.4.3 (numbers that fit decimal with the semantic type Amount, Price or Profit -> decimal)
    [TestCase("Amount")]
    [TestCase("Price")]
    [TestCase("Profit")]
    public void Infer_MoneySemanticType_IsDecimal(string semanticType)
    {
        Assert.That(Members(Spec("[{\"v\":1200.50},{\"v\":3}]", $"\"semantic_types\":{{\"v\":\"{semanticType}\"}},")), Is.EqualTo(new[] { "v=V:decimal" }));
    }

    // source: design §12.4.3 (strings that are all exactly yyyy-MM-dd valid dates -> DateOnly, with a temporal hint or none;
    // other date strings stay string, because the written form would change)
    [TestCase("[{\"v\":\"2024-01-31\"}]", "", "DateOnly")]
    [TestCase("[{\"v\":\"2024-01-31\"}]", "\"semantic_types\":{\"v\":\"Date\"},", "DateOnly")]
    [TestCase("[{\"v\":\"2024-01-31\"}]", "\"semantic_types\":{\"v\":\"Category\"},", "string")]
    [TestCase("[{\"v\":\"2024-02-30\"}]", "", "string")]
    [TestCase("[{\"v\":\"2024-1-31\"}]", "", "string")]
    [TestCase("[{\"v\":\"2024-01-31T00:00:00Z\"}]", "", "string")]
    [TestCase("[{\"v\":\"2024-01-31\"},{\"v\":null}]", "", "DateOnly?")]
    public void Infer_DateStrings(string values, string extra, string type)
    {
        Assert.That(Members(Spec(values, extra)), Is.EqualTo(new[] { $"v=V:{type}" }));
    }

    // source: design §12.4.3 (an explicit temporal encoding type is a temporal hint)
    [Test]
    public void Infer_TemporalEncoding_IsAHint()
    {
        const string json = "{\"data\":{\"values\":[{\"v\":\"2024-01-31\"}]},\"chart_spec\":{\"chartType\":\"Bar Chart\",\"encodings\":{\"x\":{\"field\":\"v\",\"type\":\"nominal\"}}}}";

        Assert.That(Members(json), Is.EqualTo(new[] { "v=V:string" }));
    }

    // source: design §12.4.3 member names (Pascal(field); empty -> Field{position}; Row and record members get the suffix
    // Field; duplicates get 2, 3 in first-seen order)
    [Test]
    public void Infer_MemberNames()
    {
        var members = Members(Spec("[{\"GDP per capita\":1,\"a b\":1,\"a_b\":1,\"A-B\":1,\"Row\":1,\"Equals\":1,\"---\":1,\"3d\":1}]"));

        Assert.That(members.Select(m => m[..m.IndexOf(':')]), Is.EqualTo(new[]
        {
            "GDP per capita=GdpPerCapita", "a b=AB", "a_b=AB2", "A-B=AB3", "Row=RowField", "Equals=EqualsField", "---=Field7", "3d=_3d",
        }));
    }

    // source: design §12.4.3 (rows replayed from the spec: exactly the keys of the first row; a field only an encoding,
    // annotation or display name names stays a string field name)
    [Test]
    public void Infer_Replay_UsesTheFirstRowKeysOnly()
    {
        const string json = "{\"data\":{\"values\":[{\"b\":1,\"a\":\"x\"}]},\"semantic_types\":{\"c\":\"Count\"},\"chart_spec\":{\"chartType\":\"Bar Chart\",\"encodings\":{\"x\":\"a\",\"y\":{\"field\":\"d\"}}},\"field_display_names\":{\"e\":\"E\"}}";

        Assert.That(Members(json), Is.EqualTo(new[] { "b=B:long", "a=A:string" }));
    }

    // source: design §12.4.3 (rows supplied later: the union, in first-seen order, of the data keys, the encoding fields
    // including series entries, the semantic_types keys and the field_display_names keys; a field missing from the data
    // is nullable, typed from the hints: Count -> int, quantitative -> double, temporal -> DateOnly (DateTime and
    // Timestamp -> DateTimeOffset), Amount -> decimal, Boolean -> bool, nominal -> string, no hint -> string)
    [Test]
    public void Infer_Union_AddsHintedFields()
    {
        const string json = "{\"data\":{\"values\":[{\"b\":1}]},\"semantic_types\":{\"c\":\"Count\",\"t\":\"Timestamp\",\"m\":\"Amount\",\"f\":\"Boolean\",\"q\":\"Quantity\"},\"chart_spec\":{\"chartType\":\"Bar Chart\",\"encodings\":{\"x\":{\"field\":\"d\",\"type\":\"temporal\"},\"y\":[\"q\",\"n\"]}},\"field_display_names\":{\"e\":\"E\"}}";

        Assert.That(Members(json, replay: false), Is.EqualTo(new[]
        {
            "b=B:long", "d=D:DateOnly?", "q=Q:double?", "n=N:string?", "c=C:int?", "t=T:DateTimeOffset?", "m=M:decimal?", "f=F:bool?", "e=E:string?",
        }));
    }

    // source: design §12.4.3 (without data.values every field comes from the hints and is not nullable)
    [Test]
    public void Infer_NoData_TypesFromHints()
    {
        const string json = "{\"semantic_types\":{\"month\":\"YearMonth\",\"revenue\":\"Amount\"},\"chart_spec\":{\"chartType\":\"Line Chart\",\"encodings\":{\"x\":\"month\",\"y\":\"revenue\",\"color\":\"region\"}}}";

        var plan = RowInference.Infer(new SpecDocument(JsonReader.Parse(json)), replay: true);

        Assert.That(plan.Typed, Is.True);
        Assert.That(plan.Fields.Select(f => $"{f.Wire}={f.Member}:{f.TypeText}"), Is.EqualTo(new[] { "month=Month:DateOnly", "revenue=Revenue:decimal", "region=Region:string" }));
        Assert.That(plan.UnhintedFields, Is.EqualTo(new[] { "region" }));
    }

    // source: design §12.4.3 shape gate (typed rows only when every row is an object with the same keys in the same order)
    [TestCase("[{\"a\":1,\"b\":2},{\"b\":2,\"a\":1}]")]
    [TestCase("[{\"a\":1},{\"a\":1,\"b\":2}]")]
    [TestCase("[{\"a\":1},null]")]
    [TestCase("[{\"a\":1},7]")]
    public void Infer_NonUniformRows_AreNotTyped(string values)
    {
        Assert.That(RowInference.Infer(new SpecDocument(JsonReader.Parse(Spec(values))), replay: true).Typed, Is.False);
    }

    // source: design §12.6.6 and §12.4.3 (when the shape gate fails for rows supplied later, every field is JsonNode?, over
    // the union of the object rows' keys)
    [Test]
    public void Infer_NonUniformRowsSuppliedLater_AreJsonNodes()
    {
        var plan = RowInference.Infer(new SpecDocument(JsonReader.Parse(Spec("[{\"a\":1},{\"b\":\"x\",\"a\":2},null]"))), replay: false);

        Assert.That(plan.Typed, Is.False);
        Assert.That(plan.Fields.Select(f => $"{f.Wire}={f.Member}:{f.TypeText}"), Is.EqualTo(new[] { "a=A:JsonNode?", "b=B:JsonNode?" }));
    }
}
