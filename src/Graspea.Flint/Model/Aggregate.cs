using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>An encoding <c>aggregate</c> literal such as <c>sum</c>. Any non-empty string is accepted and written verbatim, so values newer than this
/// library round-trip; the validator reports unknown ones.</summary>
[JsonConverter(typeof(AggregateConverter))]
public readonly partial record struct Aggregate
{
    /// <summary>Creates a value from its wire string.</summary>
    /// <param name="name">The literal.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    public Aggregate(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>The wire string; null only for <c>default(Aggregate)</c>, which cannot be serialised.</summary>
    public string Name { get; }

    /// <summary>The wire string.</summary>
    /// <returns><see cref="Name"/>, or an empty string for <c>default(Aggregate)</c>.</returns>
    public override string ToString() => Name ?? string.Empty;
}
