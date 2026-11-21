using System.Text.Json;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// The <c>titleWrap</c> member of <see cref="AssembleOptions"/> and <see cref="Theming.ThemeCompileDefaults"/>: whether a
/// themed title wraps to the drawing rather than to the canvas (default <c>false</c>): its block is the plot with what stands
/// beside it, never wider than the canvas unless the drawing is. <c>true</c> turns it on (<c>TitleWrap = true</c>), and the
/// object form turns it on with its own bounds (<c>new TitleWrap { MinWidth = 280 }</c>). The converter owns the wire shape.
/// </summary>
[JsonConverter(typeof(TitleWrapConverter))]
public sealed class TitleWrap
{
    /// <summary>The object form; set its members with an object initializer.</summary>
    public TitleWrap()
    {
    }

    private TitleWrap(bool enabled) => Enabled = enabled;

    /// <summary>The boolean form; null in the object form.</summary>
    public bool? Enabled { get; }

    /// <summary>The <c>floor</c> member: the block is never narrower than this share of the canvas (0.6).</summary>
    public double? Floor { get; init; }

    /// <summary>The <c>minWidth</c> member: the block is never narrower than this, in px (320).</summary>
    public double? MinWidth { get; init; }

    /// <summary>The <c>overhang</c> member: how far the title may overhang its block, as a factor of the block (1.15).</summary>
    public double? Overhang { get; init; }

    /// <summary>Members of the object form this version does not model; written after the known members.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; set; }

    /// <summary>The boolean form.</summary>
    /// <param name="enabled"><c>true</c> wraps the title to the drawing with the default bounds.</param>
    public static implicit operator TitleWrap(bool enabled) => new(enabled);
}
