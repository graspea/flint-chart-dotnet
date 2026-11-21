using System.Text.Json;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// The <c>defaults</c> member of an <see cref="InteractionSpec"/>: the behaviour every mounted chart has without an entry (a
/// tooltip that reads the nearest mark, a legend click that dims a group, and on a dense chart a Ctrl/⌘-wheel zoom and drag
/// pan), on unless turned off. <c>false</c> turns all three off (<c>Defaults = false</c>), and the object form turns off the
/// ones it names (<c>new InteractionDefaults { Tooltip = false }</c>). The converter owns the wire shape.
/// </summary>
[JsonConverter(typeof(InteractionDefaultsConverter))]
public sealed class InteractionDefaults
{
    /// <summary>The object form; set its members with an object initializer.</summary>
    public InteractionDefaults()
    {
    }

    private InteractionDefaults(bool enabled) => Enabled = enabled;

    /// <summary>The boolean form; null in the object form.</summary>
    public bool? Enabled { get; }

    /// <summary>The <c>legend</c> member: <c>false</c> turns off the legend click that dims a group.</summary>
    public bool? Legend { get; init; }

    /// <summary>The <c>tooltip</c> member: <c>false</c> turns off the tooltip that reads the nearest mark.</summary>
    public bool? Tooltip { get; init; }

    /// <summary>Members of the object form this version does not model; written after the known members. flint-chart 1.1.1
    /// rejects a <c>defaults</c> object with any member other than a boolean <c>tooltip</c> or <c>legend</c> (validation
    /// reports <c>invalid_interaction_spec</c> and mounting throws).</summary>
    public Dictionary<string, JsonElement>? Extensions { get; set; }

    /// <summary>The boolean form.</summary>
    /// <param name="enabled"><c>false</c> turns all three behaviours off.</param>
    public static implicit operator InteractionDefaults(bool enabled) => new(enabled);
}
