using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// The <c>strokeDash</c> of an overlay style: the dash and gap lengths in px (<see cref="Lengths"/>), or a word
/// (<see cref="Keyword"/>): <c>dash</c> (or <c>dashed</c>) or <c>dot</c> (or <c>dotted</c>); any other word is kept as written.
/// Assign either directly: <c>StrokeDash = "dot"</c> or <c>StrokeDash = new double[] { 4, 2 }</c>. The converter owns the wire
/// shape.
/// </summary>
[JsonConverter(typeof(StrokeDashConverter))]
public sealed class StrokeDash
{
    internal StrokeDash(string? keyword, double[]? lengths)
    {
        Keyword = keyword;
        Lengths = lengths;
    }

    /// <summary>The word form; null when the lengths are used.</summary>
    public string? Keyword { get; }

    /// <summary>The dash and gap lengths in px, in turn; null when the word form is used.</summary>
    public double[]? Lengths { get; }

    /// <summary>The word form; null for a null string.</summary>
    /// <param name="keyword">The word, such as <c>dot</c>.</param>
    [return: NotNullIfNotNull(nameof(keyword))]
    public static implicit operator StrokeDash?(string? keyword) => keyword is null ? null : new(keyword, null);

    /// <summary>The lengths form; null for a null array.</summary>
    /// <param name="lengths">The dash and gap lengths in px, such as <c>[4, 2]</c>.</param>
    [return: NotNullIfNotNull(nameof(lengths))]
    public static implicit operator StrokeDash?(double[]? lengths) => lengths is null ? null : new(null, lengths);
}
