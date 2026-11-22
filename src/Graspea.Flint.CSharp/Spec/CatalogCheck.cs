using System.Globalization;

namespace Graspea.Flint.CSharp;

/// <summary>The severity of a finding, as the validator's <c>IssueSeverity</c>.</summary>
internal enum SpecSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// A finding about a spec (design §12.6.7): the FLINT id, the validator code, the severity, the validator path, where the
/// offending node starts and the offset just after it, and the message, which ends with the code in parentheses.
/// </summary>
internal sealed record SpecIssue(string Id, string Code, SpecSeverity Severity, string Path, TextPosition Start, int End, string Message);

/// <summary>
/// The catalog checks of design §12.6.7: the validator rules that need no data, run as the validator runs them for a
/// template whose rows are not known (<c>backend == null</c>, every backend that has the template), with the validator's
/// severity per case. The rules mirror <c>ValidationRun</c> and <c>StaticSeries</c> of the library; a test compares the
/// two over every fixture.
/// </summary>
internal sealed class CatalogCheck
{
    private const double MaxCanvas = 4000;

    private static readonly string[] LayoutBackends = ["vegalite", "echarts", "chartjs", "plotly"];

    /// <summary>The members of JavaScript's <c>Object.prototype</c>, which upstream's plain-object lookups find.</summary>
    private static readonly HashSet<string> ObjectPrototypeKeys = new(StringComparer.Ordinal)
    {
        "constructor", "__proto__", "__defineGetter__", "__defineSetter__", "__lookupGetter__", "__lookupSetter__",
        "hasOwnProperty", "isPrototypeOf", "propertyIsEnumerable", "toLocaleString", "toString", "valueOf",
    };

    /// <summary><c>String.prototype.trim</c>: WhiteSpace and LineTerminator of ECMA-262.</summary>
    private static readonly char[] JsWhiteSpace =
    [
        '\t', '\v', '\f', ' ', '\u00A0', '\uFEFF', '\n', '\r', '\u2028', '\u2029',
        '\u1680', '\u2000', '\u2001', '\u2002', '\u2003', '\u2004', '\u2005', '\u2006', '\u2007', '\u2008', '\u2009', '\u200A', '\u202F', '\u205F', '\u3000',
    ];

    private static readonly string[] TopLevel = ["data", "semantic_types", "chart_spec", "theme_spec", "options", "field_display_names", "interaction_spec", "updates"];
    private static readonly string[] SpecLevel = ["chartType", "title", "subtitle", "label", "encodings", "baseSize", "canvasSize", "chartProperties", "source", "chrome"];

    private readonly SpecDocument _spec;
    private readonly List<SpecIssue> _issues = [];
    private BackendTemplateRow[] _targets = [];

    private CatalogCheck(SpecDocument spec)
    {
        _spec = spec;
    }

    /// <summary>The findings, in the validator's order; FLINT140 (unknown keys) last.</summary>
    public static List<SpecIssue> Run(SpecDocument spec, CancellationToken cancellationToken = default)
    {
        var check = new CatalogCheck(spec);
        check.RunRules(cancellationToken);
        return check._issues;
    }

    /// <summary>The FLINT id of a validator code.</summary>
    public static string IdOf(string code) => code switch
    {
        "unknown_chart_type" => "FLINT110",
        "unsupported_channel" => "FLINT111",
        "invalid_option_value" => "FLINT112",
        "unknown_semantic_type" => "FLINT113",
        "unknown_theme_preset" => "FLINT114",
        "retired_member" => "FLINT117",
        "unknown_chart_property" or "continuous_out_of_range" or "invalid_literal" or "semantic_type_without_field"
            or "canvas_too_large" or "required_channel_missing" or "negative_diverging_neutral" => "FLINT116",
        "unknown_key" => "FLINT140",
        _ => "FLINT115",
    };

    private void RunRules(CancellationToken cancellationToken)
    {
        var chartSpec = _spec.ChartSpec;
        if (chartSpec is not { IsObject: true })
        {
            return;
        }

        ResolveTemplate(chartSpec);
        cancellationToken.ThrowIfCancellationRequested();
        var encodings = chartSpec.Get("encodings");
        if (encodings is { IsNull: true })
        {
            Add("missing_encodings", SpecSeverity.Error, "chart_spec.encodings", encodings, "chart_spec.encodings is missing; every backend throws.");
        }
        else if (encodings is { IsObject: true })
        {
            EncodingChecks(encodings);
            if (_targets.Any(t => Array.IndexOf(LayoutBackends, t.Backend) >= 0))
            {
                StaticSeries(encodings);
            }
        }

        SemanticTypeChecks();
        ThemeChecks();
        PropertyChecks(chartSpec);
        LiteralChecks(encodings);
        AnnotationChecks();
        RequiredChannelChecks(encodings);
        CanvasChecks(chartSpec);
        AnnotationOptionChecks();
        UpdateChecks();
        UnknownKeys(chartSpec);
    }

    private string ChartTypeName => _spec.ChartType ?? string.Empty;

    private void ResolveTemplate(JsonItem chartSpec)
    {
        var name = _spec.ChartType;
        if (name is not null && Catalog.ChartType(name) is { } row)
        {
            _targets = row.Templates;
            return;
        }

        var node = chartSpec.Get("chartType") ?? chartSpec;
        Add("unknown_chart_type", SpecSeverity.Error, "chart_spec.chartType", node,
            $"\"{name}\" is not a chart type of the pinned flint-chart; every backend throws.");
    }

