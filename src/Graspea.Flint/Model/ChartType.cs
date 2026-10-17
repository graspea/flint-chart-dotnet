using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>A Flint chart template name such as <c>Line Chart</c>; exact and case-sensitive. Any non-empty string is accepted and written verbatim, so values newer than this
/// library round-trip; the validator reports unknown ones.</summary>
[JsonConverter(typeof(ChartTypeConverter))]
public readonly partial record struct ChartType
{
    /// <summary>Creates a value from its wire string.</summary>
    /// <param name="name">The exact template name.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    public ChartType(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>The wire string; null only for <c>default(ChartType)</c>, which cannot be serialised.</summary>
    public string Name { get; }

    /// <summary>The wire string.</summary>
    /// <returns><see cref="Name"/>, or an empty string for <c>default(ChartType)</c>.</returns>
    public override string ToString() => Name ?? string.Empty;
}
