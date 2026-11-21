using System.Globalization;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// The <c>divergingNeutral</c> member of a <see cref="SemanticAnnotation"/> (flint-chart 1.1.0+): how far either side of the
/// diverging midpoint still reads as neither side, for a stepped colour scale. A number (<c>DivergingNeutral = 0.1</c>,
/// <see cref="Width"/>) is the half-width of one neutral class, in the field's units: <c>0.1</c> makes correlations within
/// ±0.1 one neutral class, and <c>0</c> leaves no neutral class. <see cref="Break"/>, written as <c>false</c>, makes the
/// midpoint a break, so every class is one side or the other and a small gain never looks like a small loss. A null
/// <see cref="SemanticAnnotation.DivergingNeutral"/> leaves the member out, and the semantic type decides: a type that always
/// diverges (Correlation, Sentiment) or a stated midpoint gets a neutral class a step wide; one that diverges where its values
/// cross a sign (Profit, PercentageChange, Temperature) breaks at it.
/// </summary>
/// <remarks>
/// flint-chart reads a number of at least 0 as the half-width and anything else as a break; it reads <c>null</c> as a break
/// too, so a JSON <c>null</c> reads as <see cref="Break"/> and is written as <c>false</c>. A negative number is kept as
/// written; <see cref="Validation.ChartValidator"/> reports it (<c>negative_diverging_neutral</c>). The Vega-Lite and ECharts
/// assemblers draw the neutral class. The converter owns the wire shape.
/// </remarks>
[JsonConverter(typeof(DivergingNeutralConverter))]
public sealed class DivergingNeutral
{
    private DivergingNeutral(double? width) => Width = width;

    /// <summary>The <c>false</c> form: the midpoint is a break, with no neutral class.</summary>
    public static DivergingNeutral Break { get; } = new(null);

    /// <summary>The half-width of the neutral class, in the field's units; null for <see cref="Break"/>.</summary>
    public double? Width { get; }

    /// <summary>True for <see cref="Break"/>, which is written as <c>false</c>.</summary>
    public bool IsBreak => Width is null;

    /// <summary>The number form.</summary>
    /// <param name="width">The half-width of the neutral class, in the field's units.</param>
    public static implicit operator DivergingNeutral(double width) => new(width);

    /// <summary>The number in invariant form, or <c>false</c> for <see cref="Break"/>.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Width is { } width ? width.ToString("R", CultureInfo.InvariantCulture) : "false";
}
