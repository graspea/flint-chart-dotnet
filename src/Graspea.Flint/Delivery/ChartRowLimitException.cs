namespace Graspea.Flint.Delivery;

/// <summary>A source yielded more rows than <see cref="ChartHandle.MaxRows"/> allows for one part. It is thrown when the
/// source yields row MaxRows + 1, before that row is serialised, and for a materialised part before any byte of the page.</summary>
public sealed class ChartRowLimitException : InvalidOperationException
{
    internal ChartRowLimitException(ChartHandle chart, int limit)
        : base($"Chart \"{chart.Id}\" has more than {limit} rows (ChartHandle.MaxRows).")
    {
        Chart = chart;
        Limit = limit;
    }

    /// <summary>The chart whose limit was exceeded.</summary>
    public ChartHandle Chart { get; }

    /// <summary>The limit, <see cref="ChartHandle.MaxRows"/>.</summary>
    public int Limit { get; }
}
