using Graspea.Flint.Serialization;

namespace Graspea.Flint.Delivery;

/// <summary>One chart's part of a page: its rows as UTF-8, or a withheld code. Created by a handle only; immutable unless streamed, which is written once.</summary>
public sealed class ChartPart
{
    internal ChartPart(ChartHandle chart, string withheldCode)
    {
        Chart = chart;
        WithheldCode = withheldCode;
    }

    internal ChartPart(ChartHandle chart, byte[] utf8Rows)
    {
        Chart = chart;
        Utf8Rows = utf8Rows;
    }

    internal ChartPart(ChartHandle chart, StreamedRows streamed)
    {
        Chart = chart;
        Streamed = streamed;
    }

    /// <summary>The chart this part belongs to.</summary>
    public ChartHandle Chart { get; }

    /// <summary>The withheld code, or null when the part holds rows.</summary>
    public string? WithheldCode { get; }

    /// <summary>True when the rows are written from a source while the page is written.</summary>
    public bool IsStreamed => Streamed is not null;

    /// <summary>The rows of a streamed part.</summary>
    internal StreamedRows? Streamed { get; }

    /// <summary>The rows of a materialised part as a JSON array in UTF-8.</summary>
    internal byte[]? Utf8Rows { get; }
}
