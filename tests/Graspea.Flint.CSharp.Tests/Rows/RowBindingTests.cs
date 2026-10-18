using Graspea.Flint.CSharp;

namespace Graspea.Flint.CSharp.Tests.Rows;

/// <summary>Mode B (design §12.6.5): spec fields bound to the members of the user's row type, and the type checks.</summary>
public sealed class RowBindingTests
{
    private static RowMember M(string name, RowMemberKind kind, string? jsonName = null, string? type = null, bool accessible = true, string? ignore = null, bool asStrings = false) =>
        new(name, jsonName, type ?? kind.ToString().ToLowerInvariant(), kind, accessible, ignore, asStrings);

    private static RowBinding Bind(string json, RowMember[] members, RowNamingPolicy policy = RowNamingPolicy.CamelCase, bool enumsAsNumbers = false) =>
        RowBinding.Bind(new SpecDocument(JsonReader.Parse(json)), members, policy, enumsAsNumbers, "Sale", "sales.flint.json");

    private static string Describe(RowBindingIssue i) => $"{i.Id} {i.Severity} {i.Member?.ToString() ?? "-"}";

    // source: design §12.6.5 (rule 1: [JsonPropertyName] equals the field; rule 2: camelCase of the member name, the
    // RowOptions default; selectors over the matched member, a keyword escaped)
    [Test]
    public void Bind_JsonPropertyNameThenCamelCase_MapsFieldsToMembers()
    {
        var binding = Bind("""{"chart_spec":{"chartType":"Bar Chart","encodings":{"x":"region","y":"Total","color":"class"}}}""",
            [M("Region", RowMemberKind.String), M("Amount", RowMemberKind.Real, jsonName: "Total"), M("Class", RowMemberKind.String, jsonName: "class")]);

        Assert.That(binding.Members, Is.EquivalentTo(new Dictionary<string, string> { ["region"] = "Region", ["Total"] = "Amount", ["class"] = "Class" }));
        Assert.That(binding.Issues, Is.Empty);
    }

    // source: design §12.6.5 rule 3 (Verbatim: the member name itself) and CSharpText.Escape (a keyword member)
    [Test]
    public void Bind_Verbatim_MatchesTheMemberName()
    {
        var binding = Bind("""{"chart_spec":{"chartType":"Bar Chart","encodings":{"x":"Region","y":"event"}}}""",
            [M("Region", RowMemberKind.String), M("event", RowMemberKind.Integer)], RowNamingPolicy.Verbatim);

        Assert.That(binding.Members, Is.EquivalentTo(new Dictionary<string, string> { ["Region"] = "Region", ["event"] = "@event" }));
    }

    // source: design §12.6.5 and D7.7 (no member: FLINT120, a Warning as the validator's unknown_field, an Error for a
    // static-series entry as series_field_missing; the message names the field and its JSON position)
    [Test]
    public void Bind_FieldWithoutMember_IsFlint120()
    {
        var binding = Bind("""{"chart_spec":{"chartType":"Line Chart","encodings":{"x":"month","y":["revenue","cost"]}}}""",
            [M("Month", RowMemberKind.DateOnly), M("Revenue", RowMemberKind.Real)]);

        Assert.That(binding.Issues.Select(Describe), Is.EqualTo(new[] { "FLINT120 Error -" }));
        Assert.That(binding.Issues[0].Message, Is.EqualTo("\"cost\" (sales.flint.json(1,81)) is bound by no member of Sale; the static series throws without it (series_field_missing)."));

        var plain = Bind("""{"chart_spec":{"chartType":"Bar Chart","encodings":{"x":"city"}}}""", [M("Region", RowMemberKind.String)]);
        Assert.That(plain.Issues.Select(Describe), Is.EqualTo(new[] { "FLINT120 Warning -" }));
        Assert.That(plain.Issues[0].Message, Does.EndWith("the encoding resolves to nothing (unknown_field)."));
        Assert.That(plain.Members, Is.Empty);
    }

