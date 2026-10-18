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
    /// <exception cref="InvalidOperationException">Streamed data from an asynchronous source (converters are synchronous;
    /// use <see cref="FlintJson.SerializeAsync"/>), or streamed data that was already written.</exception>
    public override void Write(Utf8JsonWriter writer, ChartData value, JsonSerializerOptions options)
    {
        if (value.Streamed is { } streamed)
        {
            streamed.EnsureSynchronous();
            streamed.Claim();
            WriteStreamedStart(writer);
            streamed.WriteRows(writer, null);
            WriteStreamedEnd(writer, value);
            return;
        }

        JsonSerializer.Serialize(writer, new ChartDataDto { Values = value.Values, Url = value.Url, Extensions = value.Extensions }, FlintJsonContext.Default.ChartDataDto);
    }

    /// <summary>The start of the streamed data object, <c>{"values":[</c>, as <see cref="ChartDataDto"/> writes it.</summary>
    internal static void WriteStreamedStart(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("values"u8);
        writer.WriteStartArray();
    }

    /// <summary>The end of the streamed data object: <c>]</c>, the extensions after the known members, as
    /// <see cref="ChartDataDto"/> writes them, and <c>}</c>.</summary>
    internal static void WriteStreamedEnd(Utf8JsonWriter writer, ChartData value)
    {
        writer.WriteEndArray();
        foreach (var (key, element) in value.Extensions ?? [])
        {
            writer.WritePropertyName(key);
            element.WriteTo(writer);
        }

        writer.WriteEndObject();
    }
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
