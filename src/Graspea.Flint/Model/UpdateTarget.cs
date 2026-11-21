using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// The marks an update op acts on (the <c>target</c> of <c>annotate-text</c>, the <c>targets</c> of <c>set-style</c>, of an
/// enclosure and of a freeform clone). Either a resolved target, as an interaction reports it: the visual role of its marks
/// and the values each represents (<see cref="Visual"/> and <see cref="Elements"/>;
/// <c>new UpdateTarget { Visual = new TargetVisual { Kind = "mark", Role = "bar" }, Elements = [new SemanticElement { Value = new() { ["region"] = "Asia" } }] }</c>),
/// or a selector (<see cref="Select"/>), marks named by their values: <see cref="Key"/> names one combination, each field
/// equal to its value (<c>UpdateTarget.Key(new() { ["region"] = "Asia" })</c>), and <see cref="Where"/> describes a set,
/// each field <c>in</c> a list or within a <c>range</c>, and every field must hold. Ops that act on one mark (a note, an
/// order) take a key; <c>set-style</c> takes either. flint-chart reads a target that has a <c>select</c> member as a
/// selector, any other as a resolved target. The converter owns the wire shape.
/// </summary>
[JsonConverter(typeof(UpdateTargetConverter))]
public sealed class UpdateTarget
{
    /// <summary>A target with no member set; set the members of a resolved target with an object initializer.</summary>
    public UpdateTarget()
    {
    }

    /// <summary>The <c>visual</c> member of a resolved target: the visual role of its marks.</summary>
    public TargetVisual? Visual { get; init; }

    /// <summary>The <c>elements</c> member of a resolved target: the values each of its marks represents, with their
    /// contributing records.</summary>
    public SemanticElement[]? Elements { get; init; }

    /// <summary>The <c>select</c> member: set, the target is a selector, which names marks by their values; the factories
    /// <see cref="Key"/> and <see cref="Where"/> set it.</summary>
    public TargetSelector? Select { get; init; }

    /// <summary>Members this version does not model; written after the known members.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; set; }

    /// <summary>A selector that names one combination of values, each field equal to its value; a field drawn as categories
    /// matches a number and the same number written as text.</summary>
    /// <param name="key">Field name to value, such as <c>new() { ["region"] = "Asia", ["year"] = 2024 }</c>.</param>
    /// <param name="visual">The kind or role of the marks to select, when the fields name marks of several kinds.</param>
    /// <returns>The target <c>{ "select": { "key": …, "visual"?: … } }</c>.</returns>
    public static UpdateTarget Key(Dictionary<string, JsonNode?> key, TargetVisual? visual = null) =>
        new() { Select = new TargetSelector { Key = key, Visual = visual } };

    /// <summary>A selector that describes a set of marks: each field <c>in</c> a list or within a <c>range</c>, and every field
    /// must hold.</summary>
    /// <param name="where">Field name to condition, such as <c>new() { ["region"] = new FilterValue { In = ["Asia", "Europe"] } }</c>.</param>
    /// <param name="visual">The kind or role of the marks to select, when the fields name marks of several kinds.</param>
    /// <returns>The target <c>{ "select": { "where": …, "visual"?: … } }</c>.</returns>
    public static UpdateTarget Where(Dictionary<string, FilterValue> where, TargetVisual? visual = null) =>
        new() { Select = new TargetSelector { Where = where, Visual = visual } };
}
