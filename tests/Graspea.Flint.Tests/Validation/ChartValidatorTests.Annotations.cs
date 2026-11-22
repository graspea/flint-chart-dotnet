using Graspea.Flint.Validation;

namespace Graspea.Flint.Tests.Validation;

/// <summary>The Infos on the values of a semantic annotation that flint-chart reads without a throw or a warning.</summary>
public sealed partial class ChartValidatorTests
{
    // source: flint-chart 1.1.1 compares percentScale with 'fraction' and 'percent' at src/core/field-semantics.ts:364-365,
    // and polarity with 'higher-is-better' and 'lower-is-better' at src/core/resolve-semantics.ts:744, :746 (any other
    // string makes neither choice; neither member throws or warns), and docs/validation-codes.md invalid_literal (Info, the
    // literal sets of the IR)
    [Test]
    public void Infos_InvalidAnnotationLiteral()
    {
        var input = Bar(i => i.SemanticTypes = new()
        {
            ["revenue"] = new SemanticAnnotation { Type = SemanticType.Percentage, Polarity = new Polarity("sideways"), PercentScale = new PercentScale("basis-points") },
            ["cost"] = new SemanticAnnotation { Type = SemanticType.Percentage, Polarity = Polarity.HigherIsBetter, PercentScale = PercentScale.Fraction },
        });

        var issues = Issues(input, null, "invalid_literal").ToList();
        Assert.That(issues.Select(i => (i.Severity, i.Path, i.Field)), Is.EqualTo(new[]
        {
            (IssueSeverity.Info, "semantic_types.revenue.polarity", "revenue"),
            (IssueSeverity.Info, "semantic_types.revenue.percentScale", "revenue"),
        }));
        Assert.That(issues[1].Message, Is.EqualTo($"\"basis-points\" is not one of fraction, percent in flint-chart {FlintUpstream.Version}; it is written as given."));
        Assert.That(ChartValidator.Validate(input).IsValid, Is.True);
    }

    // source: flint-chart 1.1.1 src/core/resolve-semantics.ts:737-739 (a number below 0 fails `>= 0` and, not being
    // undefined, gives no neutral class, so the midpoint is a break as with false; 0 passes and leaves no class; nothing
    // throws or warns)
    [Test]
    public void Infos_NegativeDivergingNeutral()
    {
        var input = Bar(i => i.SemanticTypes = new()
        {
            ["revenue"] = new SemanticAnnotation { Type = SemanticType.Profit, DivergingNeutral = -0.1 },
            ["cost"] = new SemanticAnnotation { Type = SemanticType.Profit, DivergingNeutral = 0 },
            ["city"] = new SemanticAnnotation { Type = SemanticType.Category, DivergingNeutral = DivergingNeutral.Break },
        });

        var issue = Issues(input, null, "negative_diverging_neutral").Single();
        Assert.That((issue.Severity, issue.Path, issue.Field), Is.EqualTo((IssueSeverity.Info, "semantic_types.revenue.divergingNeutral", "revenue")));
        Assert.That(issue.Message, Is.EqualTo(
            "divergingNeutral -0.1 is negative; flint-chart reads a number of at least 0 as the half-width of the neutral class and any other value as a break, as false."));
        AssertOne(input, FlintBackend.Excel, "negative_diverging_neutral", IssueSeverity.Info);
        Assert.That(ChartValidator.Validate(input).IsValid, Is.True);
    }
}
