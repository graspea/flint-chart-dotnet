using System.Text.Json;
using Graspea.Flint.CSharp;
using Graspea.Flint.CSharp.Tests.Support;

namespace Graspea.Flint.CSharp.Tests.Writing;

public sealed class CSharpTextTests
{
    public static IEnumerable<TestCaseData> PascalVectors() =>
        JsonDocument.Parse(File.ReadAllText(Repository.Path("generator", "test", "vectors", "pascal.json"))).RootElement.EnumerateArray()
            .Select(v => new TestCaseData(v[0].GetString()!, v[1].GetString() ?? string.Empty));

    // source: generator/test/vectors/pascal.json, the rule-1 vectors naming.test.ts checks against pascal() (design §12.4.3:
    // shared test vectors; an empty result is a field without a derivable name)
    [TestCaseSource(nameof(PascalVectors))]
    public void Pascal_MatchesTheGeneratorRule(string input, string expected)
    {
        Assert.That(CSharpText.Pascal(input), Is.EqualTo(expected));
    }

    // source: design §12.5.3 (regular literals; \" \\ and the named escapes \0 \a \b \f \n \r \t \v)
    [Test]
    public void StringLiteral_UsesTheNamedEscapes()
    {
        Assert.That(CSharpText.StringLiteral("a\"b\\c\0\a\b\f\n\r\t\v"), Is.EqualTo("\"a\\\"b\\\\c\\0\\a\\b\\f\\n\\r\\t\\v\""));
    }

    // source: design §12.5.3 (other C0 and C1 controls, the categories Cf (U+202E right-to-left override, U+200B zero width
    // space, U+FEFF), Zl and Zp (U+2028, U+2029), Co (U+E000) and U+FFFD are escaped; CVE-2021-42574, "Trojan Source")
    [TestCase("\u202E", "\\u202E")]
    [TestCase("\u200B", "\\u200B")]
    [TestCase("\uFEFF", "\\uFEFF")]
    [TestCase("\u2028", "\\u2028")]
    [TestCase("\u2029", "\\u2029")]
    [TestCase("\uE000", "\\uE000")]
    [TestCase("\uFFFD", "\\uFFFD")]
    [TestCase("\u0001", "\\u0001")]
    [TestCase("\u0085", "\\u0085")]
    [TestCase("\U000E0001", "\\U000E0001")]
    [TestCase("\U000F0000", "\\U000F0000")]
    public void StringLiteral_EscapesInvisibleAndBidirectionalCharacters(string input, string escaped)
    {
        Assert.That(CSharpText.StringLiteral("a" + input + "b"), Is.EqualTo("\"a" + escaped + "b\""));
    }

    // source: design §12.5.3 (every other character is written as itself, UTF-8 output)
    [TestCase("é")]
    [TestCase("Größe")]
    [TestCase("日本")]
    [TestCase("\U0001F600")]
    public void StringLiteral_KeepsVisibleCharacters(string input)
    {
        Assert.That(CSharpText.StringLiteral(input), Is.EqualTo("\"" + input + "\""));
    }

    // source: design §12.5.3 (JSON fragments are single-line raw string literals with one more quote than the longest
    // quote run in the content, at least three)
    [TestCase("{\"a\":1}", "\"\"\"{\"a\":1}\"\"\"")]
    [TestCase("[\"a\"\"\"\"b\"]", "\"\"\"\"\"[\"a\"\"\"\"b\"]\"\"\"\"\"")]
    public void RawStringLiteral_UsesALongerDelimiterThanAnyQuoteRun(string content, string expected)
    {
        Assert.That(CSharpText.RawStringLiteral(content), Is.EqualTo(expected));
    }

    // source: design §12.5.3 (numbers keep their raw text; long gets L outside the int range, decimal gets m, double gets d
    // when the text has neither a decimal point nor an exponent)
    [TestCase("12", "12")]
    [TestCase("-2147483648", "-2147483648")]
    [TestCase("2147483648", "2147483648L")]
    [TestCase("-9007199254740991", "-9007199254740991L")]
    public void LongLiteral_AddsLOutsideTheIntRange(string raw, string expected)
    {
        Assert.That(CSharpText.LongLiteral(raw), Is.EqualTo(expected));
    }

