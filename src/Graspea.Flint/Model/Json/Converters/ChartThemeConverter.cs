using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Graspea.Flint.Serialization;

/// <summary>Wire form <c>ThemeSpec | string</c>. Infrastructure: public only so that a host source-generated
/// <see cref="JsonSerializerContext"/> can instantiate it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ChartThemeConverter : JsonConverter<ChartTheme>
{
    /// <inheritdoc/>
    public override ChartTheme Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => StringBacked.Read(ref reader, static id => new ThemePreset(id)),
            JsonTokenType.StartObject => JsonSerializer.Deserialize(ref reader, FlintJsonContext.Default.ThemeSpec)!,
            _ => throw new JsonException("theme_spec must be a preset id or an object."),
        };

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ChartTheme value, JsonSerializerOptions options)
    {
        if (value.Preset is { } preset)
        {
            StringBacked.Write<ThemePreset>(writer, preset.Name);
        }
        else
        {
            JsonSerializer.Serialize(writer, value.Spec!, FlintJsonContext.Default.ThemeSpec);
        }
    }
}
