using System.Text.Json;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// The document flint-chart assembles: <c>data</c>, <c>semantic_types</c>, <c>chart_spec</c>, <c>theme_spec</c>,
/// <c>options</c> and <c>field_display_names</c>. Serialise it with <see cref="Serialization.FlintJson"/>; the wire
/// shape is attached to the types, so any <see cref="JsonSerializerOptions"/> produce the same document.
/// </summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed partial class ChartAssemblyInput
{
    /// <summary>The <c>data</c> member: row values, a URL or streamed rows; null for a document without rows, such as a
    /// spec drafted before the data exists, which is written without <c>data</c>. A <c>"data": null</c> reads as null.
    /// flint-chart throws without data, so <see cref="Validation.ChartValidator"/> reports <c>data_missing</c>.</summary>
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChartData? Data { get; set; }

    /// <summary>The <c>semantic_types</c> member: field name to semantic type.</summary>
    [JsonPropertyName("semantic_types")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(VerbatimKeyDictionaryConverter<SemanticAnnotation>))]
    public Dictionary<string, SemanticAnnotation>? SemanticTypes { get; set; }

    /// <summary>The <c>chart_spec</c> member: chart type, encodings, sizes and chart properties.</summary>
    [JsonPropertyName("chart_spec")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required ChartSpec ChartSpec { get; set; }

    /// <summary>The <c>theme_spec</c> member: a preset id or a custom theme; applied by Vega-Lite and Plotly only.</summary>
    [JsonPropertyName("theme_spec")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChartTheme? ThemeSpec { get; set; }

    /// <summary>The <c>options</c> member: layout tuning.</summary>
    [JsonPropertyName("options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AssembleOptions? Options { get; set; }

    /// <summary>The <c>field_display_names</c> member: field name to display label.</summary>
    [JsonPropertyName("field_display_names")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(VerbatimKeyDictionaryConverter<string>))]
    public Dictionary<string, string>? FieldDisplayNames { get; set; }

    /// <summary>Members this version does not model; written after the known members.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}