    // source: as above
    [TestCase("45", "45d")]
    [TestCase("45.2", "45.2")]
    [TestCase("1e3", "1e3")]
    [TestCase("-0.5E+10", "-0.5E+10")]
    public void DoubleLiteral_AddsDToIntegralText(string raw, string expected)
    {
        Assert.That(CSharpText.DoubleLiteral(raw), Is.EqualTo(expected));
    }

    // source: as above
    [TestCase("1200.50", "1200.50m")]
    [TestCase("7", "7m")]
    public void DecimalLiteral_AddsM(string raw, string expected)
    {
        Assert.That(CSharpText.DecimalLiteral(raw), Is.EqualTo(expected));
    }

    // source: design §12.5.3 (a number double cannot represent is not a C# literal; it is written as a JSON fragment)
    [TestCase("1e400", false)]
    [TestCase("-1e400", false)]
    [TestCase("1e-400", true)]
    [TestCase("1.7976931348623157e308", true)]
    public void FitsDouble_RejectsOverflow(string raw, bool fits)
    {
        Assert.That(CSharpText.FitsDouble(raw), Is.EqualTo(fits));
    }

    // source: C# language specification, identifiers (letter or underscore, then letters, digits, connectors, combining
    // and formatting characters; a keyword is not an identifier without @) and design §12.5.3
    [TestCase("RevenueChart", true)]
    [TestCase("_x1", true)]
    [TestCase("Größe", true)]
    [TestCase("1a", false)]
    [TestCase("a-b", false)]
    [TestCase("class", false)]
    [TestCase("@class", false)]
    [TestCase("", false)]
    public void IsIdentifier_FollowsTheGrammar(string name, bool valid)
    {
        Assert.That(CSharpText.IsIdentifier(name), Is.EqualTo(valid));
    }

    // source: design §12.5.1 (--namespace is a dotted C# identifier)
    [TestCase("Charts", true)]
    [TestCase("Company.Charts", true)]
    [TestCase("Company..Charts", false)]
    [TestCase("Company.class", false)]
    [TestCase(".Charts", false)]
    public void IsNamespace_IsADottedIdentifier(string name, bool valid)
    {
        Assert.That(CSharpText.IsNamespace(name), Is.EqualTo(valid));
    }

    // source: design §12.5.1 (--class default: Pascal(stem) plus Chart unless it already ends in Chart; the stem is the
    // file name without a trailing .flint.json, else without .json; Flint before a reserved class name; FlintChart for
    // standard input)
    [TestCase("revenue.flint.json", "RevenueChart")]
    [TestCase("sales.json", "SalesChart")]
    [TestCase("Charts/kpi-card.flint.json", "KpiCardChart")]
    [TestCase("SalesChart.json", "SalesChart")]
    [TestCase("notes.txt", "NotesTxtChart")]
    [TestCase("---.json", "FlintChart")]
    [TestCase("-", "FlintChart")]
    [TestCase("chart.flint.json", "FlintChart")]
    [TestCase("Chart.json", "FlintChart")]
    public void ClassNameFor_DerivesTheClassFromTheFileName(string file, string expected)
    {
        Assert.That(CSharpText.ClassNameFor(file), Is.EqualTo(expected));
    }

    // source: design §12.5.1 (a reserved class name would hide a type the output names, such as Chart in Chart.Bar(Rows))
    // and src/Graspea.Flint.Tool/README.md (the reserved names)
    [TestCase("Chart")]
    [TestCase("ChartData")]
    [TestCase("ChartTemplate")]
    [TestCase("FlintJson")]
    [TestCase("RowOptions")]
    [TestCase("JsonArray")]
    [TestCase("AreaChartInterpolate")]
    [TestCase("ThemeInk")]
    public void ReservedClassNames_HoldTheTypesTheOutputNames(string name)
    {
        Assert.That(CSharpText.ReservedClassNames, Does.Contain(name));
    }
}
