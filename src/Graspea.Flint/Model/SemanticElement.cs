using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// One element of a resolved <see cref="UpdateTarget"/>: the backend-independent meaning and provenance of one chart element.
/// Reason from <see cref="Value"/> and <see cref="Records"/>, never from renderer metadata
/// (<c>new SemanticElement { Value = new() { ["region"] = "Asia" } }</c>). The converter owns the wire shape.
/// </summary>
[JsonConverter(typeof(SemanticElementConverter))]
public sealed class SemanticElement
{
    /// <summary>The <c>value</c> member: the values the mark's channels represent, or a semantic control such as a legend
    /// item; field name to any JSON value.</summary>
    public Dictionary<string, JsonNode?>? Value { get; init; }

    /// <summary>The <c>records</c> member: the contributing input rows when provenance is available; zero or many may support
    /// one value.</summary>
    public Dictionary<string, JsonNode?>[]? Records { get; init; }

    /// <summary>Members this version does not model; written after the known members.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}