    private void EncodingChecks(JsonItem encodings)
    {
        foreach (var member in encodings.Members)
        {
            var channel = member.Key;
            var path = $"chart_spec.encodings.{channel}";
            if (member.Value.IsNull)
            {
                PerTarget("channel_null", path, member.Value, t => t.Backend is "excel" or "image-charts" ? SpecSeverity.Info : SpecSeverity.Error,
                    (severity, on) => severity == SpecSeverity.Error
                        ? $"The \"{channel}\" encoding is null; {string.Join(", ", on)} throw."
                        : $"The \"{channel}\" encoding is null; {string.Join(", ", on)} {(on.Count == 1 ? "ignores" : "ignore")} it.");
                continue;
            }

            if (_targets.Length > 0 && !_targets.Any(t => Array.IndexOf(t.Channels, channel) >= 0 || ImageChartsReads(t, encodings, channel) || ExcelReads(t, encodings, channel) || LayoutReads(t, encodings, channel)))
            {
                var retiredOn = _targets.Where(t => Retired(t, $"channels[{channel}]") is not null).ToList();
                if (retiredOn.Count > 0)
                {
                    AddAtKey("retired_member", SpecSeverity.Warning, path, member,
                        $"flint-chart {Retired(retiredOn[0], $"channels[{channel}]")} removed the \"{channel}\" channel from the {string.Join(", ", retiredOn.Select(t => t.Backend))} template of \"{ChartTypeName}\"; it is ignored there.");
                }
                else
                {
                    AddAtKey("unsupported_channel", SpecSeverity.Warning, path, member,
                        $"No backend's template of \"{ChartTypeName}\" declares \"{channel}\"; {(_targets.All(t => t.Backend is "excel" or "image-charts") ? "it is ignored" : "no backend draws it, though it may still affect the output")}.");
                }
            }
        }
    }

    /// <summary>The templates of the Excel shared path (src/excel/assemble.ts:265-311 of flint-chart 1.1.1), as the validator's <c>Excel</c>.</summary>
    private static readonly string[] ExcelSharedPath =
        ["Area Chart", "Bar Chart", "Grouped Bar Chart", "Stacked Bar Chart", "Line Chart", "Scatter Plot", "Connected Scatter Plot", "Pie Chart", "Donut Chart"];

    /// <summary>A channel the Excel template leaves undeclared but the assembler reads, as the validator's
    /// <c>Excel.ReadsUndeclared</c> without rows (a template): <c>color</c> with a field of a Grouped Bar Chart with no
    /// <c>group</c> bound; <c>group</c> off Pie and Donut with a field or beside a <c>color</c> with a field and no aggregate; <c>strokeDash</c> with a field
    /// (on a Scatter or Connected Scatter Plot only without a series field); <c>size</c> with a field on a Connected
    /// Scatter Plot and <c>order</c> with a field on a Scatter Plot, both on a Line Chart unless the spec makes <c>x</c> or
    /// <c>y</c> not quantitative; <c>x</c> and <c>y</c> of a Pie or Donut Chart as the slice and value fallbacks; <c>y</c>
    /// of a Histogram without an <c>x</c> field. Bound is any non-null value (a Grouped Bar Chart <c>color</c> also needs a field); a field is a non-empty one of a single
    /// encoding.</summary>
    private bool ExcelReads(BackendTemplateRow t, JsonItem encodings, string channel)
    {
        if (t.Backend != "excel")
        {
            return false;
        }

        var name = ChartTypeName;
        bool Bound(string ch) => encodings.Get(ch) is { IsNull: false };
        string? Field(string ch) => encodings.Get(ch) is { IsNull: false, IsArray: false } v && FieldOf(v) is { Length: > 0 } f ? f : null;
        if (Array.IndexOf(ExcelSharedPath, name) < 0)
        {
            return name == "Histogram" && channel == "y" && Field("x") is null;
        }

        string? Agg(string ch) => encodings.Get(ch) is { IsObject: true } o && o.Get("aggregate") is { IsString: true } a && a.Value is { Length: > 0 } s ? s : null;
        var pie = name is "Pie Chart" or "Donut Chart";
        bool NumericLine() => name == "Line Chart" && !NotQuantitativeBySpec(encodings, "x") && !NotQuantitativeBySpec(encodings, "y");
        return channel switch
        {
            "color" => name == "Grouped Bar Chart" && !Bound("group") && Field("color") is not null,
            "group" => !pie && (Field("group") is not null || (Field("color") is not null && Agg("color") is null)),
            "strokeDash" => Field("strokeDash") is not null
                && (name is not ("Scatter Plot" or "Connected Scatter Plot") || (Bound("group") ? Field("group") : Field("color")) is null),
            "size" => Field("size") is not null && (name == "Connected Scatter Plot" || NumericLine()),
            "order" => Field("order") is not null && (name == "Scatter Plot" || NumericLine()),
            "x" => pie && !Bound("color"),
            "y" => pie && !Bound("size") && !Bound("theta"),
            _ => false,
        };
    }

    /// <summary>Whether the spec alone makes an axis not quantitative, as the validator's <c>FieldTypes.SpecDecided</c> and
    /// <c>Of</c> without rows: unbound, <c>null</c> or a static series; no field and no <c>count</c>; or an explicit
    /// <c>type</c> other than <c>quantitative</c> and no <c>aggregate</c> (an aggregate makes it quantitative).</summary>
    private static bool NotQuantitativeBySpec(JsonItem encodings, string axis)
    {
        if (encodings.Get(axis) is not { IsNull: false, IsArray: false } e)
        {
            return true;
        }

        string? Text(string key) => e.IsObject && e.Get(key) is { IsString: true } v && v.Value is { Length: > 0 } s ? s : null;
        if (FieldOf(e) is not { Length: > 0 })
        {
            return Text("aggregate") != "count";
        }

        return Text("aggregate") is null && Text("type") is { } type && type != "quantitative";
    }

