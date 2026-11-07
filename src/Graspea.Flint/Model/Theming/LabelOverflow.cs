using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint.Theming;

/// <summary>
/// <c>labels.overflow</c>: one step (<see cref="Step"/>) or the ordered path of steps (<see cref="Steps"/>) that category
/// names take when they do not stand flat and whole: <c>wrap</c>, <c>skip</c>, <c>rotate45</c>, <c>rotate90</c>. Assign
/// either directly: <c>Overflow = "wrap"</c> or <c>Overflow = new[] { "wrap", "rotate90" }</c>. The converter owns the
/// wire shape.
/// </summary>
[JsonConverter(typeof(LabelOverflowConverter))]
public sealed class LabelOverflow
{
    internal LabelOverflow(string? step, string[]? steps)
    {
        Step = step;
        Steps = steps;
    }

    /// <summary>The one-step form; null when the array form is used.</summary>
    public string? Step { get; }

    /// <summary>The array form; null when the one-step form is used.</summary>
    public string[]? Steps { get; }

    /// <summary>The one-step form; null for a null string.</summary>
    /// <param name="step">The step, such as <c>wrap</c>.</param>
    [return: NotNullIfNotNull(nameof(step))]
    public static implicit operator LabelOverflow?(string? step) => step is null ? null : new(step, null);

    /// <summary>The array form; null for a null array.</summary>
    /// <param name="steps">The steps in the order they are tried.</param>
    [return: NotNullIfNotNull(nameof(steps))]
    public static implicit operator LabelOverflow?(string[]? steps) => steps is null ? null : new(null, steps);
}
