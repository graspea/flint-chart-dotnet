using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Graspea.Flint.Theming;

namespace Graspea.Flint.Serialization;

/// <summary>Wire form <c>string | number</c> of <see cref="TypeWeight"/>. Infrastructure: public only so that a host
/// source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class TypeWeightConverter : JsonConverter<TypeWeight>
{
    /// <inheritdoc/>
    public override TypeWeight Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => new TypeWeight(reader.GetString(), null),
            JsonTokenType.Number => new TypeWeight(null, reader.GetDouble()),
            _ => throw new JsonException("A font weight must be a string or a number."),
        };

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, TypeWeight value, JsonSerializerOptions options) =>
        ThemeUnions.WriteStringOrNumber(writer, value.Keyword, value.Number);
}

/// <summary>Wire form <c>number | string</c> of <see cref="ThemeLength"/>. Infrastructure: public only so that a host
/// source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ThemeLengthConverter : JsonConverter<ThemeLength>
{
    /// <inheritdoc/>
    public override ThemeLength Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Number => new ThemeLength(reader.GetDouble(), null),
            JsonTokenType.String => new ThemeLength(null, reader.GetString()),
            _ => throw new JsonException("A theme length must be a number or a string."),
        };

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ThemeLength value, JsonSerializerOptions options) =>
        ThemeUnions.WriteStringOrNumber(writer, value.Text, value.Number);
}

/// <summary>Wire form <c>step | step[]</c> of <see cref="LabelOverflow"/>. Infrastructure: public only so that a host
/// source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class LabelOverflowConverter : JsonConverter<LabelOverflow>
{
    /// <inheritdoc/>
    public override LabelOverflow Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new LabelOverflow(reader.GetString(), null);
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("labels.overflow must be a string or an array of strings.");
        }

        var steps = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            steps.Add(reader.TokenType == JsonTokenType.String ? reader.GetString()! : throw new JsonException("labels.overflow steps must be strings."));
        }

        return new LabelOverflow(null, [.. steps]);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, LabelOverflow value, JsonSerializerOptions options)
    {
        if (value.Steps is null)
        {
            writer.WriteStringValue(value.Step);
            return;
        }

        writer.WriteStartArray();
        foreach (var step in value.Steps)
        {
            writer.WriteStringValue(step);
        }

        writer.WriteEndArray();
    }
}

/// <summary>Wire form <c>presence | {color?, dash?, layer?, presence?, style?, weight?}</c> of <see cref="RuleSpec"/>.
/// Infrastructure: public only so that a host source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class RuleSpecConverter : JsonConverter<RuleSpec>
{
    /// <inheritdoc/>
    public override RuleSpec Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString()!;
            case JsonTokenType.StartObject:
                var dto = JsonSerializer.Deserialize(ref reader, FlintJsonContext.Default.RuleSpecDto)!;
                return new RuleSpec
                {
                    Color = dto.Color, Dash = dto.Dash, Layer = dto.Layer, Presence = dto.Presence, Style = dto.Style, Weight = dto.Weight,
                    Extensions = dto.Extensions,
                };
            default:
                throw new JsonException("A rule must be a presence string or an object.");
        }
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, RuleSpec value, JsonSerializerOptions options)
    {
        if (value.IsPresenceOnly)
        {
            writer.WriteStringValue(value.Presence);
            return;
        }

        var dto = new RuleSpecDto
        {
            Color = value.Color, Dash = value.Dash, Layer = value.Layer, Presence = value.Presence, Style = value.Style, Weight = value.Weight,
            Extensions = value.Extensions,
        };
        JsonSerializer.Serialize(writer, dto, FlintJsonContext.Default.RuleSpecDto);
    }
}

/// <summary>Wire form <c>presence | {…rule, divisions?, on?}</c> of <see cref="MinorGridSpec"/>. Infrastructure: public
/// only so that a host source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class MinorGridSpecConverter : JsonConverter<MinorGridSpec>
{
    /// <inheritdoc/>
    public override MinorGridSpec Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString()!;
            case JsonTokenType.StartObject:
                var dto = JsonSerializer.Deserialize(ref reader, FlintJsonContext.Default.MinorGridSpecDto)!;
                return new MinorGridSpec
                {
                    Color = dto.Color, Dash = dto.Dash, Divisions = dto.Divisions, Layer = dto.Layer, On = dto.On, Presence = dto.Presence,
                    Style = dto.Style, Weight = dto.Weight, Extensions = dto.Extensions,
                };
            default:
                throw new JsonException("structure.grid.minor must be a presence string or an object.");
        }
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, MinorGridSpec value, JsonSerializerOptions options)
    {
        if (value.IsPresenceOnly)
        {
            writer.WriteStringValue(value.Presence);
            return;
        }

        var dto = new MinorGridSpecDto
        {
            Color = value.Color, Dash = value.Dash, Divisions = value.Divisions, Layer = value.Layer, On = value.On, Presence = value.Presence,
            Style = value.Style, Weight = value.Weight, Extensions = value.Extensions,
        };
        JsonSerializer.Serialize(writer, dto, FlintJsonContext.Default.MinorGridSpecDto);
    }
}

internal static class ThemeUnions
{
    internal static void WriteStringOrNumber(Utf8JsonWriter writer, string? text, double? number)
    {
        if (text is not null)
        {
            writer.WriteStringValue(text);
        }
        else
        {
            writer.WriteNumberValue(number!.Value);
        }
    }
}

/// <summary>The object form of <see cref="RuleSpec"/>; owns the wire names.</summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
internal sealed class RuleSpecDto
{
    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("dash")]
    public double[]? Dash { get; set; }

    [JsonPropertyName("layer")]
    public string? Layer { get; set; }

    [JsonPropertyName("presence")]
    public string? Presence { get; set; }

    [JsonPropertyName("style")]
    public string? Style { get; set; }

    [JsonPropertyName("weight")]
    public double? Weight { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

/// <summary>The object form of <see cref="MinorGridSpec"/>; owns the wire names.</summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
internal sealed class MinorGridSpecDto
{
    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("dash")]
    public double[]? Dash { get; set; }

    [JsonPropertyName("divisions")]
    public double? Divisions { get; set; }

    [JsonPropertyName("layer")]
    public string? Layer { get; set; }

    [JsonPropertyName("on")]
    public string[]? On { get; set; }

    [JsonPropertyName("presence")]
    public string? Presence { get; set; }

    [JsonPropertyName("style")]
    public string? Style { get; set; }

    [JsonPropertyName("weight")]
    public double? Weight { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}
