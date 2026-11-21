namespace Graspea.Flint.CSharp;

/// <summary>A catalog value and the C# member that holds it, such as <c>"x"</c> and <c>Channel.X</c>.</summary>
internal sealed record ConstantName(string Wire, string Member);

/// <summary>How a template property's fluent method takes its value.</summary>
internal enum PropertyKind
{
    Bool,
    Double,
    Enum,
    Json,
}

/// <summary>The JSON kind of an enum member's wire value.</summary>
internal enum WireKind
{
    String,
    Number,
    Boolean,
}

/// <summary>An enum member and the JSON value it writes (the label for an array option).</summary>
internal sealed record EnumMemberName(WireKind Kind, string Text, string Member);

/// <summary>A template property: key, fluent method, value kind and, for an enum, its type and members.</summary>
internal sealed record PropertyName(string Key, string Method, PropertyKind Kind, string? EnumType, EnumMemberName[] Members);

/// <summary>A chart type: wire name, <c>ChartType</c> constant, <c>Chart</c> entry, facade channels and properties.</summary>
internal sealed record TemplateNames(string Name, string Constant, string Entry, ConstantName[] Channels, PropertyName[] Properties);

/// <summary>A registered semantic type, its <c>SemanticType</c> constant and its first <c>visEncodings</c> entry.</summary>
internal sealed record SemanticTypeName(string Wire, string Member, string Encoding);

/// <summary>A generated member of an options or theming class: wire name, C# property and declared type.</summary>
internal sealed record MemberName(string Wire, string Name, string Type);

/// <summary>A generated options or theming class, namespace-qualified, and its members.</summary>
internal sealed record ClassNames(string Name, MemberName[] Members);

/// <summary>An interaction preset: its <c>type</c>, the <c>InteractionEntry</c> factory and its namespace-qualified options class.</summary>
internal sealed record PresetNames(string Type, string Method, string Options);

/// <summary>A generated open literal union struct (<c>'a' | 'b' | string</c>), namespace-qualified, and its literals with the
/// static readonly field of each.</summary>
internal sealed record StringUnionNames(string Name, ConstantName[] Literals);

/// <summary>A generated discriminated union, namespace-qualified: the wire name of its discriminator, each literal with its
/// derived class (namespace-qualified), and the class that holds a literal this version does not know.</summary>
internal sealed record UnionNames(string Name, string Discriminator, ConstantName[] Members, string Unknown);

/// <summary>
/// The C# names of the catalog (design §12.7). The arrays are written by the csharp-names emitter
/// (<c>Generated/NameTables.g.cs</c>); the lookups are built from them on first use.
/// </summary>
internal static partial class NameTables
{
    private static Dictionary<string, TemplateNames>? _templates;
    private static Dictionary<string, string>? _channels;
    private static Dictionary<string, SemanticTypeName>? _semanticTypes;
    private static Dictionary<string, string>? _themePresets;
    private static Dictionary<string, string>? _encodingTypes;
    private static Dictionary<string, string>? _aggregates;
    private static Dictionary<string, string>? _sortOrders;
    private static Dictionary<string, string>? _polarities;
    private static Dictionary<string, string>? _percentScales;
    private static Dictionary<string, string>? _seriesCountAxes;
    private static Dictionary<string, ClassNames>? _classes;
    private static Dictionary<string, StringUnionNames>? _stringUnions;
    private static Dictionary<string, UnionNames>? _unions;

    public static TemplateNames? Template(string chartType) => Find(ref _templates, Templates, t => t.Name, chartType);

    public static string? Channel(string channel) => Find(ref _channels, Channels, channel);

    public static SemanticTypeName? SemanticType(string name) => Find(ref _semanticTypes, SemanticTypes, s => s.Wire, name);

    public static string? ThemePreset(string id) => Find(ref _themePresets, ThemePresets, id);

    public static string? EncodingType(string value) => Find(ref _encodingTypes, EncodingTypes, value);

    public static string? Aggregate(string value) => Find(ref _aggregates, Aggregates, value);

    public static string? SortOrder(string value) => Find(ref _sortOrders, SortOrders, value);

    public static string? Polarity(string value) => Find(ref _polarities, Polarities, value);

    public static string? PercentScale(string value) => Find(ref _percentScales, PercentScales, value);

    public static string? SeriesCountAxis(string value) => Find(ref _seriesCountAxes, SeriesCountAxes, value);

    /// <summary>An interaction preset by its <c>type</c>, or null.</summary>
    public static PresetNames? Preset(string type) => Array.Find(Presets, p => p.Type == type);

    /// <summary>A generated class by its simple name (<c>ThemeInk</c>) or namespace-qualified name.</summary>
    public static ClassNames? Class(string name)
    {
        var classes = _classes;
        if (classes is null)
        {
            classes = new Dictionary<string, ClassNames>(StringComparer.Ordinal);
            foreach (var c in Classes.Concat(UnionObjectForms))
            {
                classes[c.Name] = c;
                classes[SimpleName(c.Name)] = c;
            }

            _classes = classes;
        }

        return classes.TryGetValue(name, out var found) ? found : null;
    }

