using Graspea.Flint.Validation;

namespace Graspea.Flint.Delivery;

/// <summary>A chart a host serves rows for: an id, a data-free template and an optional row cap. Create one with
/// <see cref="ChartHandle{TRow}"/>; it cannot be derived from.</summary>
public abstract class ChartHandle
{
    internal ChartHandle(string id, int? maxRows)
    {
        DeliveryNames.Check(id, nameof(id), "chart id");
        if (maxRows is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRows), maxRows, "MaxRows must be at least 1.");
        }

        Id = id;
        MaxRows = maxRows;
    }

    /// <summary>The chart id, matching <c>[A-Za-z0-9_.-]+</c>; the key of the chart in the manifest and in every page.</summary>
    public string Id { get; }

    /// <summary>The most rows one part may hold, or null for no cap. It caps one part (the rows of one Rows, RowsAsync or
    /// Stream call), not a chart across a paging sequence: a host caps the whole result across pages itself.</summary>
    public int? MaxRows { get; }

    /// <summary>A part that tells the browser the chart's rows are withheld, written as <c>{"withheld":code}</c>.</summary>
    /// <param name="code">A non-empty, host-defined code, written as given.</param>
    /// <returns>The part.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="code"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="code"/> is empty.</exception>
    public ChartPart Withheld(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.Length == 0)
        {
            throw new ArgumentException("A withheld code must not be empty.", nameof(code));
        }

        return new ChartPart(this, code);
    }

    /// <summary>The stored bytes of the template's document; the memory is shared and must not be modified.</summary>
    internal abstract ReadOnlyMemory<byte> SpecUtf8 { get; }

    /// <summary>Validates the template for <paramref name="backend"/>.</summary>
    internal abstract ValidationResult ValidateTemplate(FlintBackend? backend);
}
