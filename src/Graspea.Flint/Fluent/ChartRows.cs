using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>The data of a builder and the row metadata its selectors resolve through, captured once at the entry point
/// (design §5.3 rule 4); null metadata for data entered as <see cref="ChartData"/>.</summary>
internal readonly record struct ChartRows<TRow>(ChartData Data, JsonTypeInfo<TRow>? TypeInfo);

/// <summary>The three data forms of every entry point (design §4.2); generated entries delegate here.</summary>
internal static class ChartRows
{
    internal const string StringIsNotRows = "A string is IEnumerable<char>, not row data; pass ChartData.FromUrl(url) instead.";

    [RequiresUnreferencedCode(RowMaterializer.ReflectionMessage)]
    [RequiresDynamicCode(RowMaterializer.ReflectionMessage)]
    internal static ChartRows<TRow> Of<TRow>(IEnumerable<TRow> rows, RowOptions? options)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var typeInfo = RowMaterializer.TypeInfo<TRow>(options);
        return new(ChartData.FromRows(rows, typeInfo), typeInfo);
    }

    internal static ChartRows<TRow> Of<TRow>(IEnumerable<TRow> rows, JsonTypeInfo<TRow> typeInfo) => new(ChartData.FromRows(rows, typeInfo), typeInfo);

    internal static ChartRows<object> Of(ChartData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new(data, null);
    }

    /// <summary>The body of the error-marked <c>(string)</c> entry overloads, which no compiled call can reach.</summary>
    internal static NotSupportedException UrlIsNotRows() => new(StringIsNotRows);
}
