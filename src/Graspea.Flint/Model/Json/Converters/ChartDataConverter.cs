using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Graspea.Flint.Serialization;

/// <summary>Wire form <c>{values} | {url}</c>. Infrastructure: public only so that a host source-generated
/// <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ChartDataConverter : JsonConverter<ChartData>
{
    /// <inheritdoc/>
    public override ChartData Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dto = JsonSerializer.Deserialize(ref reader, FlintJsonContext.Default.ChartDataDto)!;
        return ChartData.Create(dto.Values, dto.Url, dto.Extensions);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ChartData value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, new ChartDataDto { Values = value.Values, Url = value.Url, Extensions = value.Extensions }, FlintJsonContext.Default.ChartDataDto);
}

/// <summary>The object form of <see cref="ChartData"/>; owns the wire names.</summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
internal sealed class ChartDataDto
{
    [JsonPropertyName("values")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonArray? Values { get; set; }

    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}
