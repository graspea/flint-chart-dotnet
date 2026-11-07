using System.Text.Json;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// The <c>assistedTargeting</c> member of an <see cref="InteractionSpec"/>: presets assist by default; <c>false</c>
/// requires direct hits (<c>AssistedTargeting = false</c>), and the object form tunes the assistance
/// (<c>new AssistedTargeting { MaxDistance = 24 }</c>). The converter owns the wire shape.
/// </summary>
[JsonConverter(typeof(AssistedTargetingConverter))]
public sealed class AssistedTargeting
{
    /// <summary>The object form; set its members with an object initializer.</summary>
    public AssistedTargeting()
    {
    }

    private AssistedTargeting(bool enabled) => Enabled = enabled;

    /// <summary>The boolean form; null in the object form.</summary>
    public bool? Enabled { get; }

    /// <summary>The <c>details</c> member: <c>true</c>, <c>false</c> or the fields and rows to show.</summary>
    public TargetDetails? Details { get; init; }

    /// <summary>The <c>indicator</c> member.</summary>
    public bool? Indicator { get; init; }

    /// <summary>The <c>maxDistance</c> member: a hard override for eligible preset distances, in renderer pixels.</summary>
    public double? MaxDistance { get; init; }

    /// <summary>Members of the object form this version does not model; written after the known members.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; set; }

    /// <summary>The boolean form.</summary>
    /// <param name="enabled"><c>false</c> requires direct hits.</param>
    public static implicit operator AssistedTargeting(bool enabled) => new(enabled);
}
