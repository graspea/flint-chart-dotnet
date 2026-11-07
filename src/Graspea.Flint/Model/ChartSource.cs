using System.Text.Json;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// The <c>chart_spec.source</c> member: where the data comes from, drawn as the chart's source line in the house's way (the
/// theme's <c>source</c>). A string (<c>Source = "Survey of 2024"</c>) or the object form with a link
/// (<c>new ChartSource { Text = "Open data", Href = "https://example.com/data" }</c>); each is written in the form it was
/// given. Give it only where the source is known. The converter owns the wire shape.
/// </summary>
[JsonConverter(typeof(ChartSourceConverter))]
public sealed class ChartSource
{
    /// <summary>The object form; set its members with an object initializer.</summary>
    public ChartSource()
    {
    }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    private ChartSource(string text)
    {
        Text = text;
        IsTextOnly = true;
    }

    /// <summary>True for the string form, which is written as the bare text.</summary>
    internal bool IsTextOnly { get; }

    /// <summary>The text: the whole value in the string form, the <c>text</c> member in the object form.</summary>
    public required string Text { get; init; }

    /// <summary>The <c>href</c> member: a link for the source line.</summary>
    public string? Href { get; init; }

    /// <summary>Members of the object form this version does not model; written after the known members.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; set; }

    /// <summary>The string form; null for a null string.</summary>
    /// <param name="text">The source line.</param>
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(text))]
    public static implicit operator ChartSource?(string? text) => text is null ? null : new(text);
}
