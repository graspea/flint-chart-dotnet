using System.Text.Json;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// The <c>chart_spec.chrome</c> member: the house chrome this chart keeps. <see cref="None"/> (<c>false</c>) drops it all;
/// the object form turns off parts: <see cref="Header"/> (rules, tabs, wordmarks and logos above or beside the title),
/// <see cref="Footer"/> (the closing rule and the house's credit beside the source) and <see cref="Logos"/> (every logo,
/// header or footer; a footer credit then shows its words). Unset follows the theme. The converter owns the wire shape.
/// </summary>
[JsonConverter(typeof(ChartChromeConverter))]
public sealed class ChartChrome
{
    /// <summary>The object form; set its members with an object initializer.</summary>
    public ChartChrome()
    {
    }

    private ChartChrome(bool none) => IsNone = none;

    /// <summary>The <c>false</c> form: the chart keeps none of the house chrome. A new instance on every read.</summary>
    public static ChartChrome None => new(true);

    /// <summary>True for the <c>false</c> form, which is written as <c>false</c> and ignores the other members.</summary>
    public bool IsNone { get; }

    /// <summary>The <c>header</c> member: <c>false</c> drops the rules, tabs, wordmarks and logos above or beside the title.</summary>
    public bool? Header { get; init; }

    /// <summary>The <c>footer</c> member: <c>false</c> drops the closing rule and the house's credit beside the source.</summary>
    public bool? Footer { get; init; }

    /// <summary>The <c>logos</c> member: <c>false</c> drops every logo, header or footer.</summary>
    public bool? Logos { get; init; }

    /// <summary>Members of the object form this version does not model; written after the known members.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}
