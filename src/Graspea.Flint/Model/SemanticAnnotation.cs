using System.Text.Json;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// A <c>semantic_types</c> value. The converter writes the string form when only <see cref="Type"/> is set and the
/// object form (<c>{"semanticType":…}</c>) otherwise; both forms are read.
/// </summary>
[JsonConverter(typeof(SemanticAnnotationConverter))]
public sealed partial class SemanticAnnotation
{
    /// <summary>The <c>semanticType</c> member: a registry name such as <c>Amount</c>.</summary>
    public required SemanticType Type { get; set; }

    /// <summary>The <c>intrinsicDomain</c> member: the natural <c>[min, max]</c> of the values.</summary>
    public double[]? IntrinsicDomain { get; set; }

    /// <summary>The <c>unit</c> member, such as <c>USD</c>.</summary>
    public string? Unit { get; set; }

    /// <summary>The <c>divergingMidpoint</c> member: the neutral value of a diverging scale.</summary>
    public double? DivergingMidpoint { get; set; }

    /// <summary>The <c>divergingNeutral</c> member (flint-chart 1.1.0+): how far either side of the diverging midpoint still
    /// reads as neither side, for a stepped colour scale (<c>DivergingNeutral = 0.1</c>), or <see cref="Flint.DivergingNeutral.Break"/>
    /// (<c>false</c>), which makes the midpoint a break; unset, the semantic type decides.</summary>
    public DivergingNeutral? DivergingNeutral { get; set; }

    /// <summary>The <c>sortOrder</c> member: the categories in their natural order.</summary>
    public string[]? SortOrder { get; set; }

    /// <summary>The <c>colors</c> member: colours bound to values of this field, wherever they fall, such as
    /// <c>{ "North": "#0793AB" }</c>; matched on the value, trimmed and case-insensitive. Wins over the theme's role inks.</summary>
    public Dictionary<string, string>? Colors { get; set; }

    /// <summary>The <c>polarity</c> member: which way the field's values are good, for a diverging colour; unset, the
    /// semantic type decides.</summary>
    public Polarity? Polarity { get; set; }

    /// <summary>The <c>percentScale</c> member (flint-chart 1.1.0+): how a percentage field's numbers are stored,
    /// <see cref="Flint.PercentScale.Fraction"/> (0.45 is 45%) or <see cref="Flint.PercentScale.Percent"/> (45 is 45%); unset, a
    /// decisive <see cref="IntrinsicDomain"/> says (within ±1 is fractions, reaching 100 is percent points), else the values
    /// do. State it where the values cannot tell, such as a growth rate stored as fractions mostly past 1 (2.4 is 240%).
    /// Read for the <c>Percentage</c> and <c>PercentageChange</c> types.</summary>
    public PercentScale? PercentScale { get; set; }

    /// <summary>Members of the object form this version does not model; written after the known members.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}
