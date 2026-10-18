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

    private static readonly string[] TopLevel = ["data", "semantic_types", "chart_spec", "theme_spec", "options", "field_display_names"];
    private static readonly string[] SpecLevel = ["chartType", "title", "subtitle", "encodings", "baseSize", "canvasSize", "chartProperties"];

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
                PerTarget("channel_null", path, member.Value, t => t.Backend == "excel" ? SpecSeverity.Info : SpecSeverity.Error,
                    (severity, on) => severity == SpecSeverity.Error
                        ? $"The \"{channel}\" encoding is null; {string.Join(", ", on)} throw."
                        : $"The \"{channel}\" encoding is null; excel ignores it.");
                continue;
            }

            if (_targets.Length > 0 && !_targets.Any(t => Array.IndexOf(t.Channels, channel) >= 0))
            {
                AddAtKey("unsupported_channel", SpecSeverity.Warning, path, member,
                    $"No backend's template of \"{ChartTypeName}\" declares \"{channel}\"; it is ignored.");
            }
        }
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

            if (NameTables.SemanticType(name.Value!) is null)
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
            var invalidOn = _targets.Where(t => t.Property(key) is { Kind: CatalogPropertyKind.Discrete } p && !Accepts(p, value)).Select(t => t.Backend).ToList();
            if (invalidOn.Count > 0)
            {
                Add("invalid_option_value", SpecSeverity.Warning, path, value,
                    $"{JsonText.Compact(value)} is not an option of \"{key}\" on {string.Join(", ", invalidOn)}; upstream drops it (invalid-option-value) and uses the default.");
            }

            var undeclaredOn = _targets.Where(t => t.Property(key) is null).Select(t => t.Backend).ToList();
            if (Array.IndexOf(Catalog.HostStateKeys, key) < 0 && undeclaredOn.Count == _targets.Length)
            {
                Add("unknown_chart_property", SpecSeverity.Info, path, value,
                    $"The {string.Join(", ", undeclaredOn)} template of \"{ChartTypeName}\" does not declare \"{key}\"; it is passed through and may have no effect.");
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

    /// <summary><c>normalizeChartProperties</c>: a value equal (===) to an option value, or a string equal to an option label
    /// ignoring case and surrounding whitespace.</summary>
    private static bool Accepts(CatalogPropertyRow property, JsonItem value)
    {
        if (value.IsArray || value.IsObject)
        {
            return false;
        }

        foreach (var option in property.Options ?? [])
        {
            if (option is null)
            {
                continue;
            }

            var optionValue = JsonReader.Parse(option);
            var equal = (optionValue.Kind, value.Kind) switch
            {
                (JsonItemKind.String, JsonItemKind.String) => optionValue.Value == value.Value,
                (JsonItemKind.Number, JsonItemKind.Number) => Number(optionValue.Value!) == Number(value.Value!),
                _ => optionValue.Kind == value.Kind,
            };
            if (equal)
            {
                return true;
            }
        }

        return value.IsString && property.Labels is { } labels
            && labels.Any(l => string.Equals(l.ToLowerInvariant(), value.Value!.Trim(JsWhiteSpace).ToLowerInvariant(), StringComparison.Ordinal));
    }

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
