using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// Entry points of the fluent API: one set per template (<c>Chart.Line(rows)</c>, generated) and <c>Chart.Of</c> for any
/// chart type. The rows are written to JSON at the entry point; a selector such as <c>r =&gt; r.Month</c> later resolves to
/// the key the rows were written with.
/// </summary>
public static partial class Chart
{
    /// <summary>A builder for any chart type; the rows are written by reflection (camelCase keys by default).</summary>
    /// <typeparam name="TRow">The row type.</typeparam>
    /// <param name="chartType">The chart type, such as <c>new ChartType("Image Chart")</c>.</param>
    /// <param name="rows">The rows.</param>
    /// <param name="options">Key naming; <see cref="RowOptions.Default"/> when omitted. A literal <c>null</c> here does not
    /// compile (ambiguous with the <c>JsonTypeInfo</c> overload); omit the argument.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="chartType"/> is <c>default(ChartType)</c>, or a row member may
    /// be omitted when null.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> is null.</exception>
    [RequiresUnreferencedCode(RowMaterializer.ReflectionMessage)]
    [RequiresDynamicCode(RowMaterializer.ReflectionMessage)]
    public static GenericChartBuilder<TRow> Of<TRow>(ChartType chartType, IEnumerable<TRow> rows, RowOptions? options = null) =>
        new(Require(chartType), ChartRows.Of(rows, options));

    /// <summary>A builder for any chart type; the rows are written through <paramref name="typeInfo"/> (trimming- and
    /// AOT-safe).</summary>
    /// <typeparam name="TRow">The row type.</typeparam>
    /// <param name="chartType">The chart type.</param>
    /// <param name="rows">The rows.</param>
    /// <param name="typeInfo">Metadata for <typeparamref name="TRow"/> that writes nulls.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="chartType"/> is <c>default(ChartType)</c>, or
    /// <paramref name="typeInfo"/> may omit a null member or writes numbers as strings.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> or <paramref name="typeInfo"/> is null.</exception>
    /// <remarks>Values are written exactly as this metadata writes them, unlike the reflection overload: enums as numbers
    /// unless the context sets <c>UseStringEnumConverter = true</c>, and a <c>NaN</c> or infinite <c>double</c> throws unless
    /// the row maps it to <c>null</c>. Recommended:
    /// <c>[JsonSourceGenerationOptions(UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]</c>.</remarks>
    public static GenericChartBuilder<TRow> Of<TRow>(ChartType chartType, IEnumerable<TRow> rows, JsonTypeInfo<TRow> typeInfo) =>
        new(Require(chartType), ChartRows.Of(rows, typeInfo));

    /// <summary>A builder for any chart type over data that is already built: JSON rows or a URL. Selectors are not
    /// available; bind fields by name.</summary>
    /// <param name="chartType">The chart type.</param>
    /// <param name="data">The data.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="chartType"/> is <c>default(ChartType)</c>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    public static GenericChartBuilder<object> Of(ChartType chartType, ChartData data) => new(Require(chartType), ChartRows.Of(data));

    /// <summary>Not row data: a string is <c>IEnumerable&lt;char&gt;</c>. Use <c>Chart.Of(chartType, ChartData.FromUrl(url))</c>.</summary>
    /// <param name="chartType">The chart type.</param>
    /// <param name="url">The URL.</param>
    /// <returns>Never returns.</returns>
    [Obsolete("A string is IEnumerable<char>; use Chart.Of(chartType, ChartData.FromUrl(url)).", error: true)]
    public static GenericChartBuilder<object> Of(ChartType chartType, string url) => throw ChartRows.UrlIsNotRows();

    internal static ChartType Require(ChartType chartType) =>
        chartType.Name is null ? throw new ArgumentException("default(ChartType) is not a chart type.", nameof(chartType)) : chartType;
}
