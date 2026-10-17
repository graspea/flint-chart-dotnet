using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Graspea.Flint.Serialization;

/// <summary>Wire form <c>string | SemanticAnnotation</c>. Infrastructure: public only so that a host source-generated
/// <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SemanticAnnotationConverter : JsonConverter<SemanticAnnotation>
{
    /// <inheritdoc/>
    public override SemanticAnnotation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new SemanticAnnotation { Type = StringBacked.Read(ref reader, static name => new SemanticType(name)) };
        }

        var dto = JsonSerializer.Deserialize(ref reader, FlintJsonContext.Default.SemanticAnnotationDto)!;
        return new SemanticAnnotation
        {
            Type = dto.SemanticType,
            IntrinsicDomain = dto.IntrinsicDomain,
            Unit = dto.Unit,
            DivergingMidpoint = dto.DivergingMidpoint,
            SortOrder = dto.SortOrder,
            Extensions = dto.Extensions,
        };
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, SemanticAnnotation value, JsonSerializerOptions options)
    {
        if (value is { IntrinsicDomain: null, Unit: null, DivergingMidpoint: null, SortOrder: null } && value.Extensions is not { Count: > 0 })
        {
            StringBacked.Write<SemanticType>(writer, value.Type.Name);
            return;
        }

        var dto = new SemanticAnnotationDto
        {
            SemanticType = value.Type,
            IntrinsicDomain = value.IntrinsicDomain,
            Unit = value.Unit,
            DivergingMidpoint = value.DivergingMidpoint,
            SortOrder = value.SortOrder,
            Extensions = value.Extensions,
        };
        JsonSerializer.Serialize(writer, dto, FlintJsonContext.Default.SemanticAnnotationDto);
    }
}

/// <summary>The object form of <see cref="SemanticAnnotation"/>; owns the wire names.</summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
internal sealed class SemanticAnnotationDto
{
    [JsonPropertyName("semanticType")]
    public required SemanticType SemanticType { get; set; }

    [JsonPropertyName("intrinsicDomain")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? IntrinsicDomain { get; set; }

    [JsonPropertyName("unit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Unit { get; set; }

    [JsonPropertyName("divergingMidpoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DivergingMidpoint { get; set; }

    [JsonPropertyName("sortOrder")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? SortOrder { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}