    /// <summary>A channel a layout template leaves undeclared but its assembler draws from the field, as the validator's
    /// <c>LayoutReads.ReadsUndeclared</c>: <c>column</c> and <c>row</c> with a field and no <c>count</c> aggregate facet the ECharts Pyramid Chart and
    /// Ranged Dot Plot, the Plotly Funnel Chart, Pyramid Chart, Ranged Dot Plot and Rose Chart, and the Vega-Lite Ranged
    /// Dot Plot (not the ECharts Calendar Heatmap, Funnel Chart, Network Graph or Sankey Diagram, whose faceted option does
    /// not render or draws every panel's chart in the same place); <c>group</c> with a field and no <c>count</c> aggregate is the split of an ECharts or
    /// Plotly Pyramid Chart while <c>x</c> and <c>y</c> resolve and <c>color</c> does not. A field is a non-empty one of a
    /// single encoding; a channel resolves with a field or a <c>count</c> aggregate, but a <c>count</c> aggregate on the
    /// channel itself makes the facet or split by the constant <c>_count</c>, which stays reported.</summary>
    private bool LayoutReads(BackendTemplateRow t, JsonItem encodings, string channel)
    {
        string? Field(string ch) => encodings.Get(ch) is { IsNull: false, IsArray: false } v && FieldOf(v) is { Length: > 0 } f ? f : null;
        bool Resolves(string ch) => Field(ch) is not null
            || (encodings.Get(ch) is { IsObject: true } o && o.Get("aggregate") is { IsString: true } a && a.Value == "count");
        bool Own(string ch) => Field(ch) is not null
            && !(encodings.Get(ch) is { IsObject: true } o && o.Get("aggregate") is { IsString: true } a && a.Value == "count");
        return channel switch
        {
            "column" or "row" => Own(channel) && (t.Backend, ChartTypeName) is ("vegalite", "Ranged Dot Plot")
                or ("echarts", "Pyramid Chart" or "Ranged Dot Plot")
                or ("plotly", "Funnel Chart" or "Pyramid Chart" or "Ranged Dot Plot" or "Rose Chart"),
            "group" => ChartTypeName == "Pyramid Chart" && t.Backend is ("echarts" or "plotly")
                && Own("group") && Resolves("x") && Resolves("y") && !Resolves("color"),
            _ => false,
        };
    }

    /// <summary>A channel the Image-Charts template leaves undeclared but the assembler reads, as the validator's
    /// <c>ImageCharts.ReadsUndeclared</c> (the core cannot see it): <c>group</c> with a field on every chart type but Pie
    /// and Donut, and on those two <c>x</c> when <c>color</c> has no field and <c>y</c> when neither <c>size</c> nor
    /// <c>theta</c> has one; only a missing field falls through (absent, <c>null</c>, an array, no <c>field</c>), an empty
    /// one does not.</summary>
    private bool ImageChartsReads(BackendTemplateRow t, JsonItem encodings, string channel)
    {
        if (t.Backend != "image-charts")
        {
            return false;
        }

        string? Raw(string ch) => encodings.Get(ch) is { } value ? FieldOf(value) : null;
        return ChartTypeName is "Pie Chart" or "Donut Chart"
            ? (channel == "x" && Raw("color") is null) || (channel == "y" && Raw("size") is null && Raw("theta") is null)
            : channel == "group" && Raw("group") is { Length: > 0 };
    }

    /// <summary>The throw conditions of upstream's <c>normalizeStaticSeries</c> that need no rows, in its order; the first
    /// that holds is reported.</summary>
    private void StaticSeries(JsonItem encodings)
    {
        var series = encodings.Members.Where(m => m.Value.IsArray).ToList();
        if (series.Count == 0)
        {
            return;
        }

        if (series.Count > 1)
        {
            SeriesIssue("series_multiple_channels", series[1], $"Only one channel may use a static series (array encoding); found {string.Join(", ", series.Select(s => s.Key))}.");
            return;
        }

        var member = series[0];
        var channel = member.Key;
        if (channel is not ("x" or "y"))
        {
            SeriesIssue("series_channel_not_xy", member, $"A static series is only allowed on x or y, not \"{channel}\".");
            return;
        }

        var entries = member.Value.Items;
        if (entries.Count < 2)
        {
            SeriesIssue("series_too_few", member, $"A static series needs at least 2 fields; \"{channel}\" has {entries.Count}.");
            return;
        }

        var fields = entries.Select(FieldOf).ToList();
        if (fields.Any(string.IsNullOrEmpty))
        {
            SeriesIssue("series_field_missing", member, "Each static series entry must have a field.");
            return;
        }

        if (fields.Distinct(StringComparer.Ordinal).Count() != fields.Count)
        {
            SeriesIssue("series_duplicate_field", member, "A static series contains duplicate fields; each field must be unique.");
            return;
        }

        foreach (var entry in entries)
        {
            if (entry.IsObject && entry.Get("type") is { IsString: true, Value: "nominal" or "ordinal" } type)
            {
                SeriesIssue("series_nominal_entry", member, $"Static series field \"{FieldOf(entry)}\" has type \"{type.Value}\"; only quantitative or temporal fields are allowed.");
                return;
            }
        }

        var color = encodings.Members.FirstOrDefault(m => m.Key == "color");
        if (color is not null && !color.Value.IsArray && FieldOf(color.Value) is { Length: > 0 } colorField)
        {
            SeriesIssue("series_color_bound", color, $"A static series on \"{channel}\" uses the color channel, which is bound to \"{colorField}\".");
        }
    }

    private void SeriesIssue(string code, JsonMember channel, string message)
    {
        var on = _targets.Where(t => Array.IndexOf(LayoutBackends, t.Backend) >= 0).Select(t => t.Backend);
        AddAtKey(code, SpecSeverity.Error, $"chart_spec.encodings.{channel.Key}", channel, $"{message} Throws on {string.Join(", ", on)}.");
    }

    /// <summary>The field of an encoding entry: the string shorthand or the object's <c>field</c>.</summary>
    private static string? FieldOf(JsonItem entry) =>
        entry.IsString ? entry.Value : entry.IsObject && entry.Get("field") is { IsString: true } field ? field.Value : null;

