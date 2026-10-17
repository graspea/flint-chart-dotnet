using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>A theme preset id such as <c>economist</c>. Any non-empty string is accepted and written verbatim, so values newer than this
/// library round-trip; the validator reports unknown ones.</summary>
[JsonConverter(typeof(ThemePresetConverter))]
public readonly partial record struct ThemePreset
{
    /// <summary>Creates a value from its wire string.</summary>
    /// <param name="id">The preset id.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is null or empty.</exception>
    public ThemePreset(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Name = id;
    }

    /// <summary>The wire string; null only for <c>default(ThemePreset)</c>, which cannot be serialised.</summary>
    public string Name { get; }

    /// <summary>The wire string.</summary>
    /// <returns><see cref="Name"/>, or an empty string for <c>default(ThemePreset)</c>.</returns>
    public override string ToString() => Name ?? string.Empty;
}
