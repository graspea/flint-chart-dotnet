using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// The <c>data</c> member: materialised row values (<c>{"values":[…]}</c>) or a URL (<c>{"url":…}</c>), which
/// flint-chart never fetches. The converter owns the wire shape.
/// </summary>
[JsonConverter(typeof(ChartDataConverter))]
public sealed partial class ChartData
{
    private ChartData(JsonArray? values, string? url)
    {
        Values = values;
        Url = url;
    }

    /// <summary>The rows as written; nulls are explicit.</summary>
    public JsonArray? Values { get; }

    /// <summary>The <c>url</c> member.</summary>
    public string? Url { get; }

    /// <summary>The keys of the first row as flint-chart's <c>Object.keys(data[0])</c> sees them; empty when there are no
    /// rows or the first row is not an object. Field detection and static series use exactly these keys.</summary>
    public IReadOnlyList<string> FirstRowKeys => RowMaterializer.FirstRowKeys(Values);

    /// <summary>Members of <c>data</c> this version does not model; written after the known members.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; set; }

    /// <summary>Writes each row to JSON now, by reflection: keys follow <paramref name="options"/> (camelCase by
    /// default) or <c>[JsonPropertyName]</c>, enums are written as names, nulls are always written.</summary>
    /// <typeparam name="TRow">The row type.</typeparam>
    /// <param name="rows">The rows.</param>
    /// <param name="options">Key naming; <see cref="RowOptions.Default"/> when null.</param>
    /// <returns>The data.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> is null.</exception>
    /// <exception cref="ArgumentException">A row member may be omitted when null (<c>[JsonIgnore(Condition = …)]</c>),
    /// which would hide its key from flint-chart's key detection, or is written as a string number
    /// (<c>[JsonNumberHandling]</c>).</exception>
    [RequiresUnreferencedCode(RowMaterializer.ReflectionMessage)]
    [RequiresDynamicCode(RowMaterializer.ReflectionMessage)]
    public static ChartData FromRows<TRow>(IEnumerable<TRow> rows, RowOptions? options = null) =>
        new(RowMaterializer.Materialize(rows, RowMaterializer.TypeInfo<TRow>(options)), null);

    /// <summary>Writes each row to JSON now, through <paramref name="typeInfo"/> (trimming- and AOT-safe). The metadata
    /// must write nulls: <c>DefaultIgnoreCondition = Never</c> and no conditionally ignored member.</summary>
    /// <typeparam name="TRow">The row type.</typeparam>
    /// <param name="rows">The rows.</param>
    /// <param name="typeInfo">Source-generated or custom metadata for <typeparamref name="TRow"/>.</param>
    /// <returns>The data.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> or <paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="typeInfo"/> may omit a null member, which would hide its key
    /// from flint-chart's key detection, or writes numbers as strings.</exception>
    /// <remarks>Values are written exactly as this metadata writes them, unlike the reflection overload: enums as numbers
    /// unless the context sets <c>UseStringEnumConverter = true</c>, and a <c>NaN</c> or infinite <c>double</c> throws unless
    /// the row maps it to <c>null</c>. Recommended:
    /// <c>[JsonSourceGenerationOptions(UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]</c>.</remarks>
    public static ChartData FromRows<TRow>(IEnumerable<TRow> rows, JsonTypeInfo<TRow> typeInfo) =>
        new(RowMaterializer.Materialize(rows, typeInfo), null);

    /// <summary>Rows that are already JSON; the array is used as-is, not copied.</summary>
    /// <param name="values">The rows.</param>
    /// <returns>The data.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is null.</exception>
    public static ChartData FromJson(JsonArray values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new(values, null);
    }

    /// <summary>Data that flint-chart does not fetch: the document carries only the URL.</summary>
    /// <param name="url">The URL.</param>
    /// <returns>The data.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="url"/> is null.</exception>
    public static ChartData FromUrl(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return new(null, url);
    }

    internal static ChartData Create(JsonArray? values, string? url, Dictionary<string, JsonElement>? extensions) =>
        new(values, url) { Extensions = extensions };
}
