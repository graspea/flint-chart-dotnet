using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Graspea.Flint.Serialization;

/// <summary>
/// Shared read/write logic of the generated discriminated union converters (docs/generator.md, "Discriminated unions"): an
/// abstract class whose derived classes are told apart by one string member, the discriminator. It reads and writes the
/// derived classes through source-generated metadata only and ignores the caller's options (design §5.1), so it is
/// trimming- and AOT-safe.
/// </summary>
internal static class DiscriminatedUnion
{
    /// <summary>
    /// Reads a JSON object: through the metadata <paramref name="derived"/> returns for its discriminator, or, when that is
    /// null (a value this version does not know), through <paramref name="unknown"/> with every other member.
    /// </summary>
    /// <exception cref="JsonException">The value is not an object, or its discriminator is missing or not a string.</exception>
    internal static T Read<T>(ref Utf8JsonReader reader, string discriminator, Func<string, JsonTypeInfo?> derived, Func<string, Dictionary<string, JsonElement>?, T> unknown)
        where T : class
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"{typeof(T).Name} must be a JSON object.");
        }

        var value = JsonElement.ParseValue(ref reader);
        if (!value.TryGetProperty(discriminator, out var tag))
        {
            throw new JsonException($"{typeof(T).Name} must have a \"{discriminator}\" member.");
        }

        if (tag.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"The \"{discriminator}\" member of {typeof(T).Name} must be a JSON string.");
        }

        var name = tag.GetString()!;
        if (derived(name) is { } info)
        {
            return (T)value.Deserialize(info)!;
        }

        Dictionary<string, JsonElement>? extensions = null;
        foreach (var member in value.EnumerateObject())
        {
            if (member.Name != discriminator)
            {
                (extensions ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal))[member.Name] = member.Value;
            }
        }

        return unknown(name, extensions);
    }

    /// <summary>Writes a derived class through its metadata in <paramref name="context"/>, which declares the discriminator first.</summary>
    internal static void Write(Utf8JsonWriter writer, object value, JsonSerializerContext context) =>
        JsonSerializer.Serialize(writer, value, context.GetTypeInfo(value.GetType()) ?? throw new JsonException($"{value.GetType().Name} has no serialisation metadata."));

    /// <summary>Writes a value this version does not know: its discriminator, then the other members, each once.</summary>
    internal static void WriteUnknown(Utf8JsonWriter writer, string discriminator, string name, Dictionary<string, JsonElement>? extensions)
    {
        writer.WriteStartObject();
        writer.WriteString(discriminator, name);
        foreach (var member in extensions ?? [])
        {
            if (member.Key != discriminator)
            {
                writer.WritePropertyName(member.Key);
                member.Value.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
    }
}
