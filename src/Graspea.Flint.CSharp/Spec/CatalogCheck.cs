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

    private static readonly string[] TopLevel = ["data", "semantic_types", "chart_spec", "theme_spec", "options", "field_display_names", "interaction_spec"];
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
            or "canvas_too_large" or "required_channel_missing" => "FLINT116",
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
        RequiredChannelChecks(encodings);
        CanvasChecks(chartSpec);
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
                        $"No backend's template of \"{ChartTypeName}\" declares \"{channel}\"; it is ignored.");
                }
            }
        }
    }

    /// <summary>The templates of the Excel shared path (src/excel/assemble.ts:264-310), as the validator's <c>Excel</c>.</summary>
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

            var invalidOn = invalid.Except(optionRetiredOn).Select(t => t.Backend).ToList();
            if (invalidOn.Count > 0)
            {
                Add("invalid_option_value", SpecSeverity.Warning, path, value,
                    $"{JsonText.Compact(value)} is not an option of \"{key}\" on {string.Join(", ", invalidOn)}; upstream drops it (invalid-option-value) and uses the default.");
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

    /// <summary><c>normalizeChartProperties</c>: a value equal to an option value under <c>sameOptionValue</c> (===, or for
    /// two arrays the same length with every entry ===), or a string equal to an option label ignoring case and surrounding
    /// whitespace.</summary>
    private static bool Accepts(CatalogPropertyRow property, JsonItem value)
    {
        foreach (var option in property.Options ?? [])
        {
            if (option is not null && SameOptionValue(JsonReader.Parse(option), value))
            {
                return true;
            }
        }

        return value.IsString && property.Labels is { } labels
            && labels.Any(l => string.Equals(l.ToLowerInvariant(), value.Value!.Trim(JsWhiteSpace).ToLowerInvariant(), StringComparison.Ordinal));
    }

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
