using System.Runtime.CompilerServices;

namespace Graspea.Flint.Delivery;

internal static class RowLimit
{
    /// <summary>The rows, or an iterator that throws when the source yields row MaxRows + 1.</summary>
    internal static IEnumerable<T> Apply<T>(IEnumerable<T> rows, ChartHandle chart)
    {
        if (chart.MaxRows is not { } max)
        {
            return rows;
        }

        return Limited(rows, chart, max);
    }

    private static IEnumerable<T> Limited<T>(IEnumerable<T> rows, ChartHandle chart, int max)
    {
        // The count stops at max, so it cannot overflow even when max is int.MaxValue.
        var count = 0;
        foreach (var row in rows)
        {
            if (count == max)
            {
                throw new ChartRowLimitException(chart, max);
            }

            count++;
            yield return row;
        }
    }

    /// <summary>The rows, or an iterator that throws when the source yields row MaxRows + 1.</summary>
    internal static IAsyncEnumerable<T> Apply<T>(IAsyncEnumerable<T> rows, ChartHandle chart)
    {
        if (chart.MaxRows is not { } max)
        {
            return rows;
        }

        return LimitedAsync(rows, chart, max);
    }

    private static async IAsyncEnumerable<T> LimitedAsync<T>(
        IAsyncEnumerable<T> rows, ChartHandle chart, int max, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var count = 0;
        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (count == max)
            {
                throw new ChartRowLimitException(chart, max);
            }

            count++;
            yield return row;
        }
    }
}
