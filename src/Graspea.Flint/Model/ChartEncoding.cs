using System.Text.Json;
using System.Text.Json.Serialization;

namespace Graspea.Flint;

/// <summary>One channel binding: a field plus optional type, aggregate, sort and scheme.</summary>
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

    /// <summary>The <c>scheme</c> member: a colour scheme name; <c>default</c> means none.</summary>
    [JsonPropertyName("scheme")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Scheme { get; set; }

    /// <summary>Members this version does not model; written after the known members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}