    private void SemanticTypeChecks()
    {
        if (_spec.SemanticTypes is not { } types)
        {
            return;
        }

        var bound = new HashSet<string>(_spec.EncodingFields().Select(f => f.Field), StringComparer.Ordinal);
        foreach (var member in types.Members)
        {
            var name = member.Value switch
            {
                { IsString: true } s => s,
                { IsObject: true } o when o.Get("semanticType") is { IsString: true } s => s,
                _ => null,
            };
            if (name is null)
            {
                continue;
            }

            var path = $"semantic_types.{member.Key}";
            if (ObjectPrototypeKeys.Contains(name.Value!) && bound.Contains(member.Key))
            {
                Add("reserved_semantic_type", SpecSeverity.Error, path, name,
                    $"\"{name.Value}\" is a member of JavaScript's Object.prototype; upstream's registry lookup counts it as registered and finds no entry. Every target backend throws.");
                continue;
            }

            if (Catalog.RetiredIn($"catalog.semanticTypes[{name.Value}]") is { } version)
            {
                Add("retired_member", SpecSeverity.Warning, path, name,
                    $"flint-chart {version} removed the semantic type \"{name.Value}\"; the type is inferred from the data, as without an annotation.");
            }
            else if (NameTables.SemanticType(name.Value!) is null)
            {
                Add("unknown_semantic_type", SpecSeverity.Warning, path, name,
                    $"\"{name.Value}\" is not a registered semantic type of the pinned flint-chart; the type is inferred from the data, as without an annotation.");
            }
        }
    }

    private void ThemeChecks()
    {
        var theme = _spec.Root.Get("theme_spec");
        if (theme is null || theme.IsNull || _targets.Length == 0)
        {
            return;
        }

        var (id, path) = theme switch
        {
            { IsString: true } => (theme, "theme_spec"),
            { IsObject: true } when theme.Get("extends") is { IsString: true } extends => (extends, "theme_spec.extends"),
            _ => (null, string.Empty),
        };
        if (id is null || NameTables.ThemePreset(id.Value!) is not null || ObjectPrototypeKeys.Contains(id.Value!))
        {
            return;
        }

        PerTarget("unknown_theme_preset", path, id, t => Array.IndexOf(Catalog.BackendsApplyingTheme, t.Backend) >= 0 ? SpecSeverity.Error : SpecSeverity.Info,
            (severity, on) => severity == SpecSeverity.Error
                ? $"\"{id.Value}\" is not a theme preset of the pinned flint-chart; {string.Join(", ", on)} throw."
                : $"\"{id.Value}\" is not a theme preset; {string.Join(", ", on)} do not apply themes.");
    }

    private void PropertyChecks(JsonItem chartSpec)
    {
        if (chartSpec.Get("chartProperties") is not { IsObject: true } properties || _targets.Length == 0)
        {
            return;
        }

        foreach (var member in properties.Members)
        {
            var key = member.Key;
            var value = member.Value;
            if (value.IsNull)
            {
                continue;
            }

            var path = $"chart_spec.chartProperties.{key}";
            var invalid = _targets.Where(t => t.Property(key) is { Kind: CatalogPropertyKind.Discrete } p && !Accepts(p, value)).ToList();
            // The ledger keys an option by its JSON text (generator/src/diff.ts).
            var option = $"properties[{key}].options[={JsonText.Compact(value)}]";
            var optionRetiredOn = invalid.Where(t => Retired(t, option) is not null).ToList();
            if (optionRetiredOn.Count > 0)
            {
                Add("retired_member", SpecSeverity.Warning, path, value,
                    $"flint-chart {Retired(optionRetiredOn[0], option)} removed the option {JsonText.Compact(value)} of \"{key}\" on {string.Join(", ", optionRetiredOn.Select(t => t.Backend))}; upstream drops it (invalid-option-value) and uses the default.");
            }

            var invalidRows = invalid.Except(optionRetiredOn).ToList();
            if (invalidRows.Count > 0)
            {
                // flint-chart 1.1.0+ appends ", or <acceptsLabel>" for a def with a label (normalize-properties.ts:97).
                var accepts = invalidRows.Select(t => t.Property(key)!.AcceptsLabel).Where(l => !string.IsNullOrEmpty(l)).Distinct(StringComparer.Ordinal);
                Add("invalid_option_value", SpecSeverity.Warning, path, value,
                    $"{JsonText.Compact(value)} is not an option of \"{key}\" on {string.Join(", ", invalidRows.Select(t => t.Backend))}{string.Concat(accepts.Select(l => $", or {l}"))}; upstream drops it (invalid-option-value) and uses the default.");
            }

            var undeclaredOn = _targets.Where(t => t.Property(key) is null).Select(t => t.Backend).ToList();
            if (Array.IndexOf(Catalog.HostStateKeys, key) < 0 && undeclaredOn.Count == _targets.Length)
            {
                var retiredOn = _targets.Where(t => Retired(t, $"properties[{key}]") is not null).ToList();
                if (retiredOn.Count > 0)
                {
                    Add("retired_member", SpecSeverity.Warning, path, value,
                        $"flint-chart {Retired(retiredOn[0], $"properties[{key}]")} removed \"{key}\" from the {string.Join(", ", retiredOn.Select(t => t.Backend))} template of \"{ChartTypeName}\"; it is ignored there.");
                }
                else
                {
                    Add("unknown_chart_property", SpecSeverity.Info, path, value,
                        $"The {string.Join(", ", undeclaredOn)} template of \"{ChartTypeName}\" does not declare \"{key}\"; it is passed through and may have no effect.");
                }
            }

            if (value.IsNumber && Number(value.Value!) is { } number)
            {
                var outOn = _targets.Where(t => t.Property(key) is { Kind: CatalogPropertyKind.Continuous } p && (number < p.Min || number > p.Max)).ToList();
                if (outOn.Count > 0)
                {
                    var p = outOn[0].Property(key)!;
                    Add("continuous_out_of_range", SpecSeverity.Info, path, value,
                        $"{value.Value} is outside the declared range {Format(p.Min)}..{Format(p.Max)} of \"{key}\" on {string.Join(", ", outOn.Select(t => t.Backend))}; upstream passes it through unchecked.");
                }
            }
        }
    }

