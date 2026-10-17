using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Graspea.Flint.Serialization;

/// <summary>
/// Writes a <see cref="JsonNode"/> member with <see cref="JsonNode.WriteTo"/> and no caller options, so host settings
/// such as <c>NumberHandling = WriteAsString</c> cannot alter a pass-through value. Infrastructure: public only so
/// that a host source-generated <see cref="JsonSerializerContext"/> can instantiate it.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class JsonNodePassThroughConverter : JsonConverter<JsonNode>
{
    /// <inheritdoc/>
    public override JsonNode? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonNode.Parse(ref reader);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, JsonNode value, JsonSerializerOptions options) => value.WriteTo(writer);
}
