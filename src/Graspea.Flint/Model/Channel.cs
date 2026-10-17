using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>An encoding channel name such as <c>x</c> or <c>strokeDash</c>. Any non-empty string is accepted and written verbatim, so values newer than this
/// library round-trip; the validator reports unknown ones.</summary>
[JsonConverter(typeof(ChannelConverter))]
public readonly partial record struct Channel
{
    /// <summary>Creates a value from its wire string.</summary>
    /// <param name="name">The channel name.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    public Channel(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>The wire string; null only for <c>default(Channel)</c>, which cannot be serialised.</summary>
    public string Name { get; }

    /// <summary>The channel name, for use as an <c>encodings</c> key.</summary>
    /// <param name="channel">The channel.</param>
    public static implicit operator string(Channel channel) => channel.ToString();

    /// <summary>The wire string.</summary>
    /// <returns><see cref="Name"/>, or an empty string for <c>default(Channel)</c>.</returns>
    public override string ToString() => Name ?? string.Empty;
}