    /// <summary>A generated open literal union struct by its simple name (<c>PointInk</c>) or namespace-qualified name.</summary>
    public static StringUnionNames? StringUnion(string name) => ByName(ref _stringUnions, StringUnions, u => u.Name, name);

    /// <summary>A generated discriminated union by its simple name (<c>ChartUpdateOp</c>) or namespace-qualified name.</summary>
    public static UnionNames? Union(string name) => ByName(ref _unions, Unions, u => u.Name, name);

    /// <summary>The object forms of the hand-written unions and the hand-written classes they hold, written and checked as
    /// classes; their string, <c>false</c> and boolean forms, and the factories of the <c>UpdateTarget</c> selectors, are handled
    /// by type name in <c>ValueWriter</c> and <c>SpecShape</c>.</summary>
    internal static readonly ClassNames[] UnionObjectForms =
    [
        new("Graspea.Flint.Theming.RuleSpec",
        [
            new("color", "Color", "string"), new("dash", "Dash", "double[]"), new("layer", "Layer", "string"),
            new("presence", "Presence", "string"), new("style", "Style", "string"), new("weight", "Weight", "double"),
        ]),
        new("Graspea.Flint.Theming.MinorGridSpec",
        [
            new("color", "Color", "string"), new("dash", "Dash", "double[]"), new("divisions", "Divisions", "double"),
            new("layer", "Layer", "string"), new("on", "On", "string[]"), new("presence", "Presence", "string"),
            new("style", "Style", "string"), new("weight", "Weight", "double"),
        ]),
        new("Graspea.Flint.ChartChrome", [new("footer", "Footer", "bool"), new("header", "Header", "bool"), new("logos", "Logos", "bool")]),
        new("Graspea.Flint.ChartSource", [new("href", "Href", "string"), new("text", "Text", "string")]),
        new("Graspea.Flint.AssistedTargeting",
        [
            new("details", "Details", "Graspea.Flint.TargetDetails"), new("indicator", "Indicator", "bool"),
            new("maxDistance", "MaxDistance", "double"),
        ]),
        new("Graspea.Flint.TargetDetails", [new("fields", "Fields", "string[]"), new("maxRows", "MaxRows", "double")]),
        new("Graspea.Flint.SparseFill",
        [
            new("barCap", "BarCap", "double"), new("pointCap", "PointCap", "double"), new("share", "Share", "double"),
        ]),
        new("Graspea.Flint.TitleWrap",
        [
            new("floor", "Floor", "double"), new("minWidth", "MinWidth", "double"), new("overhang", "Overhang", "double"),
        ]),
        new("Graspea.Flint.InteractionDefaults", [new("legend", "Legend", "bool"), new("tooltip", "Tooltip", "bool")]),
        new("Graspea.Flint.UpdateTarget",
        [
            new("elements", "Elements", "Graspea.Flint.SemanticElement[]"), new("select", "Select", "Graspea.Flint.TargetSelector"),
            new("visual", "Visual", "Graspea.Flint.TargetVisual"),
        ]),
        new("Graspea.Flint.TargetVisual", [new("kind", "Kind", "string"), new("role", "Role", "string")]),
        new("Graspea.Flint.SemanticElement",
        [
            new("records", "Records", "Dictionary<string, JsonNode?>[]"), new("value", "Value", "Dictionary<string, JsonNode?>"),
        ]),
        new("Graspea.Flint.TargetSelector",
        [
            new("key", "Key", "Dictionary<string, JsonNode?>"), new("visual", "Visual", "Graspea.Flint.TargetVisual"),
            new("where", "Where", "Dictionary<string, Graspea.Flint.FilterValue>"),
        ]),
        new("Graspea.Flint.FilterValue", [new("in", "In", "JsonNode?[]"), new("range", "Range", "JsonNode?[]")]),
    ];

    public static string SimpleName(string name) => name.Substring(name.LastIndexOf('.') + 1);

    public static string Namespace(string name) => name.Substring(0, Math.Max(0, name.LastIndexOf('.')));

    private static string? Find(ref Dictionary<string, string>? cache, ConstantName[] source, string key)
    {
        cache ??= source.ToDictionary(c => c.Wire, c => c.Member, StringComparer.Ordinal);
        return cache.TryGetValue(key, out var found) ? found : null;
    }

    /// <summary>An entry by its namespace-qualified name or its simple name.</summary>
    private static T? ByName<T>(ref Dictionary<string, T>? cache, T[] source, Func<T, string> nameOf, string name)
        where T : class
    {
        var byName = cache;
        if (byName is null)
        {
            byName = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var entry in source)
            {
                byName[nameOf(entry)] = entry;
                byName[SimpleName(nameOf(entry))] = entry;
            }

            cache = byName;
        }

        return byName.TryGetValue(name, out var found) ? found : null;
    }

    private static T? Find<T>(ref Dictionary<string, T>? cache, T[] source, Func<T, string> keyOf, string key)
        where T : class
    {
        cache ??= source.ToDictionary(keyOf, StringComparer.Ordinal);
        return cache.TryGetValue(key, out var found) ? found : null;
    }
}
