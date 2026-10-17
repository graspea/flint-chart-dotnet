using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Graspea.Flint.Serialization;

/// <summary>Wire form <c>number | {x, y, elasticity?, maxStretch?, seriesCountAxis?}</c>. Infrastructure:
/// public only so that a host source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ContinuousMarkCrossSectionConverter : JsonConverter<ContinuousMarkCrossSection>
{
    /// <inheritdoc/>
    public override ContinuousMarkCrossSection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return new ContinuousMarkCrossSection(reader.GetDouble());
            case JsonTokenType.StartObject:
                var dto = JsonSerializer.Deserialize(ref reader, FlintJsonContext.Default.ContinuousMarkCrossSectionDto)!;
                return new ContinuousMarkCrossSection(dto.X, dto.Y, dto.Elasticity, dto.MaxStretch, dto.SeriesCountAxis) { Extensions = dto.Extensions };
            default:
                throw new JsonException("continuousMarkCrossSection must be a number or an object.");
        }
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ContinuousMarkCrossSection value, JsonSerializerOptions options)
    {
        if (value.Uniform is { } uniform)
        {
            writer.WriteNumberValue(uniform);
            return;
        }

        var dto = new ContinuousMarkCrossSectionDto
        {
            X = value.X,
            Y = value.Y,
            Elasticity = value.Elasticity,
            MaxStretch = value.MaxStretch,
            SeriesCountAxis = value.SeriesCountAxis,
            Extensions = value.Extensions,
        };
        JsonSerializer.Serialize(writer, dto, FlintJsonContext.Default.ContinuousMarkCrossSectionDto);
    }
}

/// <summary>The object form of <see cref="ContinuousMarkCrossSection"/>; owns the wire names.</summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
internal sealed class ContinuousMarkCrossSectionDto
{
    [JsonPropertyName("x")]
    public double? X { get; set; }

    [JsonPropertyName("y")]
    public double? Y { get; set; }

    [JsonPropertyName("elasticity")]
    public double? Elasticity { get; set; }

    [JsonPropertyName("maxStretch")]
    public double? MaxStretch { get; set; }

    [JsonPropertyName("seriesCountAxis")]
    public SeriesCountAxis? SeriesCountAxis { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}
