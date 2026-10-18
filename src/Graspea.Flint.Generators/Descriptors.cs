using Microsoft.CodeAnalysis;

namespace Graspea.Flint.Generators;

/// <summary>
/// The FLINT1xx diagnostics (design §12.6.7). The severity is the descriptor's default; the catalog checks report each case
/// at the validator's severity (<c>Diagnostic.Create</c> with an effective severity), so one id can carry several. Ids are
/// never reused; they are recorded in AnalyzerReleases.*.md.
/// </summary>
internal static class Descriptors
{
    private const string Category = "Flint";
    private const string HelpLink = "https://github.com/graspea/flint-chart-dotnet/blob/main/src/Graspea.Flint.Generators/README.md#diagnostics";

    public static readonly DiagnosticDescriptor ReferenceMismatch = Create("FLINT100", "Graspea.Flint is not referenced at the generator's version", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor SpecNotFound = Create("FLINT101", "The spec is not in AdditionalFiles", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor InvalidJson = Create("FLINT102", "The spec is not valid JSON", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor NotAChart = Create("FLINT103", "The spec is not a ChartAssemblyInput", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor InvalidTarget = Create("FLINT104", "The target is not a static partial class the generator can extend", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor InvalidAttribute = Create("FLINT105", "Invalid use of the FlintChart attribute", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor ContextWithoutRowType = Create("FLINT106", "The JsonContext may have no metadata for the row type", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor LanguageVersion = Create("FLINT107", "C# 12 or later is required", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor UnknownChartType = Create("FLINT110", "Unknown chart type", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor UnsupportedChannel = Create("FLINT111", "Channel declared by no backend of the template", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor InvalidOptionValue = Create("FLINT112", "Value outside the options of a chart property", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor UnknownSemanticType = Create("FLINT113", "Unknown semantic type", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor UnknownThemePreset = Create("FLINT114", "Unknown theme preset", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor SpecThrows = Create("FLINT115", "The spec makes the assemblers throw", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor SpecInfo = Create("FLINT116", "Catalog note", DiagnosticSeverity.Info);
    public static readonly DiagnosticDescriptor FieldWithoutMember = Create("FLINT120", "No member of the row type binds a spec field", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor IncompatibleMember = Create("FLINT121", "The member's type does not fit the field", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor RowMetadata = Create("FLINT122", "The row metadata breaks the row guarantees", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor AmbiguousMember = Create("FLINT123", "Two members bind one field", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor UncheckedMember = Create("FLINT124", "Member type not checked", DiagnosticSeverity.Info);
    public static readonly DiagnosticDescriptor UninferredField = Create("FLINT130", "A field type could not be inferred", DiagnosticSeverity.Info);
    public static readonly DiagnosticDescriptor NonUniformRows = Create("FLINT131", "Rows are not uniform objects", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor UrlNotUsed = Create("FLINT133", "data.url is not used by the generated members", DiagnosticSeverity.Info);
    public static readonly DiagnosticDescriptor UnknownKey = Create("FLINT140", "Unknown key passed through Extensions", DiagnosticSeverity.Info);

    private static readonly Dictionary<string, DiagnosticDescriptor> ById = new[]
    {
        ReferenceMismatch, SpecNotFound, InvalidJson, NotAChart, InvalidTarget, InvalidAttribute, ContextWithoutRowType, LanguageVersion,
        UnknownChartType, UnsupportedChannel, InvalidOptionValue, UnknownSemanticType, UnknownThemePreset, SpecThrows, SpecInfo,
        FieldWithoutMember, IncompatibleMember, RowMetadata, AmbiguousMember, UncheckedMember, UninferredField, NonUniformRows, UrlNotUsed, UnknownKey,
    }.ToDictionary(d => d.Id, StringComparer.Ordinal);

    public static DiagnosticDescriptor Get(string id) => ById[id];

    private static DiagnosticDescriptor Create(string id, string title, DiagnosticSeverity severity) =>
        new(id, title, "{0}", Category, severity, isEnabledByDefault: true, helpLinkUri: HelpLink);
}