    // source: design §12.6.5 (two members bind one field: FLINT123, an Error; the field stays a name) and the validator's
    // semantic_type_without_field (an annotation of a key the rows do not write, Info, FLINT116)
    [Test]
    public void Bind_AmbiguousAndUnwrittenFields_AreReported()
    {
        var binding = Bind("""{"semantic_types":{"ghost":"Category"},"chart_spec":{"chartType":"Bar Chart","encodings":{"x":"region"}}}""",
            [M("Region", RowMemberKind.String), M("RegionCode", RowMemberKind.String, jsonName: "region")]);

        Assert.That(binding.Issues.Select(Describe), Is.EqualTo(new[] { "FLINT123 Error -", "FLINT116 Info -" }));
        Assert.That(binding.Members, Is.Empty);
        Assert.That(binding.Issues[1].Message, Does.EndWith("(semantic_type_without_field)"));
    }

    // source: design §12.6.5 (a member the generated class cannot reach, such as a private [JsonInclude] field, still
    // binds the key but is written by name)
    [Test]
    public void Bind_InaccessibleMember_BindsWithoutASelector()
    {
        var binding = Bind("""{"chart_spec":{"chartType":"Bar Chart","encodings":{"x":"secret"}}}""", [M("secret", RowMemberKind.String, accessible: false)]);

        Assert.That(binding.Members, Is.Empty);
        Assert.That(binding.Issues, Is.Empty);
    }

    public static IEnumerable<TestCaseData> TypeChecks() => new (string Spec, RowMemberKind Kind, string? Expected)[]
    {
        // quantitative: an explicit encoding type, a Measure semantic type, or numbers in data.values
        ("\"encodings\":{\"y\":{\"field\":\"f\",\"type\":\"quantitative\"}}", RowMemberKind.Real, null),
        ("\"encodings\":{\"y\":{\"field\":\"f\",\"type\":\"quantitative\"}}", RowMemberKind.String, "FLINT121 Warning 0"),
        ("\"encodings\":{\"y\":{\"field\":\"f\",\"type\":\"quantitative\"}}", RowMemberKind.TimeSpan, "FLINT121 Warning 0"),
        ("\"encodings\":{\"y\":{\"field\":\"f\",\"type\":\"quantitative\"}}", RowMemberKind.Opaque, "FLINT124 Info 0"),
        // temporal: dates and strings accepted, integers only for Year and Decade, TimeOnly a Warning, epochs unchecked
        ("\"encodings\":{\"x\":{\"field\":\"f\",\"type\":\"temporal\"}}", RowMemberKind.DateOnly, null),
        ("\"encodings\":{\"x\":{\"field\":\"f\",\"type\":\"temporal\"}}", RowMemberKind.String, null),
        ("\"encodings\":{\"x\":{\"field\":\"f\",\"type\":\"temporal\"}}", RowMemberKind.TimeOnly, "FLINT121 Warning 0"),
        ("\"encodings\":{\"x\":{\"field\":\"f\",\"type\":\"temporal\"}}", RowMemberKind.Integer, "FLINT124 Info 0"),
        ("\"semantic_types\":{\"f\":\"Year\"},\"encodings\":{\"x\":\"f\"}", RowMemberKind.Integer, null),
        // ordinal and nominal
        ("\"semantic_types\":{\"f\":\"Month\"},\"encodings\":{\"x\":\"f\"}", RowMemberKind.Real, null),
        ("\"semantic_types\":{\"f\":\"Month\"},\"encodings\":{\"x\":\"f\"}", RowMemberKind.Collection, "FLINT121 Warning 0"),
        ("\"semantic_types\":{\"f\":\"Category\"},\"encodings\":{\"x\":\"f\"}", RowMemberKind.Guid, null),
        ("\"semantic_types\":{\"f\":\"Category\"},\"encodings\":{\"x\":\"f\"}", RowMemberKind.Real, "FLINT121 Warning 0"),
        ("\"semantic_types\":{\"f\":\"Category\"},\"encodings\":{\"x\":\"f\"}", RowMemberKind.Object, "FLINT121 Warning 0"),
        // an aggregate that sums needs a number
        ("\"encodings\":{\"y\":{\"field\":\"f\",\"aggregate\":\"sum\"}}", RowMemberKind.String, "FLINT121 Warning 0"),
        // nothing says what the field is: not checked
        ("\"encodings\":{\"x\":\"f\"}", RowMemberKind.Collection, null),
    }.Select(c => new TestCaseData(c.Spec, c.Kind.ToString(), c.Expected).SetName($"Bind_TypeCheck({c.Spec}, {c.Kind})"));

