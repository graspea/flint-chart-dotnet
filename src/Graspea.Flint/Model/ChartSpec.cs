using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>The <c>chart_spec</c> member of <see cref="ChartAssemblyInput"/>.</summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed partial class ChartSpec
{
    /// <summary>The <c>chartType</c> member: the exact template name.</summary>
    [JsonPropertyName("chartType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required ChartType ChartType { get; set; }

    /// <summary>The <c>title</c> member.</summary>
    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    /// <summary>The <c>subtitle</c> member.</summary>
    [JsonPropertyName("subtitle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Subtitle { get; set; }

    /// <summary>The <c>encodings</c> member: channel name to field binding.</summary>
    [JsonPropertyName("encodings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonConverter(typeof(VerbatimKeyDictionaryConverter<EncodingValue>))]
    public Dictionary<string, EncodingValue> Encodings { get; set; } = new();

    /// <summary>The <c>baseSize</c> member: the plot size before clamping to <see cref="CanvasSize"/>.</summary>
    [JsonPropertyName("baseSize")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChartSize? BaseSize { get; set; }

    /// <summary>The <c>canvasSize</c> member.</summary>
    [JsonPropertyName("canvasSize")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChartSize? CanvasSize { get; set; }

    /// <summary>The <c>chartProperties</c> member: an open namespace of template properties, written as given.</summary>
    [JsonPropertyName("chartProperties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(VerbatimKeyDictionaryConverter<JsonNode?>))]
    public Dictionary<string, JsonNode?>? ChartProperties { get; set; }

    /// <summary>Members this version does not model; written after the known members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}
