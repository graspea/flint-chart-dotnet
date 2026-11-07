using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>A semantic annotation <c>polarity</c> literal: which way the field's values are good, for a diverging colour
/// (<c>higher-is-better</c> or <c>lower-is-better</c>). Any non-empty string is accepted and written verbatim, so values
/// newer than this library round-trip.</summary>
[JsonConverter(typeof(PolarityConverter))]
public readonly partial record struct Polarity
{
    /// <summary>Creates a value from its wire string.</summary>
    /// <param name="name">The literal.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    public Polarity(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>The wire string; null only for <c>default(Polarity)</c>, which cannot be serialised.</summary>
    public string Name { get; }

    /// <summary>The wire string.</summary>
    /// <returns><see cref="Name"/>, or an empty string for <c>default(Polarity)</c>.</returns>
    public override string ToString() => Name ?? string.Empty;
}