    // source: design §12.6.5, the type table (FLINT121 Warning for an incompatible type, FLINT124 Info for an unchecked one)
    // and the order: the encoding's explicit type, else the registered semantic type's first visEncodings entry
    [TestCaseSource(nameof(TypeChecks))]
    public void Bind_TypeCheck(string chartSpec, string kind, string? expected)
    {
        var json = chartSpec.StartsWith("\"semantic_types\"", StringComparison.Ordinal)
            ? $"{{{chartSpec.Substring(0, chartSpec.IndexOf(",\"encodings\"", StringComparison.Ordinal))},\"chart_spec\":{{\"chartType\":\"Bar Chart\",{chartSpec.Substring(chartSpec.IndexOf("\"encodings\"", StringComparison.Ordinal))}}}}}"
            : $"{{\"chart_spec\":{{\"chartType\":\"Bar Chart\",{chartSpec}}}}}";
        var binding = Bind(json, [M("F", Enum.Parse<RowMemberKind>(kind))]);

        Assert.That(binding.Issues.Select(Describe), Is.EqualTo(expected is null ? Array.Empty<string>() : new[] { expected }));
    }

    // source: design §12.6.5 (data.values, when present, decides with the upstream inference rule; numbers infer as
    // quantitative)
    [Test]
    public void Bind_NumbersInTheData_AreQuantitative()
    {
        var binding = Bind("""{"data":{"values":[{"f":1},{"f":2.5}]},"chart_spec":{"chartType":"Bar Chart","encodings":{"y":"f"}}}""", [M("F", RowMemberKind.String)]);

        Assert.That(binding.Issues.Select(Describe), Is.EqualTo(new[] { "FLINT121 Warning 0" }));
        Assert.That(binding.Issues[0].Message, Is.EqualTo("Sale.F (string) is bound to \"f\", which the spec treats as quantitative; upstream reads a string as a category, not a number."));
    }

    // source: design §12.6.5 (an enum written as a number under a JsonContext without UseStringEnumConverter, bound to a
    // nominal or ordinal field: FLINT121, upstream infers the number as quantitative)
    [Test]
    public void Bind_EnumWrittenAsNumber_IsFlint121()
    {
        const string json = """{"semantic_types":{"f":"Category"},"chart_spec":{"chartType":"Bar Chart","encodings":{"x":"f"}}}""";

        Assert.That(Bind(json, [M("F", RowMemberKind.Enum)], enumsAsNumbers: true).Issues.Select(Describe), Is.EqualTo(new[] { "FLINT121 Warning 0" }));
        Assert.That(Bind(json, [M("F", RowMemberKind.Enum)]).Issues, Is.Empty);
    }

    // source: design §12.6.5 (the row guarantees of §5.3 checked at compile time: a member that may be omitted when null,
    // or numbers written as strings, is FLINT122, an Error, because FromRows throws ArgumentException)
    [Test]
    public void Bind_MemberMetadataTheLibraryRefuses_IsFlint122()
    {
        var binding = Bind("""{"chart_spec":{"chartType":"Bar Chart","encodings":{"x":"a"}}}""",
            [M("A", RowMemberKind.String), M("B", RowMemberKind.String, ignore: "WhenWritingNull"), M("C", RowMemberKind.Real, asStrings: true)]);

        Assert.That(binding.Issues.Select(Describe), Is.EqualTo(new[] { "FLINT122 Error 1", "FLINT122 Error 2" }));
    }
}
