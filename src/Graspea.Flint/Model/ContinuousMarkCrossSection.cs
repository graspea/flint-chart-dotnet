using System.Text.Json;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// <c>options.continuousMarkCrossSection</c>: either one number for both axes (<see cref="Uniform"/>) or per-axis
/// values (<see cref="Axes"/>). The converter owns the wire shape.
/// </summary>
[JsonConverter(typeof(ContinuousMarkCrossSectionConverter))]
public sealed partial class ContinuousMarkCrossSection
{
    internal ContinuousMarkCrossSection(double uniform) => Uniform = uniform;

    internal ContinuousMarkCrossSection(double? x, double? y, double? elasticity, double? maxStretch, SeriesCountAxis? seriesCountAxis)
    {
        X = x;
        Y = y;
        Elasticity = elasticity;
        MaxStretch = maxStretch;
        SeriesCountAxis = seriesCountAxis;
    }

    /// <summary>The number form; null when the object form is used.</summary>
    public double? Uniform { get; }

    /// <summary>The <c>x</c> member of the object form.</summary>
    public double? X { get; }

    /// <summary>The <c>y</c> member of the object form.</summary>
    public double? Y { get; }

    /// <summary>The <c>elasticity</c> member of the object form (upstream default 0.3).</summary>
    public double? Elasticity { get; }

    /// <summary>The <c>maxStretch</c> member of the object form (upstream default 1.5).</summary>
    public double? MaxStretch { get; }

    /// <summary>The <c>seriesCountAxis</c> member of the object form.</summary>
    public SeriesCountAxis? SeriesCountAxis { get; }

    /// <summary>Members of the object form this version does not model; written after the known members.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; set; }

    /// <summary>The number form.</summary>
    /// <param name="value">The cross-section for both axes.</param>
    public static implicit operator ContinuousMarkCrossSection(double value) => new(value);

    /// <summary>The object form with per-axis values.</summary>
    /// <param name="x">The <c>x</c> member.</param>
    /// <param name="y">The <c>y</c> member.</param>
    /// <param name="elasticity">The optional <c>elasticity</c> member.</param>
    /// <param name="maxStretch">The optional <c>maxStretch</c> member.</param>
    /// <param name="seriesCountAxis">The optional <c>seriesCountAxis</c> member.</param>
    /// <returns>The object form.</returns>
    public static ContinuousMarkCrossSection Axes(double x, double y, double? elasticity = null, double? maxStretch = null, SeriesCountAxis? seriesCountAxis = null) =>
        new(x, y, elasticity, maxStretch, seriesCountAxis);
}
