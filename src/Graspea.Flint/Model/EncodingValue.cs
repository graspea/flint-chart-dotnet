using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// An <c>encodings</c> value: one <see cref="ChartEncoding"/>, or a static series (an array of encodings). The
/// converter owns the wire shape: a single encoding is written as an object, a series as an array.
/// </summary>
[JsonConverter(typeof(EncodingValueConverter))]
public sealed partial class EncodingValue
{
    private EncodingValue(ChartEncoding[] encodings, bool isSeries)
    {
        Encodings = encodings;
        IsSeries = isSeries;
    }

    /// <summary>The encodings; exactly one unless <see cref="IsSeries"/>.</summary>
    public IReadOnlyList<ChartEncoding> Encodings { get; }

    /// <summary>True when the wire form is an array (a static series), even with one entry.</summary>
    public bool IsSeries { get; }

    /// <summary>One encoding.</summary>
    /// <param name="e">The encoding.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="e"/> is null.</exception>
    public static EncodingValue Single(ChartEncoding e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return new([e], false);
    }

    /// <summary>A static series: the wire form is an array, even with one entry.</summary>
    /// <param name="e">The encodings.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="e"/> or one of its entries is null.</exception>
    public static EncodingValue Series(params ChartEncoding[] e)
    {
        ArgumentNullException.ThrowIfNull(e);
        foreach (var entry in e)
        {
            ArgumentNullException.ThrowIfNull(entry, nameof(e));
        }

        return new([.. e], true);
    }

    /// <summary>One encoding bound to <paramref name="field"/>: <c>{"field":…}</c>.</summary>
    /// <param name="field">The field name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> is null.</exception>
    public static implicit operator EncodingValue(string field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return Single(new ChartEncoding { Field = field });
    }

    /// <summary>One encoding.</summary>
    /// <param name="e">The encoding.</param>
    /// <exception cref="ArgumentNullException"><paramref name="e"/> is null.</exception>
    public static implicit operator EncodingValue(ChartEncoding e) => Single(e);
}
