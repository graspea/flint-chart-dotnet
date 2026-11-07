using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint.Theming;

/// <summary>
/// A rule (a frame, a baseline, a grid): a presence alone (<c>omit</c>, <c>hairline</c>, <c>quiet</c>, <c>full</c>,
/// <c>emphasised</c>), assigned as a string (<c>Frame = "quiet"</c>), or a rule with its own members
/// (<c>new RuleSpec { Presence = "full", Weight = 2 }</c>). The converter owns the wire shape.
/// </summary>
[JsonConverter(typeof(RuleSpecConverter))]
public sealed class RuleSpec
{
    /// <summary>The object form; set its members with an object initializer.</summary>
    public RuleSpec()
    {
    }

    private RuleSpec(string presence)
    {
        Presence = presence;
        IsPresenceOnly = true;
    }

    /// <summary>True for the string form, which is written as the bare presence.</summary>
    internal bool IsPresenceOnly { get; }

    /// <summary>The <c>color</c> member: this rule's own ink, in place of the role's.</summary>
    public string? Color { get; init; }

    /// <summary>The <c>dash</c> member: the dash rhythm in px, <c>[dash, gap]</c>.</summary>
    public double[]? Dash { get; init; }

    /// <summary>The <c>layer</c> member, a grid only: <c>behind</c> or <c>front</c>.</summary>
    public string? Layer { get; init; }

    /// <summary>The presence: the whole value in the string form, the <c>presence</c> member in the object form.</summary>
    public string? Presence { get; init; }

    /// <summary>The <c>style</c> member: <c>solid</c>, <c>dashed</c> or <c>dotted</c>.</summary>
    public string? Style { get; init; }

    /// <summary>The <c>weight</c> member: the stroke width in px before the presence scales it.</summary>
    public double? Weight { get; init; }

    /// <summary>Members of the object form this version does not model; written after the known members.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; set; }

    /// <summary>The string form; null for a null string.</summary>
    /// <param name="presence">The presence, such as <c>quiet</c>.</param>
    [return: NotNullIfNotNull(nameof(presence))]
    public static implicit operator RuleSpec?(string? presence) => presence is null ? null : new(presence);
}
