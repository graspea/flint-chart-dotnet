using System.Text.Json;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// The <c>visual</c> member of an <see cref="UpdateTarget"/> (upstream <c>SemanticTarget['visual']</c>): the visual role of
/// the marks. A resolved target carries both members; a selector's <see cref="TargetSelector.Visual"/> may carry either, and
/// then selects only the marks of that kind and role (on a line drawn with points, <c>Kind = "path"</c> anchors a note to the
/// line rather than to the point). The converter owns the wire shape.
/// </summary>
[JsonConverter(typeof(TargetVisualConverter))]
public sealed class TargetVisual
{
    /// <summary>The <c>kind</c> member: <c>mark</c>, <c>path</c>, <c>region</c>, <c>widget</c>, <c>handle</c>, <c>legend</c>,
    /// <c>axis</c>, <c>chart</c>, <c>title</c> or <c>header</c>; any other string is kept as written.</summary>
    public string? Kind { get; init; }

    /// <summary>The <c>role</c> member: the role the chart gives the marks.</summary>
    public string? Role { get; init; }

    /// <summary>Members this version does not model; written after the known members.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}
