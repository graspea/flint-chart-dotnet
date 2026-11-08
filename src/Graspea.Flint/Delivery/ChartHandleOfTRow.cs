using System.Text.Json;
using Graspea.Flint.Serialization;
using Graspea.Flint.Validation;

namespace Graspea.Flint.Delivery;

/// <summary>A chart a host serves rows for, typed by its row type: an id, a data-free template and an optional row cap.</summary>
/// <typeparam name="TRow">The row type.</typeparam>
public sealed class ChartHandle<TRow> : ChartHandle
{
    /// <summary>Creates a handle.</summary>
    /// <param name="id">The chart id, matching <c>[A-Za-z0-9_.-]+</c>.</param>
    /// <param name="template">The template; it needs row metadata and must have no <c>data</c> member.</param>
    /// <param name="maxRows">The most rows one part may hold, or null for no cap; it caps one Rows, RowsAsync or Stream call.</param>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> or <paramref name="template"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> is outside the alphabet, the template has no row metadata,
    /// or its document has a <c>data</c> member.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxRows"/> is below 1.</exception>
    public ChartHandle(string id, ChartTemplate<TRow> template, int? maxRows = null)
        : base(id, maxRows)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (template.RowTypeInfo is null)
        {
            throw new ArgumentException(
                "The template has no row metadata (its builder was entered with ChartData), so it cannot write rows; create it from a JsonTypeInfo or RowOptions entry.",
                nameof(template));
        }

        if (HasDataMember(template.SpecUtf8.Span))
        {
            throw new ArgumentException(
                "The template's document has a data member, for example from a Configure action that writes Extensions; a chart handle needs a data-free template.",
                nameof(template));
        }

        Template = template;
    }

    /// <summary>The data-free template.</summary>
    public ChartTemplate<TRow> Template { get; }

    /// <summary>A part holding the rows of a synchronous source, written to JSON now. The bytes equal the elements of
    /// <c>data.values</c> of the streamed document (<c>FlintJson.SerializeAsync</c> over <c>BindStream</c> with the same rows), and
    /// those of <c>Bind(rows)</c> for rows whose converters write JSON tokens; a row converter that writes raw JSON can differ
    /// from <c>Bind</c> in insignificant whitespace and is then JSON-equal.</summary>
    /// <param name="rows">The rows, enumerated once; at most <see cref="ChartHandle.MaxRows"/> of them.</param>
    /// <returns>The part.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> is null.</exception>
    /// <exception cref="ChartRowLimitException">The source yields more rows than <see cref="ChartHandle.MaxRows"/>; no excess
    /// row is serialised.</exception>
    /// <exception cref="ArgumentException">A row omits a member the metadata writes (a value-dependent predicate), or a
    /// value cannot be written as JSON, as a non-finite number under <c>JsonTypeInfo</c> metadata.</exception>
    public ChartPart Rows(IEnumerable<TRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        using var buffer = new PooledBufferWriter(256);
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { MaxDepth = JsonDepth.Effective - 2 });
        writer.WriteStartArray();
        new StreamedRows<TRow>(RowLimit.Apply(rows, this), Template.RowTypeInfo!).WriteRows(writer, null);
        writer.WriteEndArray();
        writer.Flush();
        return new ChartPart(this, buffer.WrittenMemory.ToArray());
    }

    /// <summary>A part holding the rows of an asynchronous source, written to JSON now; the bytes are those <see cref="Rows"/>
    /// gives for the same rows.</summary>
    /// <param name="rows">The rows, enumerated once; at most <see cref="ChartHandle.MaxRows"/> of them.</param>
    /// <param name="cancellationToken">Reaches the source; cancelling throws <see cref="OperationCanceledException"/> and no part is returned, also when the token is
    /// cancelled before the call or while the source ends, whether or not the source observes it.</param>
    /// <returns>The part.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> is null.</exception>
    /// <exception cref="ChartRowLimitException">The source yields more rows than <see cref="ChartHandle.MaxRows"/>; no excess
    /// row is serialised.</exception>
    /// <exception cref="ArgumentException">A row omits a member the metadata writes, or a value cannot be written as JSON.</exception>
    public ValueTask<ChartPart> RowsAsync(IAsyncEnumerable<TRow> rows, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return RowsCoreAsync(rows, cancellationToken);
    }

    private async ValueTask<ChartPart> RowsCoreAsync(IAsyncEnumerable<TRow> rows, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var buffer = new PooledBufferWriter(256);
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { MaxDepth = JsonDepth.Effective - 2 });
        writer.WriteStartArray();
        await new StreamedRows<TRow>(RowLimit.Apply(rows, this), Template.RowTypeInfo!)
            .WriteRowsAsync(writer, static _ => ValueTask.CompletedTask, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        writer.WriteEndArray();
        writer.Flush();
        return new ChartPart(this, buffer.WrittenMemory.ToArray());
    }

    /// <summary>A streamed part: the rows of an asynchronous source are read and written while the page is written, so nothing
    /// is enumerated here. The part is single-use: <see cref="DataPage.WriteAsync"/> claims it before the first byte, and the
    /// synchronous finishers of its page throw. The bytes equal those <see cref="RowsAsync"/> gives for the same rows. A row,
    /// limit or source failure surfaces from <see cref="DataPage.WriteAsync"/>; after the first drain the stream holds a
    /// truncated prefix of the page.</summary>
    /// <param name="rows">The rows, enumerated once, during the write; at most <see cref="ChartHandle.MaxRows"/> of them.</param>
    /// <returns>The part.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> is null.</exception>
    public ChartPart Stream(IAsyncEnumerable<TRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return new ChartPart(this, new StreamedRows<TRow>(RowLimit.Apply(rows, this), Template.RowTypeInfo!));
    }

    internal override ReadOnlyMemory<byte> SpecUtf8 => Template.SpecUtf8;

    internal override ValidationResult ValidateTemplate(FlintBackend? backend) => Template.Validate(backend);

    private static bool HasDataMember(ReadOnlySpan<byte> spec)
    {
        var reader = new Utf8JsonReader(spec);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1 && reader.ValueTextEquals("data"u8))
            {
                return true;
            }
        }

        return false;
    }
}
