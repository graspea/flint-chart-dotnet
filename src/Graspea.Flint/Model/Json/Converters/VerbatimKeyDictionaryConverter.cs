using System.ComponentModel;
using System.Globalization;
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
            result[key] = MapValue<T>.Read(ref reader);
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
            MapValue<T>.Write(writer, item);
        }

        writer.WriteEndObject();
    }
}

/// <summary>
/// A map with integer keys (<c>Record&lt;number, T&gt;</c>), written as JSON object keys in the integer's invariant form
/// (<c>"3"</c>). A key that is not such a form, such as <c>"03"</c> or <c>"2.5"</c>, is refused, because it would not write
/// back as itself. Values are handled as by <see cref="VerbatimKeyDictionaryConverter{T}"/>. Infrastructure: public only so
/// that a host source-generated <see cref="JsonSerializerContext"/> can instantiate it.
/// </summary>
/// <typeparam name="T">The map value type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class IntKeyDictionaryConverter<T> : JsonConverter<Dictionary<int, T>>
{
    /// <inheritdoc/>
    public override Dictionary<int, T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected a JSON object.");
        }

        var result = new Dictionary<int, T>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var text = reader.GetString()!;
            if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var key)
                || key.ToString(CultureInfo.InvariantCulture) != text)
            {
                throw new JsonException($"The key \"{text}\" is not an integer.");
            }

            reader.Read();
            result[key] = MapValue<T>.Read(ref reader);
        }

        return result;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, Dictionary<int, T> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (key, item) in value)
        {
            writer.WritePropertyName(key.ToString(CultureInfo.InvariantCulture));
            MapValue<T>.Write(writer, item);
        }

        writer.WriteEndObject();
    }
}

/// <summary>Reads and writes one map value: <see cref="JsonNode"/> passes through, other types use the library's own
/// source-generated metadata, never the caller's options.</summary>
internal static class MapValue<T>
{
    // Resolved once per closed type, on first use (never for JsonNode values, which bypass it); a race resolves twice, harmlessly.
    private static JsonTypeInfo<T>? typeInfo;

    private static JsonTypeInfo<T> TypeInfo => typeInfo ??=
        (FlintJsonContext.Default.GetTypeInfo(typeof(T)) ?? FlintMapJsonContext.Default.GetTypeInfo(typeof(T))) as JsonTypeInfo<T>
        ?? throw new NotSupportedException($"No library metadata for {typeof(T)}; register it with [JsonSerializable] in FlintJsonContext.");

    public static T Read(ref Utf8JsonReader reader) =>
        typeof(T) == typeof(JsonNode) ? (T)(object?)JsonNode.Parse(ref reader)! : JsonSerializer.Deserialize(ref reader, TypeInfo)!;

    public static void Write(Utf8JsonWriter writer, T item)
    {
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
}
