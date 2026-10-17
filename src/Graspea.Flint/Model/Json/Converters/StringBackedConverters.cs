using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Graspea.Flint.Serialization;

/// <summary>Shared read/write logic of the string-backed struct converters (design §4.1, §5.1).</summary>
internal static class StringBacked
{
    internal static T Read<T>(ref Utf8JsonReader reader, Func<string, T> create)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"{typeof(T).Name} must be a JSON string.");
        }

        try
        {
            return create(reader.GetString()!);
        }
        catch (ArgumentException e)
        {
            throw new JsonException($"{typeof(T).Name} must be a non-empty string.", e);
        }
    }

    internal static void Write<T>(Utf8JsonWriter writer, string? name) =>
        writer.WriteStringValue(name ?? throw new JsonException($"default({typeof(T).Name}) cannot be serialised."));
}

/// <summary>Writes <see cref="ChartType"/> as its string, verbatim. Infrastructure: public only so that a host
/// source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ChartTypeConverter : JsonConverter<ChartType>
{
    /// <inheritdoc/>
    public override ChartType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        StringBacked.Read(ref reader, static name => new ChartType(name));

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ChartType value, JsonSerializerOptions options) =>
        StringBacked.Write<ChartType>(writer, value.Name);
}

/// <summary>Writes <see cref="Channel"/> as its string, verbatim. Infrastructure: public only so that a host
/// source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ChannelConverter : JsonConverter<Channel>
{
    /// <inheritdoc/>
    public override Channel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        StringBacked.Read(ref reader, static name => new Channel(name));

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, Channel value, JsonSerializerOptions options) =>
        StringBacked.Write<Channel>(writer, value.Name);
}

/// <summary>Writes <see cref="SemanticType"/> as its string, verbatim. Infrastructure: public only so that a host
/// source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SemanticTypeConverter : JsonConverter<SemanticType>
{
    /// <inheritdoc/>
    public override SemanticType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        StringBacked.Read(ref reader, static name => new SemanticType(name));

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, SemanticType value, JsonSerializerOptions options) =>
        StringBacked.Write<SemanticType>(writer, value.Name);
}

/// <summary>Writes <see cref="ThemePreset"/> as its string, verbatim. Infrastructure: public only so that a host
/// source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ThemePresetConverter : JsonConverter<ThemePreset>
{
    /// <inheritdoc/>
    public override ThemePreset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        StringBacked.Read(ref reader, static name => new ThemePreset(name));

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ThemePreset value, JsonSerializerOptions options) =>
        StringBacked.Write<ThemePreset>(writer, value.Name);
}

/// <summary>Writes <see cref="EncodingType"/> as its string, verbatim. Infrastructure: public only so that a host
/// source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class EncodingTypeConverter : JsonConverter<EncodingType>
{
    /// <inheritdoc/>
    public override EncodingType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        StringBacked.Read(ref reader, static name => new EncodingType(name));

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, EncodingType value, JsonSerializerOptions options) =>
        StringBacked.Write<EncodingType>(writer, value.Name);
}

/// <summary>Writes <see cref="Aggregate"/> as its string, verbatim. Infrastructure: public only so that a host
/// source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class AggregateConverter : JsonConverter<Aggregate>
{
    /// <inheritdoc/>
    public override Aggregate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        StringBacked.Read(ref reader, static name => new Aggregate(name));

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, Aggregate value, JsonSerializerOptions options) =>
        StringBacked.Write<Aggregate>(writer, value.Name);
}

/// <summary>Writes <see cref="SortOrder"/> as its string, verbatim. Infrastructure: public only so that a host
/// source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SortOrderConverter : JsonConverter<SortOrder>
{
    /// <inheritdoc/>
    public override SortOrder Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        StringBacked.Read(ref reader, static name => new SortOrder(name));

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, SortOrder value, JsonSerializerOptions options) =>
        StringBacked.Write<SortOrder>(writer, value.Name);
}

/// <summary>Writes <see cref="SeriesCountAxis"/> as its string, verbatim. Infrastructure: public only so that a host
/// source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SeriesCountAxisConverter : JsonConverter<SeriesCountAxis>
{
    /// <inheritdoc/>
    public override SeriesCountAxis Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        StringBacked.Read(ref reader, static name => new SeriesCountAxis(name));

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, SeriesCountAxis value, JsonSerializerOptions options) =>
        StringBacked.Write<SeriesCountAxis>(writer, value.Name);
}
