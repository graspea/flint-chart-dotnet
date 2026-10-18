using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

public sealed partial class ChartData
{
    private ChartData(StreamedRows streamed)
    {
        Streamed = streamed;
    }

    /// <summary>True for streamed rows (<c>Stream</c>, <c>ChartTemplate.BindStream</c>): single use, read during the
    /// write, never held; <see cref="Values"/> and <see cref="Url"/> are null.</summary>
    public bool IsStreamed => Streamed is not null;

    internal StreamedRows? Streamed { get; }

    /// <summary>Rows written straight from <paramref name="rows"/> to the output when the document is written, through
    /// <paramref name="typeInfo"/> (trimming- and AOT-safe), with memory that does not grow with the row count. The
    /// source is not read now. Single use: the first write enumerates it; a second write throws.</summary>
    /// <typeparam name="TRow">The row type.</typeparam>
    /// <param name="rows">The rows. An <c>IQueryable</c> binds here; <see cref="FlintJson.SerializeAsync"/> reads it
    /// asynchronously when its run-time object implements <see cref="IAsyncEnumerable{T}"/>, as an Entity Framework Core
    /// query does, and otherwise with synchronous I/O: end a database query with <c>.AsAsyncEnumerable()</c>.</param>
    /// <param name="typeInfo">Metadata for <typeparamref name="TRow"/> that writes nulls.</param>
    /// <returns>The streamed data.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> or <paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="typeInfo"/> may omit a null member or writes numbers as
    /// strings.</exception>
    /// <remarks>Write the document with <see cref="FlintJson.SerializeAsync"/> or
    /// <see cref="FlintJson.Serialize(System.IO.Stream, ChartAssemblyInput)"/> to keep the memory bound; the in-memory paths
    /// write the rows into their result. A builder does not accept streamed data; use
    /// <see cref="ChartTemplate{TRow}.BindStream(IEnumerable{TRow})"/>.</remarks>
    public static ChartData Stream<TRow>(IEnumerable<TRow> rows, JsonTypeInfo<TRow> typeInfo) => new(new StreamedRows<TRow>(rows, typeInfo));

    /// <summary>Rows written straight from an asynchronous source to the output when the document is written with
    /// <see cref="FlintJson.SerializeAsync"/>, through <paramref name="typeInfo"/> (trimming- and AOT-safe), with memory
    /// that does not grow with the row count. The source is not read now. Single use.</summary>
    /// <typeparam name="TRow">The row type.</typeparam>
    /// <param name="rows">The rows. Preferred over the <c>IEnumerable</c> overload for a source that implements both
    /// (C# 13 or later); under C# 12, call <c>.AsAsyncEnumerable()</c> on such a source.</param>
    /// <param name="typeInfo">Metadata for <typeparamref name="TRow"/> that writes nulls.</param>
    /// <returns>The streamed data.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> or <paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="typeInfo"/> may omit a null member or writes numbers as
    /// strings.</exception>
    /// <remarks>Every synchronous serialisation path throws <see cref="InvalidOperationException"/> for this data.</remarks>
    [OverloadResolutionPriority(1)]
    public static ChartData Stream<TRow>(IAsyncEnumerable<TRow> rows, JsonTypeInfo<TRow> typeInfo) => new(new StreamedRows<TRow>(rows, typeInfo));

    /// <summary>Rows written straight from <paramref name="rows"/> to the output when the document is written, by
    /// reflection (camelCase keys by default), as <see cref="FromRows{TRow}(IEnumerable{TRow}, RowOptions?)"/> writes
    /// them. The source is not read now. Single use.</summary>
    /// <typeparam name="TRow">The row type.</typeparam>
    /// <param name="rows">The rows; end a database query with <c>.AsAsyncEnumerable()</c>.</param>
    /// <param name="options">Key naming; <see cref="RowOptions.Default"/> when null.</param>
    /// <returns>The streamed data.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> is null.</exception>
    /// <exception cref="ArgumentException">A row member may be omitted when null, or is written as a string number.</exception>
    [RequiresUnreferencedCode(RowMaterializer.ReflectionMessage)]
    [RequiresDynamicCode(RowMaterializer.ReflectionMessage)]
    public static ChartData Stream<TRow>(IEnumerable<TRow> rows, RowOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return new(new StreamedRows<TRow>(rows, RowMaterializer.TypeInfo<TRow>(options)));
    }

    /// <summary>Rows written straight from an asynchronous source to the output when the document is written with
    /// <see cref="FlintJson.SerializeAsync"/>, by reflection (camelCase keys by default). The source is not read now.
    /// Single use.</summary>
    /// <typeparam name="TRow">The row type.</typeparam>
    /// <param name="rows">The rows.</param>
    /// <param name="options">Key naming; <see cref="RowOptions.Default"/> when null.</param>
    /// <returns>The streamed data.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> is null.</exception>
    /// <exception cref="ArgumentException">A row member may be omitted when null, or is written as a string number.</exception>
    [RequiresUnreferencedCode(RowMaterializer.ReflectionMessage)]
    [RequiresDynamicCode(RowMaterializer.ReflectionMessage)]
    [OverloadResolutionPriority(1)]
    public static ChartData Stream<TRow>(IAsyncEnumerable<TRow> rows, RowOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return new(new StreamedRows<TRow>(rows, RowMaterializer.TypeInfo<TRow>(options)));
    }
}
