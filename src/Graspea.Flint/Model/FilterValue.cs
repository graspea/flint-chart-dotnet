using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// One field's condition in the <c>where</c> of a <see cref="TargetSelector"/>: the values a reader keeps (<see cref="In"/>;
/// <c>new FilterValue { In = ["Asia", "Europe"] }</c>), or an inclusive range whose ends compare as numbers or dates
/// (<see cref="Range"/>; <c>new FilterValue { Range = ["2025-03", "2025-06"] }</c>). flint-chart reads a condition that has
/// an <c>in</c> member as a set of values, any other as a range. The converter owns the wire shape.
/// </summary>
[JsonConverter(typeof(FilterValueConverter))]
public sealed class FilterValue
{
    /// <summary>The <c>in</c> member: the values a mark's value must be one of, any JSON values.</summary>
    public JsonNode?[]? In { get; init; }

    /// <summary>The <c>range</c> member: the low and the high end, inclusive, any JSON values; an end that is neither a number
    /// nor a date leaves that side open.</summary>
    public JsonNode?[]? Range { get; init; }

    /// <summary>Members this version does not model; written after the known members.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}
