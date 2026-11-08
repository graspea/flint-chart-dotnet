using System.Text.Json;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>One channel binding: a field plus optional type, aggregate, sort, scheme, title and colour scale settings.</summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed partial class ChartEncoding
{
    /// <summary>The <c>field</c> member: the row key, matched exactly.</summary>
    [JsonPropertyName("field")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Field { get; set; }

    /// <summary>The <c>type</c> member: <c>quantitative</c>, <c>nominal</c>, <c>ordinal</c> or <c>temporal</c>.</summary>
    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EncodingType? Type { get; set; }

    /// <summary>The <c>aggregate</c> member: <c>count</c>, <c>sum</c>, <c>average</c> or <c>mean</c>.</summary>
    [JsonPropertyName("aggregate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Aggregate? Aggregate { get; set; }

    /// <summary>The <c>sortOrder</c> member: <c>ascending</c> or <c>descending</c>.</summary>
    [JsonPropertyName("sortOrder")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SortOrder? SortOrder { get; set; }

    /// <summary>The <c>sortBy</c> member: <c>x</c>, <c>y</c>, <c>color</c> or a JSON-encoded array string (<see cref="Flint.SortBy.ExplicitOrder"/>).</summary>
    [JsonPropertyName("sortBy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SortBy { get; set; }

    /// <summary>The <c>scheme</c> member: a colour scheme name (<c>default</c> means none) or the colour stops, low end first.</summary>
    [JsonPropertyName("scheme")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ColorScheme? ColorScheme { get; set; }

    /// <summary>The scheme name of <see cref="ColorScheme"/>; null when the scheme is a list of colour stops. Never written
    /// as a member of its own.</summary>
    [JsonIgnore]
    [Obsolete("Use ChartEncoding.ColorScheme, which also holds an array of colour stops.", DiagnosticId = "FLINT0002", UrlFormat = "https://github.com/graspea/flint-chart-dotnet/blob/main/docs/deprecations.md#{0}")]
    public string? Scheme
    {
        get => ColorScheme?.Name;
        set => ColorScheme = value;
    }

    /// <summary>The <c>title</c> member: the name the channel's axis or key prints, in place of the field's; <see cref="ChannelTitle.None"/> prints none.</summary>
    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelTitle? Title { get; set; }

    /// <summary>The <c>reverse</c> member, colour channel: run the scale the other way round.</summary>
    [JsonPropertyName("reverse")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Reverse { get; set; }

    /// <summary>The <c>mid</c> member, colour channel: the value a diverging scale pivots on, for this chart (the field's <c>divergingMidpoint</c>).</summary>
    [JsonPropertyName("mid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Mid { get; set; }

    /// <summary>The <c>domain</c> member, colour channel: the measure extent the ramp spans, <c>[low, high]</c>, in place of the data's.</summary>
    [JsonPropertyName("domain")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? Domain { get; set; }

    /// <summary>The <c>bins</c> member, colour channel: quantize a continuous measure into this many bins; <c>0</c> keeps the ramp continuous.</summary>
    [JsonPropertyName("bins")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Bins { get; set; }

    /// <summary>The <c>colors</c> member, colour channel: colours bound to values, for this chart (the field's <c>colors</c>), such as <c>{ "Win": "#2a9d8f" }</c>.</summary>
    [JsonPropertyName("colors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(VerbatimKeyDictionaryConverter<string>))]
    public Dictionary<string, string>? Colors { get; set; }

    /// <summary>Members this version does not model; written after the known members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}
