using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// <c>encoding.scheme</c>: a colour scheme name (<see cref="Name"/>; <c>default</c> means none) or the colour stops a
/// ramp or palette runs through, low end first (<see cref="Stops"/>). Assign either directly:
/// <c>ColorScheme = "viridis"</c> or <c>ColorScheme = new[] { "#c0392b", "#f2f2f2", "#2e75b6" }</c>. The converter owns
/// the wire shape.
/// </summary>
[JsonConverter(typeof(ColorSchemeConverter))]
public sealed class ColorScheme
{
    internal ColorScheme(string? name, string[]? stops)
    {
        Name = name;
        Stops = stops;
    }

    /// <summary>The scheme name; null when the stops form is used.</summary>
    public string? Name { get; }

    /// <summary>The colour stops, low end first; null when the name form is used.</summary>
    public string[]? Stops { get; }

    /// <summary>The name form; null for a null string.</summary>
    /// <param name="name">The scheme name, such as <c>viridis</c>.</param>
    [return: NotNullIfNotNull(nameof(name))]
    public static implicit operator ColorScheme?(string? name) => name is null ? null : new(name, null);

    /// <summary>The stops form; null for a null array.</summary>
    /// <param name="stops">The colour stops, low end first.</param>
    [return: NotNullIfNotNull(nameof(stops))]
    public static implicit operator ColorScheme?(string[]? stops) => stops is null ? null : new(null, stops);

    /// <summary>The name, or the stops joined with a comma and a space.</summary>
    public override string ToString() => Name ?? string.Join(", ", Stops ?? []);
}
