namespace Graspea.Flint.CSharp;

/// <summary>The kind of a template property in the catalog.</summary>
internal enum CatalogPropertyKind
{
    Binary,
    Discrete,
    Continuous,
}

/// <summary>
/// A template property: its key, kind, the JSON text of each option value (null for an option that omits the key) with the
/// option labels, and the declared range of a continuous property.
/// </summary>
internal sealed record CatalogPropertyRow(string Key, CatalogPropertyKind Kind, string?[]? Options, string[]? Labels, double? Min, double? Max);

/// <summary>The template of a chart type on one backend: the backend id, its channels and its properties.</summary>
internal sealed record BackendTemplateRow(string Backend, string[] Channels, CatalogPropertyRow[] Properties)
{
    public CatalogPropertyRow? Property(string key) => Array.Find(Properties, p => p.Key == key);
}

/// <summary>A chart type and its templates, in the catalog's backend order.</summary>
internal sealed record ChartTypeRow(string Name, BackendTemplateRow[] Templates);

/// <summary>
/// The catalog the checks of design §12.6.7 read: templates per backend, the backends that apply themes and the host-state
/// keys. The arrays are written by the csharp-names emitter (<c>Generated/Catalog.g.cs</c>); the lookup is built on first use.
/// </summary>
internal static partial class Catalog
{
    private static Dictionary<string, ChartTypeRow>? _chartTypes;

    public static ChartTypeRow? ChartType(string name)
    {
        _chartTypes ??= ChartTypes.ToDictionary(c => c.Name, StringComparer.Ordinal);
        return _chartTypes.TryGetValue(name, out var found) ? found : null;
    }
}
