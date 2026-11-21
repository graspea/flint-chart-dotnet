using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Graspea.Flint.Serialization;

/// <summary>Wire form <c>{visual, elements} | {select}</c> of <see cref="UpdateTarget"/>; every member is kept, so a target
/// reads back as it was written. Infrastructure: public only so that a host source-generated
/// <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class UpdateTargetConverter : JsonConverter<UpdateTarget>
{
    /// <inheritdoc/>
    public override UpdateTarget Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dto = UpdateTargets.Read(ref reader, FlintJsonContext.Default.UpdateTargetDto, "An update target (target, targets[i]) must be an object.");
        return new UpdateTarget { Visual = dto.Visual, Elements = dto.Elements, Select = dto.Select, Extensions = dto.Extensions };
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, UpdateTarget value, JsonSerializerOptions options)
    {
        var dto = new UpdateTargetDto { Visual = value.Visual, Elements = value.Elements, Select = value.Select, Extensions = value.Extensions };
        JsonSerializer.Serialize(writer, dto, FlintJsonContext.Default.UpdateTargetDto);
    }
}

/// <summary>Wire form <c>{kind?, role?}</c> of <see cref="TargetVisual"/>. Infrastructure: public only so that a host
/// source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class TargetVisualConverter : JsonConverter<TargetVisual>
{
    /// <inheritdoc/>
    public override TargetVisual Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dto = UpdateTargets.Read(ref reader, FlintJsonContext.Default.TargetVisualDto, "The visual of an update target (visual, select.visual) must be an object.");
        return new TargetVisual { Kind = dto.Kind, Role = dto.Role, Extensions = dto.Extensions };
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, TargetVisual value, JsonSerializerOptions options)
    {
        var dto = new TargetVisualDto { Kind = value.Kind, Role = value.Role, Extensions = value.Extensions };
        JsonSerializer.Serialize(writer, dto, FlintJsonContext.Default.TargetVisualDto);
    }
}

/// <summary>Wire form <c>{value, records?}</c> of <see cref="SemanticElement"/>. Infrastructure: public only so that a host
/// source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SemanticElementConverter : JsonConverter<SemanticElement>
{
    /// <inheritdoc/>
    public override SemanticElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dto = UpdateTargets.Read(ref reader, FlintJsonContext.Default.SemanticElementDto, "An item of an update target's elements must be an object.");
        return new SemanticElement { Value = dto.Value, Records = dto.Records, Extensions = dto.Extensions };
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, SemanticElement value, JsonSerializerOptions options)
    {
        var dto = new SemanticElementDto { Value = value.Value, Records = value.Records, Extensions = value.Extensions };
        JsonSerializer.Serialize(writer, dto, FlintJsonContext.Default.SemanticElementDto);
    }
}

/// <summary>Wire form <c>{key, visual?} | {where, visual?}</c> of <see cref="TargetSelector"/>; every member is kept.
/// Infrastructure: public only so that a host source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class TargetSelectorConverter : JsonConverter<TargetSelector>
{
    /// <inheritdoc/>
    public override TargetSelector Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dto = UpdateTargets.Read(ref reader, FlintJsonContext.Default.TargetSelectorDto, "The select of an update target must be an object.");
        return new TargetSelector { Key = dto.Key, Where = dto.Where, Visual = dto.Visual, Extensions = dto.Extensions };
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, TargetSelector value, JsonSerializerOptions options)
    {
        var dto = new TargetSelectorDto { Key = value.Key, Where = value.Where, Visual = value.Visual, Extensions = value.Extensions };
        JsonSerializer.Serialize(writer, dto, FlintJsonContext.Default.TargetSelectorDto);
    }
}

/// <summary>Wire form <c>{in} | {range}</c> of <see cref="FilterValue"/>; every member is kept. Infrastructure: public only so
/// that a host source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class FilterValueConverter : JsonConverter<FilterValue>
{
    /// <inheritdoc/>
    public override FilterValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dto = UpdateTargets.Read(ref reader, FlintJsonContext.Default.FilterValueDto, "A condition of an update target's select.where must be an object: {in} or {range}.");
        return new FilterValue { In = dto.In, Range = dto.Range, Extensions = dto.Extensions };
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, FilterValue value, JsonSerializerOptions options)
    {
        var dto = new FilterValueDto { In = value.In, Range = value.Range, Extensions = value.Extensions };
        JsonSerializer.Serialize(writer, dto, FlintJsonContext.Default.FilterValueDto);
    }
}

internal static class UpdateTargets
{
    /// <summary>Reads an object form through the library's own metadata, whatever the caller's options are.</summary>
    /// <exception cref="JsonException">The value is not a JSON object; <paramref name="message"/> names the member.</exception>
    internal static T Read<T>(ref Utf8JsonReader reader, JsonTypeInfo<T> typeInfo, string message)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException(message);
        }

        return JsonSerializer.Deserialize(ref reader, typeInfo)!;
    }
}

/// <summary>The object form of <see cref="UpdateTarget"/>; owns the wire names.</summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
internal sealed class UpdateTargetDto
{
    // select first: it decides the form, so a selector that also has the members of a resolved target leads with it.
    [JsonPropertyName("select")]
    public TargetSelector? Select { get; set; }

    [JsonPropertyName("visual")]
    public TargetVisual? Visual { get; set; }

    [JsonPropertyName("elements")]
    public SemanticElement[]? Elements { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

/// <summary>The object form of <see cref="TargetVisual"/>; owns the wire names.</summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
internal sealed class TargetVisualDto
{
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

/// <summary>The object form of <see cref="SemanticElement"/>; owns the wire names.</summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
internal sealed class SemanticElementDto
{
    [JsonPropertyName("value")]
    public Dictionary<string, JsonNode?>? Value { get; set; }

    [JsonPropertyName("records")]
    public Dictionary<string, JsonNode?>[]? Records { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

/// <summary>The object form of <see cref="TargetSelector"/>; owns the wire names.</summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
internal sealed class TargetSelectorDto
{
    [JsonPropertyName("key")]
    public Dictionary<string, JsonNode?>? Key { get; set; }

    [JsonPropertyName("where")]
    public Dictionary<string, FilterValue>? Where { get; set; }

    [JsonPropertyName("visual")]
    public TargetVisual? Visual { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

/// <summary>The object form of <see cref="FilterValue"/>; owns the wire names.</summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
internal sealed class FilterValueDto
{
    [JsonPropertyName("in")]
    public JsonNode?[]? In { get; set; }

    [JsonPropertyName("range")]
    public JsonNode?[]? Range { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}
