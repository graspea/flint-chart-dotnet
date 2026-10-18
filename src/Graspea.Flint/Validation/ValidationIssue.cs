namespace Graspea.Flint.Validation;

/// <summary>One finding of <see cref="ChartValidator"/>. The codes are listed in <see href="https://github.com/graspea/flint-chart-dotnet/blob/main/docs/validation-codes.md">docs/validation-codes.md</see>.</summary>
public sealed class ValidationIssue
{
    internal ValidationIssue()
    {
    }

    /// <summary>Error where upstream throws, Warning where it silently drops, Info otherwise.</summary>
    public IssueSeverity Severity { get; init; }

    /// <summary>The snake_case code, such as <c>unknown_chart_type</c>.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>A description of the finding, naming the backends it applies to.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>The wire path of the offending member, such as <c>chart_spec.encodings.x</c> or <c>data.values[3]</c>.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>The encoding channel the issue concerns, if any.</summary>
    public Channel? Channel { get; init; }

    /// <summary>The data field the issue concerns, if any.</summary>
    public string? Field { get; init; }

    /// <summary>The severity, code, path and message.</summary>
    /// <returns>A one-line description.</returns>
    public override string ToString() => $"{Severity} {Code} at {Path}: {Message}";
}
