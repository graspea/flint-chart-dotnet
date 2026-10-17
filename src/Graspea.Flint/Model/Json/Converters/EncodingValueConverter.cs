using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Graspea.Flint.Serialization;

/// <summary>Wire form <c>ChartEncoding | string | (ChartEncoding | string)[]</c>. Infrastructure: public only so that a
/// host source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class EncodingValueConverter : JsonConverter<EncodingValue>
{
    /// <inheritdoc/>
    public override EncodingValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            return EncodingValue.Single(ReadOne(ref reader));
        }

        var series = new List<ChartEncoding>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            series.Add(ReadOne(ref reader));
        }

        return EncodingValue.Series([.. series]);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, EncodingValue value, JsonSerializerOptions options)
    {
        if (!value.IsSeries)
        {
            JsonSerializer.Serialize(writer, value.Encodings[0], FlintJsonContext.Default.ChartEncoding);
            return;
        }

        writer.WriteStartArray();
        foreach (var encoding in value.Encodings)
        {
            JsonSerializer.Serialize(writer, encoding, FlintJsonContext.Default.ChartEncoding);
        }

        writer.WriteEndArray();
    }

    private static ChartEncoding ReadOne(ref Utf8JsonReader reader) =>
        reader.TokenType switch
        {
            JsonTokenType.String => new ChartEncoding { Field = reader.GetString() },
            JsonTokenType.StartObject => JsonSerializer.Deserialize(ref reader, FlintJsonContext.Default.ChartEncoding)!,
            _ => throw new JsonException("An encoding must be a string, an object or an array of strings and objects."),
        };
}
