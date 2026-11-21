using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// The <c>select</c> member of an <see cref="UpdateTarget"/> selector: marks named by their values. <see cref="Key"/> names
/// one combination, each field equal to its value; <see cref="Where"/> describes a set, each field <c>in</c> a list or within
/// a <c>range</c>, and every field must hold. flint-chart reads a select that has a <c>where</c> member as a set, any other
/// by its key. <see cref="UpdateTarget.Key"/> and <see cref="UpdateTarget.Where"/> write the two forms. The converter owns
/// the wire shape.
/// </summary>
[JsonConverter(typeof(TargetSelectorConverter))]
public sealed class TargetSelector
{
    /// <summary>The <c>key</c> member: field name to the value the mark has, any JSON value. A field the chart reads as time
    /// compares as time; a field drawn as categories matches a number and the same number written as text.</summary>
    public Dictionary<string, JsonNode?>? Key { get; init; }

    /// <summary>The <c>where</c> member: field name to the condition the mark's value meets.</summary>
    public Dictionary<string, FilterValue>? Where { get; init; }

    /// <summary>The <c>visual</c> member: the kind or role of the marks to select; a mark of another kind or role is not
    /// selected.</summary>
    public TargetVisual? Visual { get; init; }

    /// <summary>Members this version does not model; written after the known members.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}