    /// <summary>The version that retired a member of the template on a backend (<c>generator/retired.json</c>), or null.</summary>
    private string? Retired(BackendTemplateRow t, string member) => Catalog.RetiredIn($"catalog.chartTypes[{ChartTypeName}].backends.{t.Backend}.{member}");

    /// <summary><c>normalizeChartProperties</c> (src/core/normalize-properties.ts:55-84 of flint-chart 1.1.1): a value equal to
    /// an option value under <c>sameOptionValue</c> (===, or for two arrays the same length with every entry ===), a value the
    /// def's <c>accepts</c> predicate holds for (flint-chart 1.1.0+, <see cref="AcceptsKeeps"/>), or a string equal to an
    /// option label ignoring case and surrounding whitespace.</summary>
    private static bool Accepts(CatalogPropertyRow property, JsonItem value)
    {
        foreach (var option in property.Options ?? [])
        {
            if (option is not null && SameOptionValue(JsonReader.Parse(option), value))
            {
                return true;
            }
        }

        if (AcceptsKeeps(property, value))
        {
            return true;
        }

        return value.IsString && property.Labels is { } labels
            && labels.Any(l => string.Equals(l.ToLowerInvariant(), value.Value!.Trim(JsWhiteSpace).ToLowerInvariant(), StringComparison.Ordinal));
    }

    /// <summary>The <c>accepts</c> predicates, as the validator's <c>Accepts.Keeps</c>
    /// (src/Graspea.Flint/Validation/Rules/Accepts.cs): only where the row carries the accepts label, by the predicate
    /// ported for its key.</summary>
    private static bool AcceptsKeeps(CatalogPropertyRow property, JsonItem value) =>
        property.AcceptsLabel is not null && property.Key switch
        {
            "boundaries" => IsBoundaryFile(value),
            "mapStyle" => IsCustomMapStyle(value),
            _ => false,
        };

    /// <summary><c>BOUNDARY_IDS</c> (src/chart-types/boundaries.ts:28).</summary>
    private static readonly string[] BoundaryIds = ["iso-numeric", "iso3", "iso2", "fips", "as-is"];

    /// <summary><c>MAP_STYLES</c> (src/vegalite/templates/map-style.ts:55).</summary>
    private static readonly string[] MapStyles = ["auto", "light", "muted", "atlas", "dark", "minimal"];

    /// <summary><c>CUSTOM_COLORS</c> (src/vegalite/templates/map-style.ts:74).</summary>
    private static readonly string[] CustomColors = ["sea", "land", "coast", "border", "noData", "graticule", "disputed"];

    /// <summary><c>isBoundaryFile</c> (src/chart-types/boundaries.ts:63-80 of flint-chart 1.1.1), as the validator's
    /// <c>Accepts.IsBoundaryFile</c>: a URL (a non-blank string, or an object with a non-blank <c>coarse</c> or
    /// <c>medium</c> and nothing else stated for the other) or an object <c>values</c>, not both, with <c>feature</c>,
    /// <c>ids</c>, <c>key</c>, <c>disputed</c> and <c>attribution</c> well-formed where present (<c>null</c> is
    /// present).</summary>
    private static bool IsBoundaryFile(JsonItem value)
    {
        if (!value.IsObject)
        {
            return false;
        }

        var url = value.Get("url");
        var urlOk = IsUrl(url) || (url is { IsObject: true }
            && (IsUrl(url.Get("coarse")) || IsUrl(url.Get("medium")))
            && (url.Get("coarse") is not { } coarse || IsUrl(coarse)) && (url.Get("medium") is not { } medium || IsUrl(medium)));
        var valuesOk = value.Get("values") is { IsObject: true };
        if (urlOk == valuesOk)
        {
            return false;
        }

        return (value.Get("feature") is not { } feature || feature.IsString)
            && (value.Get("ids") is not { } ids || (ids.IsString && Array.IndexOf(BoundaryIds, ids.Value) >= 0))
            && (value.Get("key") is not { } key || (key.IsString && key.Value!.Length > 0))
            && (value.Get("disputed") is not { } disputed || disputed.IsString)
            && (value.Get("attribution") is not { } attribution || attribution.IsString || (attribution.IsObject && attribution.Get("text") is { IsString: true }));
    }

    /// <summary><c>isCustomMapStyle</c> (src/vegalite/templates/map-style.ts:78-84 of flint-chart 1.1.1), as the
    /// validator's <c>Accepts.IsCustomMapStyle</c>: an object with at least one member, each <c>base</c> naming one of
    /// <c>MAP_STYLES</c> or a colour of <c>CUSTOM_COLORS</c> set to a non-blank string.</summary>
    private static bool IsCustomMapStyle(JsonItem value) =>
        value.IsObject && value.Members.Count > 0 && value.Members.All(m => m.Key == "base"
            ? m.Value.IsString && Array.IndexOf(MapStyles, m.Value.Value) >= 0
            : Array.IndexOf(CustomColors, m.Key) >= 0 && m.Value.IsString && m.Value.Value!.Trim(JsWhiteSpace).Length > 0);

    /// <summary><c>isUrl</c> (src/chart-types/boundaries.ts:63): a string that is not blank after <c>trim()</c>; null for
    /// an absent member (<c>undefined</c>).</summary>
    private static bool IsUrl(JsonItem? value) => value is { IsString: true } && value.Value!.Trim(JsWhiteSpace).Length > 0;

    private static bool SameOptionValue(JsonItem option, JsonItem value) =>
        option.IsArray && value.IsArray
            ? option.Items.Count == value.Items.Count && option.Items.Select((entry, i) => StrictEquals(entry, value.Items[i])).All(e => e)
            : StrictEquals(option, value);

