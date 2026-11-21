using System.Text.Json;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// The <c>sparseFill</c> member of <see cref="AssembleOptions"/> and <see cref="Theming.ThemeCompileDefaults"/>: whether an x
/// axis of few bands fills at least a share of the room the canvas leaves its plot (default <c>false</c>): the bands thicken
/// past the sparse-fit ceiling until the plot spans <see cref="Share"/> of that room. One view, y not banded. <c>true</c>
/// turns it on (<c>SparseFill = true</c>), and the object form turns it on with its own tuning
/// (<c>new SparseFill { Share = 0.9 }</c>). The converter owns the wire shape.
/// </summary>
[JsonConverter(typeof(SparseFillConverter))]
public sealed class SparseFill
{
    /// <summary>The object form; set its members with an object initializer.</summary>
    public SparseFill()
    {
    }

    private SparseFill(bool enabled) => Enabled = enabled;

    /// <summary>The boolean form; null in the object form.</summary>
    public bool? Enabled { get; }

    /// <summary>The <c>barCap</c> member: the widest a band grows where it holds bars, in px (200).</summary>
    public double? BarCap { get; init; }

    /// <summary>The <c>pointCap</c> member: the widest a band grows where it holds points, in px (360).</summary>
    public double? PointCap { get; init; }

    /// <summary>The <c>share</c> member: the share of the room the canvas leaves its plot that the plot spans (0.85).</summary>
    public double? Share { get; init; }

    /// <summary>Members of the object form this version does not model; written after the known members.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; set; }

    /// <summary>The boolean form.</summary>
    /// <param name="enabled"><c>true</c> fills the room with the default tuning.</param>
    public static implicit operator SparseFill(bool enabled) => new(enabled);
}
