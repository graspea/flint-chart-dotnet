using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint.Theming;

/// <summary>
/// <c>structure.grid.minor</c>: a presence alone, assigned as a string (<c>Minor = "hairline"</c>), or the members of a
/// <see cref="RuleSpec"/> plus <see cref="Divisions"/> and <see cref="On"/>. The converter owns the wire shape.
/// </summary>
[JsonConverter(typeof(MinorGridSpecConverter))]
public sealed class MinorGridSpec
{
    /// <summary>The object form; set its members with an object initializer.</summary>
    public MinorGridSpec()
    {
    }

    private MinorGridSpec(string presence)
    {
        Presence = presence;
        IsPresenceOnly = true;
    }

    /// <summary>True for the string form, which is written as the bare presence.</summary>
    internal bool IsPresenceOnly { get; }

    /// <summary>The <c>color</c> member: the lines' own ink.</summary>
    public string? Color { get; init; }

    /// <summary>The <c>dash</c> member: the dash rhythm in px, <c>[dash, gap]</c>.</summary>
    public double[]? Dash { get; init; }

    /// <summary>The <c>divisions</c> member: intervals each major interval is cut into (upstream default 2).</summary>
    public double? Divisions { get; init; }

    /// <summary>The <c>layer</c> member: <c>behind</c> or <c>front</c>.</summary>
    public string? Layer { get; init; }

    /// <summary>The <c>on</c> member: which grids carry the lines, <c>measure</c> and/or <c>category</c>.</summary>
    public string[]? On { get; init; }

    /// <summary>The presence: the whole value in the string form, the <c>presence</c> member in the object form.</summary>
    public string? Presence { get; init; }

    /// <summary>The <c>style</c> member: <c>solid</c>, <c>dashed</c> or <c>dotted</c>.</summary>
    public string? Style { get; init; }

    /// <summary>The <c>weight</c> member: the stroke width in px.</summary>
    public double? Weight { get; init; }

    /// <summary>Members of the object form this version does not model; written after the known members.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; set; }

    /// <summary>The string form; null for a null string.</summary>
    /// <param name="presence">The presence, such as <c>hairline</c>.</param>
    [return: NotNullIfNotNull(nameof(presence))]
    public static implicit operator MinorGridSpec?(string? presence) => presence is null ? null : new(presence);
}
