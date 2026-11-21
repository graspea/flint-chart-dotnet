using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>A semantic annotation <c>percentScale</c> literal (flint-chart 1.1.0+): how a percentage field's numbers are
/// stored, <c>fraction</c> (0.45 is 45%) or <c>percent</c> (45 is 45%). Upstream reads exactly these two; unset, a decisive
/// <c>intrinsicDomain</c> says, else the values do. Any non-empty string is accepted and written verbatim, so values newer
/// than this library round-trip.</summary>
[JsonConverter(typeof(PercentScaleConverter))]
public readonly partial record struct PercentScale
{
    /// <summary>Creates a value from its wire string.</summary>
    /// <param name="name">The literal.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    public PercentScale(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>The wire string; null only for <c>default(PercentScale)</c>, which cannot be serialised.</summary>
    public string Name { get; }

    /// <summary>The wire string.</summary>
    /// <returns><see cref="Name"/>, or an empty string for <c>default(PercentScale)</c>.</returns>
    public override string ToString() => Name ?? string.Empty;
}
