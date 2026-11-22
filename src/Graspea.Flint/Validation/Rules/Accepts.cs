using System.Text.Json;
using System.Text.Json.Nodes;

namespace Graspea.Flint.Validation.Rules;

/// <summary>
/// The <c>accepts</c> predicates of the pinned package (flint-chart 1.1.0+): a discrete chart property keeps a value outside
/// its options when its template def's <c>accepts</c> holds for it (<c>normalizeChartProperties</c>,
/// src/core/normalize-properties.ts:63-65 of flint-chart 1.1.1). The predicates are code, so they are ported by hand, per
/// property key; <c>generator/overrides.json</c> <c>acceptsSource</c> fingerprints the files they are read from, so a sync
/// stops when one changes (docs/generator.md, "Accepting properties"). The mirror over the core's JSON is in
/// <c>src/Graspea.Flint.CSharp/Spec/CatalogCheck.cs</c>.
/// </summary>
internal static class Accepts
{
    /// <summary><c>BOUNDARY_IDS</c> (src/chart-types/boundaries.ts:28).</summary>
    private static readonly string[] BoundaryIds = ["iso-numeric", "iso3", "iso2", "fips", "as-is"];

    /// <summary><c>MAP_STYLES</c> (src/vegalite/templates/map-style.ts:55).</summary>
    private static readonly string[] MapStyles = ["auto", "light", "muted", "atlas", "dark", "minimal"];

    /// <summary><c>CUSTOM_COLORS</c> (src/vegalite/templates/map-style.ts:74).</summary>
    private static readonly string[] CustomColors = ["sea", "land", "coast", "border", "noData", "graticule", "disputed"];

    /// <summary>
    /// Whether upstream keeps <paramref name="value"/> by the predicate of <paramref name="property"/>'s def: only where the
    /// catalog row carries <see cref="PropertyRow.AcceptsLabel"/> (the def has <c>accepts</c>; another backend's def of the
    /// same key may not), and by the predicate ported for its key. A key without a ported predicate keeps nothing; the
    /// generator fails on such a key before it reaches the tables.
    /// </summary>
    internal static bool Keeps(PropertyRow property, JsonNode value) =>
        property.AcceptsLabel is not null && property.Key switch
        {
            "boundaries" => IsBoundaryFile(value),
            "mapStyle" => IsCustomMapStyle(value),
            _ => false,
        };

    /// <summary>
    /// <c>isBoundaryFile</c> (src/chart-types/boundaries.ts:63-80 of flint-chart 1.1.1): an object that gives either a URL
    /// (a non-blank string, or an object with a non-blank <c>coarse</c> or <c>medium</c> and nothing else stated for the
    /// other) or inline shapes (an object <c>values</c>), not both; <c>feature</c> and <c>disputed</c> strings, <c>ids</c>
    /// one of <c>BOUNDARY_IDS</c>, <c>key</c> a non-empty string and <c>attribution</c> a string or an object with a string
    /// <c>text</c>, each where present. A member that is <c>null</c> is present (it is not <c>undefined</c>); other members
    /// are allowed.
    /// </summary>
    internal static bool IsBoundaryFile(JsonNode? value)
    {
        if (value is not JsonObject file)
        {
            return false;
        }

        var url = Js.Get(file, "url", out var hasUrl);
        var urlOk = IsUrl(url, hasUrl) || (url is JsonObject details
            && (IsUrl(Js.Get(details, "coarse", out var hasCoarse), hasCoarse) || IsUrl(Js.Get(details, "medium", out var hasMedium), hasMedium))
            && UndefinedOrUrl(details, "coarse") && UndefinedOrUrl(details, "medium"));
        var valuesOk = Js.Get(file, "values", out _) is JsonObject;
        if (urlOk == valuesOk)
        {
            return false;
        }

        var feature = Js.Get(file, "feature", out var hasFeature);
        var ids = Js.Get(file, "ids", out var hasIds);
        var key = Js.Get(file, "key", out var hasKey);
        var disputed = Js.Get(file, "disputed", out var hasDisputed);
        var attribution = Js.Get(file, "attribution", out var hasAttribution);
        return (!hasFeature || IsString(feature))
            && (!hasIds || (IsString(ids) && BoundaryIds.Contains(ids!.GetValue<string>(), StringComparer.Ordinal)))
            && (!hasKey || (IsString(key) && key!.GetValue<string>().Length > 0))
            && (!hasDisputed || IsString(disputed))
            && (!hasAttribution || IsString(attribution) || (attribution is JsonObject credit && IsString(Js.Get(credit, "text", out _))));
    }

    /// <summary>
    /// <c>isCustomMapStyle</c> (src/vegalite/templates/map-style.ts:78-84 of flint-chart 1.1.1): an object with at least one
    /// member, each <c>base</c> naming one of <c>MAP_STYLES</c> or a colour of <c>CUSTOM_COLORS</c> set to a non-blank
    /// string.
    /// </summary>
    internal static bool IsCustomMapStyle(JsonNode? value) =>
        value is JsonObject style && style.Count > 0 && style.All(m => m.Key == "base"
            ? IsString(m.Value) && MapStyles.Contains(m.Value!.GetValue<string>(), StringComparer.Ordinal)
            : CustomColors.Contains(m.Key, StringComparer.Ordinal) && IsString(m.Value) && Js.Trim(m.Value!.GetValue<string>()).Length > 0);

    /// <summary><c>isUrl</c> (src/chart-types/boundaries.ts:63): a string that is not blank after <c>trim()</c>.</summary>
    private static bool IsUrl(JsonNode? value, bool present) => present && IsString(value) && Js.Trim(value!.GetValue<string>()).Length > 0;

    /// <summary><c>detail === undefined || isUrl(detail)</c> for a member of a URL object.</summary>
    private static bool UndefinedOrUrl(JsonObject details, string name) =>
        Js.Get(details, name, out var present) is var detail && (!present || IsUrl(detail, present));

    private static bool IsString(JsonNode? value) => Js.Kind(value) == JsonValueKind.String;
}
