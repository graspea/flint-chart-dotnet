using System.Globalization;
using Graspea.Flint.CSharp;

namespace Graspea.Flint.Tool;

/// <summary>
/// The catalog findings the tool prints (design §12.5.4, §12.6.7): the checks of the conversion core, which the generator
/// runs too, in the MSBuild canonical form with the category <c>warning</c> whatever their severity, so that the same run
/// passes under every caller; the code and the validator severity are in the message. They never change the exit code.
/// </summary>
internal static class CatalogFindings
{
    public static IEnumerable<string> Of(JsonItem root, string file)
    {
        foreach (var issue in CatalogCheck.Run(new SpecDocument(root)))
        {
            var suffix = $" ({issue.Code})";
            var message = issue.Message.EndsWith(suffix, StringComparison.Ordinal) ? issue.Message[..^suffix.Length] : issue.Message;
            yield return string.Create(CultureInfo.InvariantCulture, $"{file}({issue.Start.Line},{issue.Start.Column}): warning {issue.Id}: {issue.Code} ({issue.Severity}): {message}");
        }
    }
}
