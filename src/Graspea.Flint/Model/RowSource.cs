using System.Runtime.CompilerServices;

namespace Graspea.Flint;

/// <summary>Adapters that turn batched or paged row sources into one <see cref="IAsyncEnumerable{T}"/>, which every row
/// API accepts (design §13.4).</summary>
public static class RowSource
{
    /// <summary>The rows of every batch, in order; empty batches are allowed. Each enumeration enumerates
    /// <paramref name="batches"/> again and forwards its cancellation token to it. A page loader becomes a batch source
    /// with a short iterator that yields each page's rows.</summary>
    /// <typeparam name="TRow">The row type.</typeparam>
    /// <param name="batches">The batches.</param>
    /// <returns>The rows.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="batches"/> is null.</exception>
    /// <remarks>Enumerating throws <see cref="InvalidOperationException"/> at a null batch.</remarks>
    public static IAsyncEnumerable<TRow> FromBatches<TRow>(IAsyncEnumerable<IReadOnlyList<TRow>> batches)
    {
        ArgumentNullException.ThrowIfNull(batches);
        return Flatten(batches);
    }

    private static async IAsyncEnumerable<TRow> Flatten<TRow>(IAsyncEnumerable<IReadOnlyList<TRow>> batches, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var batch in batches.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (batch is null)
            {
                throw new InvalidOperationException("The batch source yielded a null batch.");
            }

            for (var i = 0; i < batch.Count; i++)
            {
                yield return batch[i];
            }
        }
    }
}
