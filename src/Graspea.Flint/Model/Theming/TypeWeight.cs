using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint.Theming;

/// <summary>
/// A font weight: a keyword (<c>thin</c>, <c>extralight</c>, <c>light</c>, <c>regular</c>, <c>medium</c>, <c>semibold</c>,
/// <c>bold</c>, <c>extrabold</c>, <c>black</c>; <see cref="Keyword"/>) or the CSS number 100 to 900 (<see cref="Number"/>).
/// Assign either directly: <c>Weight = "bold"</c> or <c>Weight = 700</c>. The converter owns the wire shape.
/// </summary>
[JsonConverter(typeof(TypeWeightConverter))]
public sealed class TypeWeight
{
    internal TypeWeight(string? keyword, double? number)
    {
        Keyword = keyword;
        Number = number;
    }

    /// <summary>The keyword form; null when the number form is used.</summary>
    public string? Keyword { get; }

    /// <summary>The number form; null when the keyword form is used.</summary>
    public double? Number { get; }

    /// <summary>The keyword form; null for a null string.</summary>
    /// <param name="keyword">The weight keyword, such as <c>bold</c>.</param>
    [return: NotNullIfNotNull(nameof(keyword))]
    public static implicit operator TypeWeight?(string? keyword) => keyword is null ? null : new(keyword, null);

    /// <summary>The number form.</summary>
    /// <param name="number">The CSS weight, 100 to 900.</param>
    public static implicit operator TypeWeight(double number) => new(null, number);
}
