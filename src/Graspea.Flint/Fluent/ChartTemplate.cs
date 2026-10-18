using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Graspea.Flint.Serialization;
using Graspea.Flint.Validation;
using Graspea.Flint.Validation.Rules;

namespace Graspea.Flint;

/// <summary>
/// A chart spec without rows plus the row metadata to write rows with: build it once, store it in a static field, and bind
/// rows per request, from any thread (design §13.3.3). Create one with <c>ToTemplate()</c> on a builder or with
/// <see cref="ChartTemplate.Create{TRow}(ChartAssemblyInput, JsonTypeInfo{TRow})"/>. Immutable and thread-safe; every bind
/// returns a new document that shares no object with any other.
/// </summary>
/// <typeparam name="TRow">The row type.</typeparam>
/// <example>
/// <code>
/// static readonly ChartTemplate&lt;Sale&gt; Revenue =
///     Chart.Line(SalesJson.Default.Sale).X(r =&gt; r.Month).Y(r =&gt; r.Revenue).ToTemplate();
///
/// ChartAssemblyInput input = Revenue.Bind(sales);
/// await FlintJson.SerializeAsync(response.Body, Revenue.BindStream(db.Sales.AsAsyncEnumerable()), ct);
/// </code>
/// </example>
public sealed class ChartTemplate<TRow>
{
    private const string NoMetadata =
        "The template has no row metadata (its builder was entered with ChartData), so it cannot write rows; bind a ChartData instead.";

    private readonly byte[] spec;
    private readonly JsonTypeInfo<TRow>? typeInfo;
    private readonly IReadOnlyList<string>? keys;

    /// <summary>Serialises <paramref name="input"/> without its data once; the metadata is checked now, before any row.</summary>
    internal ChartTemplate(ChartAssemblyInput input, JsonTypeInfo<TRow>? typeInfo)
    {
        ArgumentNullException.ThrowIfNull(input);
        spec = JsonSerializer.SerializeToUtf8Bytes(input.WithoutData(), FlintJsonContext.Default.ChartAssemblyInput);
        _ = FlintJson.Deserialize(spec);
        if (typeInfo is not null)
        {
            _ = new RowWriter<TRow>(typeInfo);
            keys = RowMaterializer.StaticKeys(typeInfo);
        }

        this.typeInfo = typeInfo;
    }

    /// <summary>A new document with the rows written to JSON now, as <see cref="ChartData.FromRows{TRow}(IEnumerable{TRow}, JsonTypeInfo{TRow})"/>
    /// writes them.</summary>
    /// <param name="rows">The rows.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The template has no row metadata.</exception>
    /// <exception cref="ArgumentException">A row omits a member the metadata writes (a value-dependent predicate).</exception>
    public ChartAssemblyInput Bind(IEnumerable<TRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return Bind(ChartData.FromRows(rows, Metadata()));
    }

