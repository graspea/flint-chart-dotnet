using System.Text.Json;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// The <c>details</c> member of <see cref="AssistedTargeting"/>: <c>true</c> or <c>false</c> (<c>Details = true</c>), or
/// the object form (<c>new TargetDetails { Fields = ["region"], MaxRows = 3 }</c>). The converter owns the wire shape.
/// </summary>
[JsonConverter(typeof(TargetDetailsConverter))]
public sealed class TargetDetails
{
    /// <summary>The object form; set its members with an object initializer.</summary>
    public TargetDetails()
    {
    }

    private TargetDetails(bool enabled) => Enabled = enabled;

    /// <summary>The boolean form; null in the object form.</summary>
    public bool? Enabled { get; }

    /// <summary>The <c>fields</c> member.</summary>
    public string[]? Fields { get; init; }

    /// <summary>The <c>maxRows</c> member.</summary>
    public double? MaxRows { get; init; }

    /// <summary>Members of the object form this version does not model; written after the known members.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; set; }

    /// <summary>The boolean form.</summary>
    /// <param name="enabled">The value.</param>
    public static implicit operator TargetDetails(bool enabled) => new(enabled);
}
