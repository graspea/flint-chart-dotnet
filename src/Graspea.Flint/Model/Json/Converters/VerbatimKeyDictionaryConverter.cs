using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Graspea.Flint.Serialization;

/// <summary>
/// A map whose keys are written exactly as stored, whatever the caller's <c>DictionaryKeyPolicy</c>.
/// Closed generic per member, so no <c>MakeGenericType</c>. <see cref="JsonNode"/> values pass through with
/// <see cref="JsonNode.WriteTo"/>; other values use the library's own source-generated metadata
/// (<see cref="FlintJsonContext"/> for hand-written types, the generated <see cref="FlintMapJsonContext"/> for the map
/// value types of generated members), never the caller's options. Infrastructure: public only so that a host source-generated <see cref="JsonSerializerContext"/> can
/// instantiate it.
/// </summary>
/// <typeparam name="T">The map value type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class VerbatimKeyDictionaryConverter<T> : JsonConverter<Dictionary<string, T>>
{
    // Resolved once per closed type, on first use (never for JsonNode values, which bypass it); a race resolves twice, harmlessly.
    private static JsonTypeInfo<T>? typeInfo;

    private static JsonTypeInfo<T> TypeInfo => typeInfo ??=
        (FlintJsonContext.Default.GetTypeInfo(typeof(T)) ?? FlintMapJsonContext.Default.GetTypeInfo(typeof(T))) as JsonTypeInfo<T>
        ?? throw new NotSupportedException($"No library metadata for {typeof(T)}; register it with [JsonSerializable] in FlintJsonContext.");

    /// <inheritdoc/>
    public override Dictionary<string, T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected a JSON object.");
        }

        var result = new Dictionary<string, T>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var key = reader.GetString()!;
            reader.Read();
            result[key] = typeof(T) == typeof(JsonNode)
                ? (T)(object?)JsonNode.Parse(ref reader)!
                : JsonSerializer.Deserialize(ref reader, TypeInfo)!;
        }

        return result;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, Dictionary<string, T> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (key, item) in value)
        {
            writer.WritePropertyName(key);
            if (item is null)
            {
                writer.WriteNullValue();
            }
            else if (item is JsonNode node)
            {
                node.WriteTo(writer);
            }
            else
            {
                JsonSerializer.Serialize(writer, item, TypeInfo);
            }
        }

        writer.WriteEndObject();
    }
}
