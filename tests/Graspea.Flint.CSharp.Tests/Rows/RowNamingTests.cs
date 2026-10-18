using System.Text.Json;
using Graspea.Flint.CSharp;

namespace Graspea.Flint.CSharp.Tests.Rows;

/// <summary>The naming policies mode B binds with (design §12.6.5): System.Text.Json's, reimplemented for netstandard2.0.</summary>
public sealed class RowNamingTests
{
    private static readonly string[] Names =
    [
        "Month", "FiscalYear", "URL", "URLValue", "IOStream", "ABC", "a", "X", "MyURL", "Item1", "HTMLParser2", "alreadyCamel",
        "Snake_Case", "With Space", "With  Two Spaces", "PascalCase", "__Private", "A B", "XMLHttpRequest", "x1Y2", "GDPPerCapita",
        "Ünïcode", "ÉCOLE", "Already-Kebab", "Number1Value", "ID", "Id", "I", "A1B2C3", "Revenue2024Q1", "_",
    ];

    public static IEnumerable<TestCaseData> Policies() =>
        from policy in new[]
        {
            (RowNamingPolicy.CamelCase, JsonNamingPolicy.CamelCase),
            (RowNamingPolicy.SnakeCaseLower, JsonNamingPolicy.SnakeCaseLower),
            (RowNamingPolicy.SnakeCaseUpper, JsonNamingPolicy.SnakeCaseUpper),
            (RowNamingPolicy.KebabCaseLower, JsonNamingPolicy.KebabCaseLower),
            (RowNamingPolicy.KebabCaseUpper, JsonNamingPolicy.KebabCaseUpper),
        }
        from name in Names
        select new TestCaseData(policy.Item1.ToString(), policy.Item2, name).SetName($"Convert_EqualsSystemTextJson({policy.Item1}, \"{name}\")");

    // source: System.Text.Json's JsonNamingPolicy of the running .NET (net8.0 and net10.0 test legs), the policies a row
    // type's keys are written with (RowOptions.Default is camelCase; a JsonSerializerContext may name another)
    [TestCaseSource(nameof(Policies))]
    public void Convert_EqualsSystemTextJson(string policy, JsonNamingPolicy expected, string name)
    {
        Assert.That(RowNaming.Convert(Enum.Parse<RowNamingPolicy>(policy), name), Is.EqualTo(expected.ConvertName(name)));
    }

    // source: design §12.6.5 rule 3 (FlintRowNaming.Verbatim: the member name as written)
    [Test]
    public void Convert_Verbatim_KeepsTheName()
    {
        Assert.That(RowNaming.Convert(RowNamingPolicy.Verbatim, "FiscalYear"), Is.EqualTo("FiscalYear"));
    }
}
