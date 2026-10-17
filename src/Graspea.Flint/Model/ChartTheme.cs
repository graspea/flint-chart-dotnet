using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// The <c>theme_spec</c> member: a preset id (written as a string) or a custom theme (written as an object). Applied by the Vega-Lite and Plotly backends only. The
/// converter owns the wire shape.
/// </summary>
[JsonConverter(typeof(ChartThemeConverter))]
public sealed partial class ChartTheme
{
    private ChartTheme(ThemePreset preset) => Preset = preset;

    private ChartTheme(Theming.ThemeSpec spec) => Spec = spec;

    /// <summary>The preset; null when a custom theme is set.</summary>
    public ThemePreset? Preset { get; }

    /// <summary>The custom theme; null when a preset is set.</summary>
    public Theming.ThemeSpec? Spec { get; }

    /// <summary>A preset, written as its id.</summary>
    /// <param name="preset">The preset.</param>
    public static implicit operator ChartTheme(ThemePreset preset) => new(preset);

    /// <summary>A custom theme, written as an object.</summary>
    /// <param name="spec">The theme.</param>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is null.</exception>
    public static implicit operator ChartTheme(Theming.ThemeSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return new(spec);
    }
}