    /// <summary>JavaScript <c>===</c> on JSON values: equal strings, equal numbers, the same boolean, both null; an object or an
    /// array parsed from JSON is never === to another.</summary>
    private static bool StrictEquals(JsonItem a, JsonItem b) => (a.Kind, b.Kind) switch
    {
        (JsonItemKind.String, JsonItemKind.String) => a.Value == b.Value,
        (JsonItemKind.Number, JsonItemKind.Number) => Number(a.Value!) == Number(b.Value!),
        (JsonItemKind.Object, _) or (JsonItemKind.Array, _) => false,
        _ => a.Kind == b.Kind,
    };

    private void LiteralChecks(JsonItem? encodings)
    {
        foreach (var member in encodings is { IsObject: true } ? encodings.Members : [])
        {
            var value = member.Value;
            var entries = value.IsArray ? value.Items : value.IsNull ? [] : [value];
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (!entry.IsObject)
                {
                    continue;
                }

                var path = value.IsArray ? $"chart_spec.encodings.{member.Key}[{i}]" : $"chart_spec.encodings.{member.Key}";
                Literal(entry.Get("type"), NameTables.EncodingType, $"{path}.type", NameTables.EncodingTypes);
                Literal(entry.Get("aggregate"), NameTables.Aggregate, $"{path}.aggregate", NameTables.Aggregates);
                Literal(entry.Get("sortOrder"), NameTables.SortOrder, $"{path}.sortOrder", NameTables.SortOrders);
            }
        }

