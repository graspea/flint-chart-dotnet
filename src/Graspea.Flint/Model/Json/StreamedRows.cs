using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Graspea.Flint.Serialization;

/// <summary>
/// The rows of streamed <see cref="ChartData"/> (design §13.5): a source that is read exactly once, during the write that
/// claims it. Only <see cref="FlintJson.SerializeAsync"/> reads an asynchronous source; the synchronous paths refuse it
/// before claiming, so the data can still be written asynchronously. <see cref="FlintJson.SerializeAsync"/> also reads a
/// synchronous source asynchronously when its run-time object implements <see cref="IAsyncEnumerable{T}"/>.
/// </summary>
internal abstract class StreamedRows
{
    internal const string AlreadyWritten = "The streamed rows were already written; bind a new row source for each write.";

    internal const string AsynchronousSource =
        "The streamed rows come from an asynchronous source; write the document with FlintJson.SerializeAsync.";

    private int claimed;

    /// <summary>The keys every row writes, when the metadata fixes them (design §13.6); null for a dynamic shape.</summary>
    internal abstract IReadOnlyList<string>? Keys { get; }

    internal abstract bool IsAsynchronous { get; }

    /// <summary>Claims the rows for one write; a second or concurrent claim throws.</summary>
    internal void Claim()
    {
        if (Interlocked.Exchange(ref claimed, 1) != 0)
        {
            throw new InvalidOperationException(AlreadyWritten);
        }
    }

    internal void EnsureSynchronous()
    {
        if (IsAsynchronous)
        {
            throw new InvalidOperationException(AsynchronousSource);
        }
    }

    /// <summary>Writes every row of a synchronous source, each followed by <paramref name="afterRow"/>.</summary>
    internal abstract void WriteRows(Utf8JsonWriter writer, Action? afterRow);

    /// <summary>Writes every row, each followed by <paramref name="afterRow"/>; the token reaches the source and is
    /// checked before each row.</summary>
    internal abstract ValueTask WriteRowsAsync(Utf8JsonWriter writer, Func<CancellationToken, ValueTask> afterRow, CancellationToken cancellationToken);
}

internal sealed class StreamedRows<TRow> : StreamedRows
{
    private readonly IEnumerable<TRow>? rows;
    private readonly IAsyncEnumerable<TRow>? asyncRows;
    private readonly RowWriter<TRow> rowWriter;

    internal StreamedRows(IEnumerable<TRow> rows, JsonTypeInfo<TRow> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(rows);
        this.rows = rows;
        rowWriter = new RowWriter<TRow>(typeInfo);
        Keys = RowMaterializer.StaticKeys(typeInfo);
    }

    internal StreamedRows(IAsyncEnumerable<TRow> rows, JsonTypeInfo<TRow> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(rows);
        asyncRows = rows;
        rowWriter = new RowWriter<TRow>(typeInfo);
        Keys = RowMaterializer.StaticKeys(typeInfo);
    }

    internal override IReadOnlyList<string>? Keys { get; }

    internal override bool IsAsynchronous => asyncRows is not null;

    internal override void WriteRows(Utf8JsonWriter writer, Action? afterRow)
    {
        foreach (var row in rows!)
        {
            rowWriter.Write(writer, row);
            afterRow?.Invoke();
        }
    }

    internal override async ValueTask WriteRowsAsync(Utf8JsonWriter writer, Func<CancellationToken, ValueTask> afterRow, CancellationToken cancellationToken)
    {
        // A source typed as IEnumerable may also be asynchronous at run time, as an IQueryable of a database query is; it
        // is read asynchronously here, so the asynchronous write does no synchronous I/O (design §13.5, "Hosting").
        var source = asyncRows ?? rows as IAsyncEnumerable<TRow>;
        if (source is null)
        {
            foreach (var row in rows!)
            {
                cancellationToken.ThrowIfCancellationRequested();
                rowWriter.Write(writer, row);
                await afterRow(cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        await foreach (var row in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowWriter.Write(writer, row);
            await afterRow(cancellationToken).ConfigureAwait(false);
        }
    }
}
