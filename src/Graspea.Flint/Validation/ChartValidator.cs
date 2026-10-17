using Graspea.Flint.Validation.Rules;

namespace Graspea.Flint.Validation;

/// <summary>
/// A static pre-flight of a <see cref="ChartAssemblyInput"/> against the pinned flint-chart version. Severity
/// mirrors upstream: <see cref="IssueSeverity.Error"/> exactly where an assembler of the pinned version throws for the
/// target backend, <see cref="IssueSeverity.Warning"/> where it silently drops or substitutes, <see cref="IssueSeverity.Info"/>
/// otherwise. The codes are listed in <see href="https://github.com/graspea/flint-dotnet/blob/main/docs/validation-codes.md">docs/validation-codes.md</see>.
/// </summary>
public static class ChartValidator
{
    /// <summary>Validates a document for one backend, or for every backend that has its template.</summary>
    /// <param name="input">The document.</param>
    /// <param name="backend">The target backend; null means every backend that has the template, so the result is valid only
    /// when all of them assemble the document.</param>
    /// <returns>The issues; <see cref="ValidationResult.IsValid"/> is false when an assembler throws.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
    public static ValidationResult Validate(ChartAssemblyInput input, FlintBackend? backend = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new ValidationResult(new ValidationRun(input, backend).Run());
    }
}
