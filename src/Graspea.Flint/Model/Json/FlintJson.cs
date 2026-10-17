using System.Text.Json;
using System.Text.Json.Nodes;

namespace Graspea.Flint.Serialization;

/// <summary>
/// Serialises and reads <see cref="ChartAssemblyInput"/> documents with the library's own source-generated metadata.
/// The wire rules live on the model types, so serialising with any other options gives the same document.
/// </summary>
public static class FlintJson
{
    /// <summary>The library's options: no naming or key policy, the library's source-generated resolver; read-only.</summary>
    public static JsonSerializerOptions Options => FlintJsonContext.Default.Options;

    /// <summary>Writes the document as JSON text.</summary>
    /// <param name="input">The document.</param>
    /// <param name="indented">Whether to indent the output; the document is the same either way.</param>
    /// <returns>The JSON text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
    public static string Serialize(ChartAssemblyInput input, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!indented)
        {
            return JsonSerializer.Serialize(input, FlintJsonContext.Default.ChartAssemblyInput);
        }

        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            JsonSerializer.Serialize(writer, input, FlintJsonContext.Default.ChartAssemblyInput);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Writes the document as compact UTF-8 JSON.</summary>
    /// <param name="input">The document.</param>
    /// <returns>The UTF-8 bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
    public static byte[] SerializeToUtf8Bytes(ChartAssemblyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return JsonSerializer.SerializeToUtf8Bytes(input, FlintJsonContext.Default.ChartAssemblyInput);
    }

    /// <summary>Writes the document to <paramref name="writer"/>, which keeps its own formatting options.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="input">The document.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> or <paramref name="input"/> is null.</exception>
    public static void Serialize(Utf8JsonWriter writer, ChartAssemblyInput input)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(input);
        JsonSerializer.Serialize(writer, input, FlintJsonContext.Default.ChartAssemblyInput);
    }

    /// <summary>Writes the document as a <see cref="JsonNode"/>.</summary>
    /// <param name="input">The document.</param>
    /// <returns>The document as a JSON object.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
    public static JsonNode SerializeToNode(ChartAssemblyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return JsonSerializer.SerializeToNode(input, FlintJsonContext.Default.ChartAssemblyInput)!;
    }

    /// <summary>Reads a document from JSON text. An absent <c>chart_spec.encodings</c> is read as <c>{}</c>, as
    /// <see cref="ChartSpec.Encodings"/> defaults, and written back as <c>{}</c>; only an explicit <c>null</c> stays missing.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
    /// <exception cref="JsonException">The JSON is malformed, is <c>null</c>, lacks <c>chart_spec</c> or <c>chartType</c>,
    /// or holds an empty or non-string value where a string-backed value is expected.</exception>
    public static ChartAssemblyInput Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize(json, FlintJsonContext.Default.ChartAssemblyInput) ?? throw NullDocument();
    }

    /// <summary>Reads a document from UTF-8 JSON. An absent <c>chart_spec.encodings</c> is read as <c>{}</c>, as
    /// <see cref="ChartSpec.Encodings"/> defaults, and written back as <c>{}</c>; only an explicit <c>null</c> stays missing.</summary>
    /// <param name="utf8">The UTF-8 JSON.</param>
    /// <returns>The document.</returns>
    /// <exception cref="JsonException">The JSON is malformed, is <c>null</c>, lacks <c>chart_spec</c> or <c>chartType</c>,
    /// or holds an empty or non-string value where a string-backed value is expected.</exception>
    public static ChartAssemblyInput Deserialize(ReadOnlySpan<byte> utf8) =>
        JsonSerializer.Deserialize(utf8, FlintJsonContext.Default.ChartAssemblyInput) ?? throw NullDocument();

    private static JsonException NullDocument() => new("A ChartAssemblyInput document cannot be null.");
}
