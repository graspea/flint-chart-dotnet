using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint.Theming;

/// <summary>
/// A length in a theme: px as a number (<see cref="Number"/>), or a string with its unit (<see cref="Text"/>), such as
/// <c>"12px"</c> for a gap or <c>"40%"</c> for a label limit. Assign either directly: <c>Gap = 12</c> or
/// <c>RowLimit = "25%"</c>. The converter owns the wire shape.
/// </summary>
[JsonConverter(typeof(ThemeLengthConverter))]
public sealed class ThemeLength
{
    internal ThemeLength(double? number, string? text)
    {
        Number = number;
        Text = text;
    }

    /// <summary>The number form, in px; null when the string form is used.</summary>
    public double? Number { get; }

    /// <summary>The string form with its unit; null when the number form is used.</summary>
    public string? Text { get; }

    /// <summary>The number form.</summary>
    /// <param name="number">The length in px.</param>
    public static implicit operator ThemeLength(double number) => new(number, null);

    /// <summary>The string form; null for a null string.</summary>
    /// <param name="text">The length with its unit, such as <c>"12px"</c> or <c>"40%"</c>.</param>
    [return: NotNullIfNotNull(nameof(text))]
    public static implicit operator ThemeLength?(string? text) => text is null ? null : new(null, text);
}
