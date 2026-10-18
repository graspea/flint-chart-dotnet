namespace Graspea.Flint.Validation;

/// <summary>The outcome of <see cref="ChartValidator.Validate"/>.</summary>
public sealed class ValidationResult
{
    internal ValidationResult(IReadOnlyList<ValidationIssue> issues) => Issues = issues;

    /// <summary>Every issue, in a stable order.</summary>
    public IReadOnlyList<ValidationIssue> Issues { get; }

    /// <summary>True when no issue is an <see cref="IssueSeverity.Error"/>: every targeted assembler assembles the document.</summary>
    public bool IsValid => !Issues.Any(i => i.Severity == IssueSeverity.Error);

    /// <summary>Throws when the result has an error.</summary>
    /// <exception cref="FlintValidationException"><see cref="IsValid"/> is false; the exception carries every issue.</exception>
    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new FlintValidationException(Issues);
        }
    }
}
