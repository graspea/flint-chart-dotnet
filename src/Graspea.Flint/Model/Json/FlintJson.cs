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
    /// <exception cref="InvalidOperationException">The data is streamed from an asynchronous source (use
    /// <see cref="SerializeAsync"/>), or streamed data was already written. Streamed rows from a synchronous source are
    /// written into the result.</exception>
    public static string Serialize(ChartAssemblyInput input, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureSynchronous(input);
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
    /// <exception cref="InvalidOperationException">The data is streamed from an asynchronous source (use
    /// <see cref="SerializeAsync"/>), or streamed data was already written. Streamed rows from a synchronous source are
    /// written into the result.</exception>
    public static byte[] SerializeToUtf8Bytes(ChartAssemblyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureSynchronous(input);
        return JsonSerializer.SerializeToUtf8Bytes(input, FlintJsonContext.Default.ChartAssemblyInput);
    }

    /// <summary>Writes the document to <paramref name="writer"/>, which keeps its own formatting options.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="input">The document.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> or <paramref name="input"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The data is streamed from an asynchronous source (use
    /// <see cref="SerializeAsync"/>), or streamed data was already written.</exception>
    /// <remarks>Streamed rows from a synchronous source are written into <paramref name="writer"/>, which is flushed after
    /// each row; its target owns the buffering.</remarks>
    public static void Serialize(Utf8JsonWriter writer, ChartAssemblyInput input)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(input);
        EnsureSynchronous(input);
        JsonSerializer.Serialize(writer, input, FlintJsonContext.Default.ChartAssemblyInput);
    }

    /// <summary>Writes the document as a <see cref="JsonNode"/>.</summary>
    /// <param name="input">The document.</param>
    /// <returns>The document as a JSON object.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The data is streamed from an asynchronous source (use
    /// <see cref="SerializeAsync"/>), or streamed data was already written. Streamed rows from a synchronous source are
    /// written into the result.</exception>
    public static JsonNode SerializeToNode(ChartAssemblyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureSynchronous(input);
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

    /// <summary>Writes the document to <paramref name="utf8Json"/> asynchronously, as compact UTF-8 JSON with the bytes of
    /// <see cref="Serialize(ChartAssemblyInput, bool)"/>. Streamed data (<see cref="ChartData.IsStreamed"/>) is read from its
    /// source during the write, through a pooled buffer that is drained to the stream every 16 KiB, so memory does not grow
    /// with the row count; the stream is flushed once, at the end. Never calls the stream's synchronous <c>Write</c> or
    /// <c>Flush</c>, so it can write an ASP.NET Core response body directly. Never validates.</summary>
    /// <param name="utf8Json">The stream.</param>
    /// <param name="input">The document.</param>
    /// <param name="cancellationToken">Passed to the row source and to every write, and checked before each row.</param>
    /// <returns>The write.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Json"/> or <paramref name="input"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Streamed data was already written; it is single use.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled. The stream then holds a truncated document,
    /// which is never valid JSON.</exception>
    /// <remarks>An exception from the row source or the stream propagates unchanged; streamed data stays claimed and is not
    /// written again.</remarks>
    public static Task SerializeAsync(Stream utf8Json, ChartAssemblyInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);
        ArgumentNullException.ThrowIfNull(input);
        if (input.Data is not { Streamed: { } streamed } data)
        {
            return JsonSerializer.SerializeAsync(utf8Json, input, FlintJsonContext.Default.ChartAssemblyInput, cancellationToken);
        }

        streamed.Claim();
        return WriteStreamedAsync(utf8Json, input, data, streamed, cancellationToken);
    }

    /// <summary>Writes the document to <paramref name="utf8Json"/> as compact UTF-8 JSON, with the bytes of
    /// <see cref="Serialize(ChartAssemblyInput, bool)"/>. Streamed data from a synchronous source is read during the write,
    /// through a pooled buffer drained to the stream every 16 KiB; the stream is flushed once, at the end. Never
    /// validates.</summary>
    /// <param name="utf8Json">The stream.</param>
    /// <param name="input">The document.</param>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Json"/> or <paramref name="input"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The data is streamed from an asynchronous source (use
    /// <see cref="SerializeAsync"/>), or streamed data was already written.</exception>
    public static void Serialize(Stream utf8Json, ChartAssemblyInput input)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);
        ArgumentNullException.ThrowIfNull(input);
        if (input.Data is not { Streamed: { } streamed } data)
        {
            JsonSerializer.Serialize(utf8Json, input, FlintJsonContext.Default.ChartAssemblyInput);
            return;
        }

        streamed.EnsureSynchronous();
        streamed.Claim();
        using var document = new StreamedDocument(input);
        streamed.WriteRows(document.Writer, () =>
        {
            if (document.IsFull)
            {
                utf8Json.Write(document.Written.Span);
                document.Drained();
            }
        });
        document.End(data);
        utf8Json.Write(document.Written.Span);
        utf8Json.Flush();
    }

    private static async Task WriteStreamedAsync(Stream utf8Json, ChartAssemblyInput input, ChartData data, StreamedRows streamed, CancellationToken cancellationToken)
    {
        using var document = new StreamedDocument(input);
        await streamed.WriteRowsAsync(document.Writer, DrainAsync, cancellationToken).ConfigureAwait(false);
        document.End(data);
        await utf8Json.WriteAsync(document.Written, cancellationToken).ConfigureAwait(false);
        await utf8Json.FlushAsync(cancellationToken).ConfigureAwait(false);

        async ValueTask DrainAsync(CancellationToken token)
        {
            if (document.IsFull)
            {
                await utf8Json.WriteAsync(document.Written, token).ConfigureAwait(false);
                document.Drained();
            }
        }
    }

    /// <summary>The synchronous paths cannot read an asynchronous source; checked before anything is written.</summary>
    private static void EnsureSynchronous(ChartAssemblyInput input) => input.Data?.Streamed?.EnsureSynchronous();

    private static JsonException NullDocument() => new("A ChartAssemblyInput document cannot be null.");
}