    /// <summary>A new document with <paramref name="data"/> in any state (values, url or streamed), used as given.</summary>
    /// <param name="data">The data.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    public ChartAssemblyInput Bind(ChartData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var input = FlintJson.Deserialize(spec);
        input.Data = data;
        return input;
    }

    /// <summary>A new document with the rows of an asynchronous source written to JSON, as
    /// <see cref="ChartData.FromRowsAsync{TRow}(IAsyncEnumerable{TRow}, JsonTypeInfo{TRow}, CancellationToken)"/> writes
    /// them; the rows are enumerated once, before the task completes.</summary>
    /// <param name="rows">The rows; end a database query with <c>.AsAsyncEnumerable()</c>.</param>
    /// <param name="cancellationToken">Passed to the source and checked before each row.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The template has no row metadata.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public ValueTask<ChartAssemblyInput> BindAsync(IAsyncEnumerable<TRow> rows, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return BindWhenRead(ChartData.FromRowsAsync(rows, Metadata(), cancellationToken));
    }

    /// <summary>A new document whose rows are written straight from <paramref name="rows"/> when the document is written
    /// (<see cref="ChartData.Stream{TRow}(IEnumerable{TRow}, JsonTypeInfo{TRow})"/>). Single use: write it once, with
    /// <see cref="FlintJson.SerializeAsync"/> or <see cref="FlintJson.Serialize(System.IO.Stream, ChartAssemblyInput)"/>.</summary>
    /// <param name="rows">The rows. An <c>IQueryable</c> binds here; <see cref="FlintJson.SerializeAsync"/> reads it
    /// asynchronously when its run-time object implements <see cref="IAsyncEnumerable{T}"/>, as an Entity Framework Core
    /// query does, and otherwise with synchronous I/O: end a database query with <c>.AsAsyncEnumerable()</c>.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The template has no row metadata.</exception>
    public ChartAssemblyInput BindStream(IEnumerable<TRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return Bind(ChartData.Stream(rows, Metadata()));
    }

    /// <summary>A new document whose rows are written straight from an asynchronous source when the document is written
    /// with <see cref="FlintJson.SerializeAsync"/>, with memory that does not grow with the row count. Single use.</summary>
    /// <param name="rows">The rows. Preferred over the <c>IEnumerable</c> overload for a source that implements both
    /// (C# 13 or later); under C# 12, call <c>.AsAsyncEnumerable()</c> on such a source.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The template has no row metadata.</exception>
    [OverloadResolutionPriority(1)]
    public ChartAssemblyInput BindStream(IAsyncEnumerable<TRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return Bind(ChartData.Stream(rows, Metadata()));
    }

    /// <summary>Validates the spec and the row metadata without rows (design §13.6): the spec rules run, the key rules run
    /// on the keys the row type writes (skipped without metadata or for a dynamic row shape), <c>data_missing</c> is not
    /// reported, <c>series_field_missing</c> is a Warning, and <c>rows_not_inspected</c> names the row rules that were
    /// skipped. Validate a sample with every rule through <c>ChartValidator.Validate(template.Bind(sample), backend)</c>.</summary>
    /// <param name="backend">The target backend; null means every backend that has the template.</param>
    /// <returns>The issues; <see cref="ValidationResult.IsValid"/> means no Error that can be decided without the rows.</returns>
    public ValidationResult Validate(FlintBackend? backend = null) =>
        ChartValidator.ValidateDeferred(FlintJson.Deserialize(spec), backend, new DeferredRows(keys, Template: true));

    private JsonTypeInfo<TRow> Metadata() => typeInfo ?? throw new InvalidOperationException(NoMetadata);

    private async ValueTask<ChartAssemblyInput> BindWhenRead(ValueTask<ChartData> data) => Bind(await data.ConfigureAwait(false));
}

/// <summary>Creates a <see cref="ChartTemplate{TRow}"/> from a document, such as a spec read from a JSON file at run time.</summary>
public static class ChartTemplate
{
    /// <summary>A template from <paramref name="spec"/> and row metadata (trimming- and AOT-safe). The spec is copied
    /// without its data, whatever state the data is in (streamed data is not claimed), and is not changed. Its field names
    /// must equal the keys the metadata writes; <see cref="ChartTemplate{TRow}.Validate"/> reports <c>unknown_field</c>
    /// otherwise.</summary>
    /// <typeparam name="TRow">The row type.</typeparam>
    /// <param name="spec">The document, with or without data.</param>
    /// <param name="typeInfo">Metadata for <typeparamref name="TRow"/> that writes nulls.</param>
    /// <returns>The template.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> or <paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="typeInfo"/> may omit a null member or writes numbers as
    /// strings.</exception>
    public static ChartTemplate<TRow> Create<TRow>(ChartAssemblyInput spec, JsonTypeInfo<TRow> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return new(spec, typeInfo);
    }

    /// <summary>A template from <paramref name="spec"/> whose rows are written by reflection (camelCase keys by default).
    /// The metadata is resolved now, so binding is trimming- and AOT-safe.</summary>
    /// <typeparam name="TRow">The row type.</typeparam>
    /// <param name="spec">The document, with or without data.</param>
    /// <param name="options">Key naming; <see cref="RowOptions.Default"/> when null.</param>
    /// <returns>The template.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is null.</exception>
    /// <exception cref="ArgumentException">A row member may be omitted when null, or is written as a string number.</exception>
    [RequiresUnreferencedCode(RowMaterializer.ReflectionMessage)]
    [RequiresDynamicCode(RowMaterializer.ReflectionMessage)]
    public static ChartTemplate<TRow> Create<TRow>(ChartAssemblyInput spec, RowOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return new(spec, RowMaterializer.TypeInfo<TRow>(options));
    }
}