        Literal(_spec.Root.Get("options")?.Get("continuousMarkCrossSection")?.Get("seriesCountAxis"), NameTables.SeriesCountAxis,
            "options.continuousMarkCrossSection.seriesCountAxis", NameTables.SeriesCountAxes);
        Literal(_spec.Root.Get("theme_spec")?.Get("compileDefaults")?.Get("continuousMarkCrossSection")?.Get("seriesCountAxis"), NameTables.SeriesCountAxis,
            "theme_spec.compileDefaults.continuousMarkCrossSection.seriesCountAxis", NameTables.SeriesCountAxes);
    }

    /// <summary>The validator's <c>AnnotationChecks</c>: a negative <c>divergingNeutral</c>, which flint-chart reads as a
    /// break, and a <c>polarity</c> or <c>percentScale</c> outside the pinned union, in the annotation's declaration order.</summary>
    private void AnnotationChecks()
    {
        foreach (var member in _spec.SemanticTypes is { IsObject: true } types ? types.Members : [])
        {
            if (!member.Value.IsObject)
            {
                continue;
            }

            var path = $"semantic_types.{member.Key}";
            if (member.Value.Get("divergingNeutral") is { IsNumber: true } neutral && Number(neutral.Value!) is { } width && width < 0)
            {
                Add("negative_diverging_neutral", SpecSeverity.Info, $"{path}.divergingNeutral", neutral,
                    $"divergingNeutral {Format(width)} is negative; flint-chart reads a number of at least 0 as the half-width of the neutral class and any other value as a break, as false.");
            }

            Literal(member.Value.Get("polarity"), NameTables.Polarity, $"{path}.polarity", NameTables.Polarities);
            Literal(member.Value.Get("percentScale"), NameTables.PercentScale, $"{path}.percentScale", NameTables.PercentScales);
        }
    }

    private void Literal(JsonItem? value, Func<string, string?> lookup, string path, ConstantName[] literals)
    {
        if (value is { IsString: true } && lookup(value.Value!) is null)
        {
            Add("invalid_literal", SpecSeverity.Info, path, value,
                $"\"{value.Value}\" is not one of {string.Join(", ", literals.Select(l => l.Wire).OrderBy(l => l, StringComparer.Ordinal))} in the pinned flint-chart; it is written as given.");
        }
    }

    /// <summary>The flint-chart MCP server requires x and y when the template declares both, and metric and value on a KPI
    /// Card; the assemblers accept a document without them.</summary>
    private void RequiredChannelChecks(JsonItem? encodings)
    {
        if (encodings is not { IsObject: true } || encodings.Members.Count == 0 || _targets.Length == 0)
        {
            return;
        }

        var required = _targets
            .SelectMany(t => Array.IndexOf(t.Channels, "x") >= 0 && Array.IndexOf(t.Channels, "y") >= 0 ? ["x", "y"]
                : ChartTypeName == "KPI Card" ? ["metric", "value"] : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal);
        foreach (var channel in required)
        {
            var member = encodings.Members.FirstOrDefault(m => m.Key == channel);
            if (member is null || !IsBound(member.Value))
            {
                var path = $"chart_spec.encodings.{channel}";
                var message = $"\"{channel}\" is not bound; the flint-chart MCP server rejects a \"{ChartTypeName}\" without it, the assemblers accept it.";
                if (member is null)
                {
                    Add("required_channel_missing", SpecSeverity.Info, path, encodings, message);
                }
                else
                {
                    AddAtKey("required_channel_missing", SpecSeverity.Info, path, member, message);
                }
            }
        }
    }

    private static bool IsBound(JsonItem value)
    {
        var entries = value.IsArray ? value.Items : value.IsNull ? [] : [value];
        return entries.Any(e => FieldOf(e) is { } f && f.Trim(JsWhiteSpace).Length > 0
            || e.IsObject && e.Get("aggregate") is { IsString: true, Value: "count" });
    }

    private void CanvasChecks(JsonItem chartSpec)
    {
        foreach (var name in new[] { "baseSize", "canvasSize" })
        {
            if (chartSpec.Get(name) is not { IsObject: true } size)
            {
                continue;
            }

            var width = size.Get("width") is { IsNumber: true } w ? Number(w.Value!) : null;
            var height = size.Get("height") is { IsNumber: true } h ? Number(h.Value!) : null;
            if (width > MaxCanvas || height > MaxCanvas)
            {
                Add("canvas_too_large", SpecSeverity.Info, $"chart_spec.{name}", size,
                    $"{Format(width ?? 0)} x {Format(height ?? 0)} exceeds {MaxCanvas} px, the limit of the flint-chart MCP server; the assemblers accept it.");
            }
        }
    }

    /// <summary>A null entry of <c>updates</c> throws on the four layout backends, which read the id of every update; Excel
    /// and Image-Charts never read them (the validator's <c>UpdateChecks</c>).</summary>
    private void UpdateChecks()
    {
        var on = _targets.Where(t => Array.IndexOf(LayoutBackends, t.Backend) >= 0).Select(t => t.Backend).ToList();
        if (_spec.Root.Get("updates") is not { IsArray: true } updates || on.Count == 0)
        {
            return;
        }

        for (var i = 0; i < updates.Items.Count; i++)
        {
            if (updates.Items[i].IsNull)
            {
                Add("update_null", SpecSeverity.Error, $"updates[{i}]", updates.Items[i],
                    $"updates[{i}] is null; flint-chart reads the id of every update to note that a static output draws the chart without them. Throws on {string.Join(", ", on)}.");
            }
        }
    }

    private static readonly string[] AnnotationOptionMembers = ["notes", "regions", "enclosures"];

    /// <summary>The validator's <c>AnnotationOptionChecks</c> and <c>AnnotationOptions.Evaluate</c>: an entry of
    /// <c>notes</c>, <c>regions</c> or <c>enclosures</c> that the Vega-Lite compile throws on reading (src/vegalite/note-room.ts
    /// and src/core/target-selector.ts of flint-chart 1.1.1), of <c>options</c> or, for a member the options leave out,
    /// of <c>theme_spec.compileDefaults</c>; the first fault of each entry in upstream's order. An Error where the template
    /// is on Vega-Lite, an Info elsewhere. The validator reads the JSON the typed model writes, which leaves out a member
    /// that is <c>null</c> (an option, a compile default, a region's <c>placement</c>), so these read as absent here.</summary>
    private void AnnotationOptionChecks()
    {
        if (_targets.Length == 0)
        {
            return;
        }

        var options = _spec.Root.Get("options") is { IsObject: true } o ? o : null;
        var house = _spec.Root.Get("theme_spec") is { IsObject: true } theme && theme.Get("compileDefaults") is { IsObject: true } d ? d : null;
        foreach (var member in AnnotationOptionMembers)
        {
            var (root, value) = Written(options, member) is { } stated ? ("options", stated)
                : Written(house, member) is { } defaulted ? ("theme_spec.compileDefaults", defaulted)
                : (null, null);
            if (value is not { IsArray: true } entries)
            {
                continue;
            }

            var option = $"{root}.{member}";
            for (var i = 0; i < entries.Items.Count; i++)
            {
                var at = $"{option}[{i}]";
                var entry = entries.Items[i];
                var fault = member switch
                {
                    "notes" => NoteFault(at, entry),
                    "regions" => RegionFault(at, entry),
                    _ => EnclosureFault(at, entry),
                };
                if (fault is { } f)
                {
                    PerTarget("invalid_annotation_option", f.Path, f.Node, t => t.Backend == "vegalite" ? SpecSeverity.Error : SpecSeverity.Info,
                        (severity, on) => severity == SpecSeverity.Error
                            ? $"{f.Fact}; {f.Reason}. Throws on {string.Join(", ", on)}."
                            : $"{f.Fact}; {string.Join(", ", on)} {(on.Count == 1 ? "does" : "do")} not read {option}, but Vega-Lite throws on it.");
                }
            }
        }
    }

    /// <summary>A member as the typed model writes it: absent when it is <c>null</c>.</summary>
    private static JsonItem? Written(JsonItem? owner, string key) => owner?.Get(key) is { IsNull: false } value ? value : null;

    private static (string Path, JsonItem Node, string Fact, string Reason)? NoteFault(string at, JsonItem note)
    {
        if (note.IsNull)
        {
            return (at, note, $"{at} is null", "Vega-Lite reads the key of every note");
        }

        if (Written(note, "key") is not { } key)
        {
            return ($"{at}.key", note, $"{at} has no key", "Vega-Lite reads the fields of every note's key");
        }

        // A key without fields names no mark, and the text is never read.
        return EntryCount(key) > 0 && Written(note, "text") is null
            ? ($"{at}.text", note, $"{at} has a key but no text", "Vega-Lite measures the text of a note whose key names a mark")
            : null;
    }

    private static (string Path, JsonItem Node, string Fact, string Reason)? RegionFault(string at, JsonItem region)
    {
        if (region.IsNull)
        {
            return (at, region, $"{at} is null", "Vega-Lite reads the text of every region");
        }

        var x = Written(region, "x");
        var placement = Written(region, "placement");
        var captionedAtTop = Truthy(Written(region, "text")) && TruthyLength(x) && !TruthyLength(Written(region, "y"))
            && (placement is null || placement is { IsString: true, Value: "top" });
        return captionedAtTop && !x!.IsArray
            ? ($"{at}.x", x, $"{at}.x is {JsonText.Compact(x)}, not an array", "Vega-Lite maps the stretch of a region captioned at the top")
            : null;
    }

    private static (string Path, JsonItem Node, string Fact, string Reason)? EnclosureFault(string at, JsonItem enclosure)
    {
        if (enclosure.IsNull)
        {
            return (at, enclosure, $"{at} is null", "Vega-Lite reads the targets of every enclosure");
        }

        if (Written(enclosure, "targets") is not { } targets)
        {
            return null;
        }

        if (!targets.IsArray)
        {
            return ($"{at}.targets", targets, $"{at}.targets is {JsonText.Compact(targets)}, not an array", "Vega-Lite reads the targets of every enclosure as an array");
        }

        for (var j = 0; j < targets.Items.Count; j++)
        {
            if (TargetFault($"{at}.targets[{j}]", targets.Items[j]) is { } fault)
            {
                return fault;
            }
        }

        return null;
    }

    private static (string Path, JsonItem Node, string Fact, string Reason)? TargetFault(string at, JsonItem target)
    {
        if (!Truthy(target))
        {
            return null;
        }

        if (!IsObjectOrArray(target))
        {
            return (at, target, $"{at} is {JsonText.Compact(target)}", "Vega-Lite looks for a select in every target but null, false, 0 and \"\"");
        }

        if (target.Get("select") is not { } select)
        {
            return null;
        }

        var sat = $"{at}.select";
        const string selector = "Vega-Lite reads the where or the key of a target's select";
        if (!IsObjectOrArray(select))
        {
            return (sat, select, $"{sat} is {JsonText.Compact(select)}", selector);
        }

        if (select.Get("where") is not { } where)
        {
            return select.Get("key") is { IsNull: false } ? null : (sat, select, $"{sat} has neither a where nor a key", selector);
        }

        if (where.IsNull)
        {
            return ($"{sat}.where", where, $"{sat}.where is null", "Vega-Lite reads the fields of a select's where");
        }

        const string condition = "Vega-Lite reads the in or the range of each where condition";
        if (where.IsString)
        {
            // Object.entries of a string: its characters, each a string, which is not a condition.
            return where.Value!.Length == 0 ? null
                : ($"{sat}.where.0", where, $"{sat}.where.0 is {JsonText.String(where.Value.Substring(0, 1))}", condition);
        }

        foreach (var (field, value) in Entries(where))
        {
            var cat = $"{sat}.where.{field}";
            if (!IsObjectOrArray(value))
            {
                return (cat, value, $"{cat} is {JsonText.Compact(value)}", condition);
            }

            if (value.Get("in") is { } members)
            {
                if (!members.IsArray)
                {
                    return ($"{cat}.in", members, $"{cat}.in is {JsonText.Compact(members)}, not an array", "Vega-Lite maps the in of a where condition");
                }
            }
            else if (value.Get("range") is not { IsNull: false })
            {
                return (cat, value, $"{cat} has neither an in nor a range", condition);
            }
        }

        return null;
    }

    /// <summary><c>Object.entries(v)</c> of an object or an array: the members, or each index and element; nothing for a
    /// number or a boolean (the callers handle a string).</summary>
    private static List<(string Key, JsonItem Value)> Entries(JsonItem item) =>
        item.IsObject ? item.Members.Select(m => (m.Key, m.Value)).ToList()
        : item.Items.Select((v, i) => (i.ToString(CultureInfo.InvariantCulture), v)).ToList();

    /// <summary>The count of <c>Object.entries(v)</c>: members, elements or characters.</summary>
    private static int EntryCount(JsonItem item) => item.IsObject ? item.Members.Count : item.IsArray ? item.Items.Count : item.IsString ? item.Value!.Length : 0;

    /// <summary>A JSON value as JavaScript's <c>Boolean(v)</c> reads it; an absent value is <c>undefined</c>.</summary>
    private static bool Truthy(JsonItem? item) => item?.Kind switch
    {
        null or JsonItemKind.Null or JsonItemKind.False => false,
        JsonItemKind.Number => Number(item.Value!) != 0,
        JsonItemKind.String => item.Value!.Length > 0,
        _ => true,
    };

    /// <summary><c>v?.length</c> read as a boolean: a string's or an array's length, an object's <c>length</c> member; a
    /// number or a boolean has none.</summary>
    private static bool TruthyLength(JsonItem? item) => item?.Kind switch
    {
        JsonItemKind.Array => item.Items.Count > 0,
        JsonItemKind.Object => Truthy(item.Get("length")),
        JsonItemKind.String => item.Value!.Length > 0,
        _ => false,
    };

    /// <summary>The right-hand side the <c>in</c> operator accepts: an object or an array.</summary>
    private static bool IsObjectOrArray(JsonItem item) => item.IsObject || item.IsArray;

    private void UnknownKeys(JsonItem chartSpec)
    {
        foreach (var member in _spec.Root.Members.Where(m => Array.IndexOf(TopLevel, m.Key) < 0))
        {
            AddAtKey("unknown_key", SpecSeverity.Info, member.Key, member, $"\"{member.Key}\" is not modelled by Graspea.Flint and is passed through Extensions.");
        }

        foreach (var member in chartSpec.Members.Where(m => Array.IndexOf(SpecLevel, m.Key) < 0))
        {
            AddAtKey("unknown_key", SpecSeverity.Info, $"chart_spec.{member.Key}", member, $"\"chart_spec.{member.Key}\" is not modelled by Graspea.Flint and is passed through Extensions.");
        }
    }

    /// <summary>Reports one finding for the targets where <paramref name="severity"/> applies, at the highest severity.</summary>
    private void PerTarget(string code, string path, JsonItem node, Func<BackendTemplateRow, SpecSeverity> severity, Func<SpecSeverity, IReadOnlyList<string>, string> message)
    {
        if (_targets.Length == 0)
        {
            return;
        }

        var max = _targets.Max(severity);
        Add(code, max, path, node, message(max, _targets.Where(t => severity(t) == max).Select(t => t.Backend).ToList()));
    }

    private static double? Number(string raw) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string Format(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty;

    private void Add(string code, SpecSeverity severity, string path, JsonItem node, string message) =>
        _issues.Add(new SpecIssue(IdOf(code), code, severity, path, node.Start, node.End, $"{message} ({code})"));

    private void AddAtKey(string code, SpecSeverity severity, string path, JsonMember member, string message) =>
        _issues.Add(new SpecIssue(IdOf(code), code, severity, path, member.KeyPosition, member.KeyEnd, $"{message} ({code})"));
}
