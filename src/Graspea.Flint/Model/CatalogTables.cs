using System.Collections.Frozen;

namespace Graspea.Flint;

/// <summary>Row types and builders for the generated catalog tables (Generated/Tables/CatalogTables.g.cs).</summary>
internal static partial class CatalogTables
{
    private static FrozenSet<string> NameSet(params string[] names) => names.ToFrozenSet(StringComparer.Ordinal);

    private static string[] Names(params string[] names) => names;

    private static FrozenSet<FlintBackend> BackendSet(params FlintBackend[] backends) => backends.ToFrozenSet();

    private static FrozenDictionary<string, T> ByName<T>(params (string Name, T Value)[] rows) =>
        rows.ToFrozenDictionary(r => r.Name, r => r.Value, StringComparer.Ordinal);

    private static FrozenDictionary<FlintBackend, T> ByBackend<T>(params (FlintBackend Backend, T Value)[] rows) =>
        rows.ToFrozenDictionary(r => r.Backend, r => r.Value);
}

/// <summary>One template on one backend.</summary>
internal sealed class TemplateRow
{
    internal TemplateRow(string[] channels, PropertyRow[] properties, bool supportsFacets = true, bool hasValidate = false)
    {
        Channels = channels.ToFrozenSet(StringComparer.Ordinal);
        Properties = properties.ToFrozenDictionary(p => p.Key, StringComparer.Ordinal);
        SupportsFacets = supportsFacets;
        HasValidate = hasValidate;
    }

    internal FrozenSet<string> Channels { get; }

    internal FrozenDictionary<string, PropertyRow> Properties { get; }

    /// <summary>False where the backend throws on a bound <c>column</c>/<c>row</c> (Excel).</summary>
    internal bool SupportsFacets { get; }

    /// <summary>The Excel template has a <c>validate()</c> that rejects some inputs.</summary>
    internal bool HasValidate { get; }
}

/// <summary>A declared <c>chartProperties</c> key. <paramref name="Options"/> holds discrete values as compact JSON (null = omitted
/// default) and <paramref name="Labels"/> their display labels, in the same order.</summary>
internal sealed record PropertyRow(string Key, PropertyKind Kind, string?[]? Options = null, string[]? Labels = null, double? Min = null, double? Max = null, double? Step = null);

internal enum PropertyKind
{
    Binary,
    Discrete,
    Continuous,
}
