using System.Text.Json;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// The <c>title</c> member of a <see cref="ChartEncoding"/>: the name the channel's axis or key prints, in place of the
/// field's. A string (<c>Title = "Revenue"</c>), or <see cref="None"/>, written as <c>null</c>, which prints no title; a
/// null <see cref="ChartEncoding.Title"/> leaves the member out and keeps the field's name. A theme still decides whether a
/// key is titled at all.
/// </summary>
[JsonConverter(typeof(ChannelTitleConverter))]
public sealed class ChannelTitle
{
    private ChannelTitle(string? text) => Text = text;

    /// <summary>No title: written as <c>null</c>.</summary>
    public static ChannelTitle None { get; } = new(null);

    /// <summary>The title; null for <see cref="None"/>.</summary>
    public string? Text { get; }

    /// <summary>A title; null for a null string, which leaves the member out.</summary>
    /// <param name="text">The title.</param>
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(text))]
    public static implicit operator ChannelTitle?(string? text) => text is null ? null : new(text);

    /// <summary>The title, or an empty string for <see cref="None"/>.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Text ?? string.Empty;
}
