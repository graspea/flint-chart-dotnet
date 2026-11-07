using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Graspea.Flint.Serialization;

/// <summary>Wire form <c>false | {header?, footer?, logos?}</c> of <see cref="ChartChrome"/>. Infrastructure: public only
/// so that a host source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ChartChromeConverter : JsonConverter<ChartChrome>
{
    /// <inheritdoc/>
    public override ChartChrome Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.False:
                return ChartChrome.None;
            case JsonTokenType.StartObject:
                var dto = JsonSerializer.Deserialize(ref reader, FlintJsonContext.Default.ChartChromeDto)!;
                return new ChartChrome { Header = dto.Header, Footer = dto.Footer, Logos = dto.Logos, Extensions = dto.Extensions };
            default:
                throw new JsonException("chart_spec.chrome must be false or an object.");
        }
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ChartChrome value, JsonSerializerOptions options)
    {
        if (value.IsNone)
        {
            writer.WriteBooleanValue(false);
            return;
        }

        var dto = new ChartChromeDto { Header = value.Header, Footer = value.Footer, Logos = value.Logos, Extensions = value.Extensions };
        JsonSerializer.Serialize(writer, dto, FlintJsonContext.Default.ChartChromeDto);
    }
}

/// <summary>Wire form <c>string | {text, href?}</c> of <see cref="ChartSource"/>. Infrastructure: public only so that a
/// host source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ChartSourceConverter : JsonConverter<ChartSource>
{
    /// <inheritdoc/>
    public override ChartSource Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString()!;
            case JsonTokenType.StartObject:
                var dto = JsonSerializer.Deserialize(ref reader, FlintJsonContext.Default.ChartSourceDto)!;
                return new ChartSource { Text = dto.Text, Href = dto.Href, Extensions = dto.Extensions };
            default:
                throw new JsonException("chart_spec.source must be a string or an object.");
        }
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ChartSource value, JsonSerializerOptions options)
    {
        if (value.IsTextOnly)
        {
            writer.WriteStringValue(value.Text);
            return;
        }

        var dto = new ChartSourceDto { Text = value.Text, Href = value.Href, Extensions = value.Extensions };
        JsonSerializer.Serialize(writer, dto, FlintJsonContext.Default.ChartSourceDto);
    }
}

/// <summary>Wire form <c>string | null</c> of <see cref="ChannelTitle"/>: <c>null</c> reads as
/// <see cref="ChannelTitle.None"/>. Infrastructure: public only so that a host source-generated
/// <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ChannelTitleConverter : JsonConverter<ChannelTitle>
{
    /// <summary>Reads <c>null</c> as <see cref="ChannelTitle.None"/> instead of leaving the member unset.</summary>
    public override bool HandleNull => true;

    /// <inheritdoc/>
    public override ChannelTitle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => ChannelTitle.None,
            JsonTokenType.String => reader.GetString()!,
            _ => throw new JsonException("An encoding title must be a string or null."),
        };

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ChannelTitle value, JsonSerializerOptions options)
    {
        if (value?.Text is { } text)
        {
            writer.WriteStringValue(text);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

/// <summary>Wire form <c>boolean | {details?, indicator?, maxDistance?}</c> of <see cref="AssistedTargeting"/>.
/// Infrastructure: public only so that a host source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class AssistedTargetingConverter : JsonConverter<AssistedTargeting>
{
    /// <inheritdoc/>
    public override AssistedTargeting Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True or JsonTokenType.False:
                return reader.GetBoolean();
            case JsonTokenType.StartObject:
                var dto = JsonSerializer.Deserialize(ref reader, FlintJsonContext.Default.AssistedTargetingDto)!;
                return new AssistedTargeting { Details = dto.Details, Indicator = dto.Indicator, MaxDistance = dto.MaxDistance, Extensions = dto.Extensions };
            default:
                throw new JsonException("interaction_spec.assistedTargeting must be a boolean or an object.");
        }
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, AssistedTargeting value, JsonSerializerOptions options)
    {
        if (value.Enabled is { } enabled)
        {
            writer.WriteBooleanValue(enabled);
            return;
        }

        var dto = new AssistedTargetingDto { Details = value.Details, Indicator = value.Indicator, MaxDistance = value.MaxDistance, Extensions = value.Extensions };
        JsonSerializer.Serialize(writer, dto, FlintJsonContext.Default.AssistedTargetingDto);
    }
}

/// <summary>Wire form <c>boolean | {fields?, maxRows?}</c> of <see cref="TargetDetails"/>. Infrastructure: public only so
/// that a host source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class TargetDetailsConverter : JsonConverter<TargetDetails>
{
    /// <inheritdoc/>
    public override TargetDetails Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True or JsonTokenType.False:
                return reader.GetBoolean();
            case JsonTokenType.StartObject:
                var dto = JsonSerializer.Deserialize(ref reader, FlintJsonContext.Default.TargetDetailsDto)!;
                return new TargetDetails { Fields = dto.Fields, MaxRows = dto.MaxRows, Extensions = dto.Extensions };
            default:
                throw new JsonException("assistedTargeting.details must be a boolean or an object.");
        }
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, TargetDetails value, JsonSerializerOptions options)
    {
        if (value.Enabled is { } enabled)
        {
            writer.WriteBooleanValue(enabled);
            return;
        }

        var dto = new TargetDetailsDto { Fields = value.Fields, MaxRows = value.MaxRows, Extensions = value.Extensions };
        JsonSerializer.Serialize(writer, dto, FlintJsonContext.Default.TargetDetailsDto);
    }
}

/// <summary>The object form of <see cref="ChartChrome"/>; owns the wire names.</summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
internal sealed class ChartChromeDto
{
    [JsonPropertyName("header")]
    public bool? Header { get; set; }

    [JsonPropertyName("footer")]
    public bool? Footer { get; set; }

    [JsonPropertyName("logos")]
    public bool? Logos { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

/// <summary>The object form of <see cref="ChartSource"/>; owns the wire names.</summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
internal sealed class ChartSourceDto
{
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required string Text { get; set; }

    [JsonPropertyName("href")]
    public string? Href { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

/// <summary>The object form of <see cref="AssistedTargeting"/>; owns the wire names.</summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
internal sealed class AssistedTargetingDto
{
    [JsonPropertyName("details")]
    public TargetDetails? Details { get; set; }

    [JsonPropertyName("indicator")]
    public bool? Indicator { get; set; }

    [JsonPropertyName("maxDistance")]
    public double? MaxDistance { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

/// <summary>The object form of <see cref="TargetDetails"/>; owns the wire names.</summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
internal sealed class TargetDetailsDto
{
    [JsonPropertyName("fields")]
    public string[]? Fields { get; set; }

    [JsonPropertyName("maxRows")]
    public double? MaxRows { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}
