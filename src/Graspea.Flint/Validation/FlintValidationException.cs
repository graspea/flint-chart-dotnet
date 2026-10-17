namespace Graspea.Flint.Validation;

/// <summary>Thrown by <see cref="ValidationResult.ThrowIfInvalid"/> when a document has an error.</summary>
public sealed class FlintValidationException : Exception
{
    internal FlintValidationException(IReadOnlyList<ValidationIssue> issues)
        : base(Describe(issues)) => Issues = issues;

    /// <summary>Every issue of the result, not only the errors.</summary>
    public IReadOnlyList<ValidationIssue> Issues { get; }

    private static string Describe(IReadOnlyList<ValidationIssue> issues)
    {
        var errors = issues.Where(i => i.Severity == IssueSeverity.Error).Select(e => $"{e.Code} at {e.Path}: {e.Message}");
        return $"The chart document is not valid: {string.Join("; ", errors)}";
    }
}
