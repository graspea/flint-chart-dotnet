using System.Collections.Frozen;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Graspea.Flint.Serialization;

namespace Graspea.Flint.Validation.Rules;

/// <summary>
/// One validation (design §6). Backend-scoped checks run for each target backend (the given one, or every backend that
/// has the template) and are reported once, with the highest severity among the targets and the backends it applies to.
/// </summary>
internal sealed partial class ValidationRun
{
    private const int MaxCanvas = 4000;

    /// <summary>The flint-chart MCP server rejects more rows (<c>MAX_DATA_ROWS</c>).</summary>
    private const int MaxRows = 100_000;

    /// <summary>Integers beyond 2^53 lose precision as a JavaScript number.</summary>
    private static readonly BigInteger MaxSafeInteger = BigInteger.Pow(2, 53);

    private static readonly FlintBackend[] LayoutBackends = [FlintBackend.VegaLite, FlintBackend.ECharts, FlintBackend.ChartJs, FlintBackend.Plotly];

    private readonly ChartAssemblyInput input;
    private readonly FlintBackend? backend;
    private readonly List<ValidationIssue> issues = [];
    private FrozenDictionary<FlintBackend, TemplateRow> template = FrozenDictionary<FlintBackend, TemplateRow>.Empty;
    private FlintBackend[] targets = [];

    internal ValidationRun(ChartAssemblyInput input, FlintBackend? backend)
    {
        this.input = input;
        this.backend = backend;
    }

    internal static string Id(FlintBackend b) => b switch
    {
        FlintBackend.VegaLite => "vegalite",
        FlintBackend.ECharts => "echarts",
        FlintBackend.ChartJs => "chartjs",
        FlintBackend.Plotly => "plotly",
        FlintBackend.Excel => "excel",
        _ => b.ToString(),
    };

    internal List<ValidationIssue> Run()
    {
        if (input.Data is null)
        {
            Add(IssueSeverity.Error, Codes.DataMissing, "data", "data is null; every backend throws reading data.values.");
        }

        var spec = input.ChartSpec;
        if (spec is null)
        {
            Add(IssueSeverity.Error, Codes.UnknownChartType, "chart_spec", "chart_spec is null; every backend throws reading chartType.");
            RowChecks();
            SemanticTypeChecks();
            return issues;
        }

        ResolveTemplate(spec);
        var types = new FieldTypes(input);
        var excelReasons = ExcelReasons(types);

        if (spec.Encodings is null)
        {
            Add(IssueSeverity.Error, Codes.MissingEncodings, "chart_spec.encodings", "chart_spec.encodings is missing; every backend throws.");
        }
        else
        {
            EncodingChecks(spec, excelReasons);
            if (targets.Any(LayoutBackends.Contains))
            {
                foreach (var v in StaticSeries.Evaluate(input, types))
                {
                    Add(IssueSeverity.Error, v.Code, $"chart_spec.encodings.{v.Channel}", $"{v.Message} {Throws(targets.Where(LayoutBackends.Contains))}", v.Channel, v.Field);
                }
            }
        }

        DataChecks(excelReasons);
        RowChecks();
        SemanticTypeChecks();
        ThemeChecks();
        PropertyChecks(spec);
        LiteralChecks(spec);
        SortByChecks(spec);
        RequiredChannelChecks(spec);
        OptionChecks();
        CanvasChecks(spec);
        return issues;
    }

    private void ResolveTemplate(ChartSpec spec)
    {
        var name = spec.ChartType.Name;
        if (name is null || !CatalogTables.ChartTypes.TryGetValue(name, out var rows))
        {
            Add(IssueSeverity.Error, Codes.UnknownChartType, "chart_spec.chartType", $"\"{name}\" is not a chart type of flint-chart {FlintUpstream.Version}; every backend throws.");
            return;
        }

        if (backend is { } b && !rows.ContainsKey(b))
        {
            Add(IssueSeverity.Error, Codes.ChartTypeNotOnBackend, "chart_spec.chartType", $"\"{name}\" has no {Id(b)} template ({string.Join(", ", Ordered(rows.Keys).Select(Id))} have it); {Id(b)} throws.");
            return;
        }

        template = rows;
        targets = backend is { } only ? [only] : Ordered(rows.Keys);
    }

    private static FlintBackend[] Ordered(IEnumerable<FlintBackend> backends) => CatalogTables.Backends.Where(backends.Contains).ToArray();

    private List<ExcelReason> ExcelReasons(FieldTypes types)
    {
        if (!targets.Contains(FlintBackend.Excel) || input.ChartSpec.Encodings is not { Count: > 0 } || input.Data is null)
        {
            return [];
        }

        return Excel.Evaluate(input, types, template[FlintBackend.Excel]);
    }

    private bool HasRows => input.Data?.Values is { Count: > 0 };

    private void EncodingChecks(ChartSpec spec, List<ExcelReason> excelReasons)
    {
        var encodings = spec.Encodings;
        if (encodings.Count == 0)
        {
            PerTarget(Codes.EncodingsEmpty, "chart_spec.encodings", b => b == FlintBackend.Excel ? IssueSeverity.Error : IssueSeverity.Info,
                (severity, on) => severity == IssueSeverity.Error
                    ? $"Encodings are empty; Excel throws (it needs a quantitative measure). {Throws(on)}"
                    : $"Encodings are empty; {string.Join(", ", on.Select(Id))} assemble an empty chart.");
        }

        foreach (var (channel, value) in encodings)
        {
            var path = $"chart_spec.encodings.{channel}";
            if (value is null)
            {
                PerTarget(Codes.ChannelNull, path, b => b == FlintBackend.Excel ? IssueSeverity.Info : IssueSeverity.Error,
                    (severity, on) => severity == IssueSeverity.Error
                        ? $"The \"{channel}\" encoding is null. {Throws(on)}"
                        : $"The \"{channel}\" encoding is null; Excel ignores it.",
                    channel);
                continue;
            }

            UnsupportedChannel(channel, path, excelReasons);
            if (!value.IsSeries)
            {
                UnknownField(channel, path, value.Encodings[0]);
            }
        }

        if (input.Data is { } data && data.Values is { Count: > 0 } && targets.Contains(FlintBackend.Excel))
        {
            foreach (var reason in excelReasons)
            {
                AddExcel(reason);
            }
        }
    }

    private void AddExcel(ExcelReason reason) =>
        Add(IssueSeverity.Error, Codes.ExcelUnsupported, reason.Channel is { } ch ? $"chart_spec.encodings.{ch}" : "chart_spec",
            $"Excel {reason.Message}; Excel throws.", reason.Channel);

    private void UnsupportedChannel(string channel, string path, List<ExcelReason> excelReasons)
    {
        if (targets.Length == 0)
        {
            return;
        }

        if (backend is { } b)
        {
            if (template[b].Channels.Contains(channel) || (b == FlintBackend.Excel && excelReasons.Any(r => r.Channel == channel)))
            {
                return;
            }

            Add(IssueSeverity.Warning, Codes.UnsupportedChannel, path, $"The {Id(b)} template of \"{input.ChartSpec.ChartType.Name}\" does not declare \"{channel}\"; {Id(b)} ignores it.", channel);
            return;
        }

        var declaring = targets.Where(t => template[t].Channels.Contains(channel)).ToArray();
        if (declaring.Length == 0)
        {
            Add(IssueSeverity.Warning, Codes.UnsupportedChannel, path, $"No backend's template of \"{input.ChartSpec.ChartType.Name}\" declares \"{channel}\"; it is ignored.", channel);
        }
    }

    private void UnknownField(string channel, string path, ChartEncoding encoding)
    {
        if (encoding.Field is not { } field || input.Data?.Values is not { Count: > 0 } values || values[0] is not JsonObject)
        {
            return;
        }

        if (!input.Data.FirstRowKeys.Contains(field))
        {
            Add(IssueSeverity.Warning, Codes.UnknownField, $"{path}.field", $"\"{field}\" is not a key of the first row; the encoding resolves to nothing.", channel, field);
        }
    }

    private void DataChecks(List<ExcelReason> excelReasons)
    {
        if (input.Data is not { } data || targets.Length == 0 || HasRows)
        {
            return;
        }

        var urlOnly = data.Url is not null && data.Values is null;
        var rowReasons = excelReasons.Where(r => r.FromRowsOrTypes).ToArray();
        foreach (var reason in excelReasons.Where(r => !r.FromRowsOrTypes))
        {
            AddExcel(reason);
        }

        var excelThrows = rowReasons.Length > 0;
        var code = urlOnly ? Codes.DataUrlOnly : Codes.DataEmpty;
        if (targets.Contains(FlintBackend.Excel))
        {
            var because = excelThrows ? rowReasons[0].Message : null;
            Add(excelThrows ? IssueSeverity.Error : IssueSeverity.Info, code, urlOnly ? "data.url" : "data.values",
                excelThrows
                    ? $"Excel reads only data.values and has no rows: it {because}; Excel throws."
                    : urlOnly ? "Excel reads only data.values; the URL is not fetched and the chart has no rows." : "data.values has no rows; Excel assembles an empty chart.");
        }

        var others = targets.Where(t => t != FlintBackend.Excel).ToArray();
        if (others.Length > 0)
        {
            Add(IssueSeverity.Info, urlOnly ? Codes.DataUrlNotFetched : Codes.DataEmpty, urlOnly ? "data.url" : "data.values",
                urlOnly
                    ? $"flint-chart never fetches data.url; {string.Join(", ", others.Select(Id))} assemble an empty chart."
                    : $"data.values has no rows; {string.Join(", ", others.Select(Id))} assemble an empty chart.");
        }
    }

    private void RowChecks()
    {
        if (input.Data is { Values: not null, Url: not null })
        {
            Add(IssueSeverity.Info, Codes.DataValuesAndUrl, "data", "data has both values and url; flint-chart reads data.values and ignores the URL.");
        }

        if (input.Data?.Values is not { Count: > 0 } values)
        {
            return;
        }

        if (values.Count > MaxRows)
        {
            Add(IssueSeverity.Info, Codes.TooManyRows, "data.values",
                $"{values.Count} rows exceed {MaxRows}, the limit of the flint-chart MCP server; the assemblers accept them.");
        }

        RowValueChecks(values);

        NonObjectRows(values);

        if (values[0] is not JsonObject first)
        {
            return;
        }

        var keys = first.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var differing = Enumerable.Range(1, values.Count - 1)
            .Where(i => values[i] is JsonObject row && !keys.SetEquals(row.Select(p => p.Key)))
            .ToArray();
        if (differing.Length > 0)
        {
            Add(IssueSeverity.Info, Codes.InconsistentRowKeys, $"data.values[{differing[0]}]",
                $"{differing.Length} row(s) have other keys than the first row, the first at index {differing[0]}; flint-chart detects fields from the first row only.");
        }
    }

    /// <summary>A null row throws on every backend: upstream reads keys of every row. A primitive row throws when a key of
    /// the first row is temporal, because <c>convertTemporalData</c> writes that key back into every row, which strict mode
    /// refuses on a number, string or boolean. Anything else assembles; only the MCP front door rejects it.</summary>
    private void NonObjectRows(JsonArray values)
    {
        var nulls = Enumerable.Range(0, values.Count).Where(i => values[i] is null).ToArray();
        if (nulls.Length > 0)
        {
            Add(IssueSeverity.Error, Codes.RowNotObject, $"data.values[{nulls[0]}]",
                $"{nulls.Length} row(s) are null, the first at index {nulls[0]}; upstream reads the keys of every row. {ThrowsOnTargets()}");
            return;
        }

        var notObjects = Enumerable.Range(0, values.Count).Where(i => values[i] is not JsonObject).ToArray();
        if (notObjects.Length == 0)
        {
            return;
        }

        var primitives = notObjects.Where(i => values[i] is JsonValue).ToArray();
        if (primitives.Length > 0 && values[0] is JsonObject && new FieldTypes(input).HasTemporalKey(input.Data!.FirstRowKeys) == true)
        {
            Add(IssueSeverity.Error, Codes.RowNotObject, $"data.values[{primitives[0]}]",
                $"{primitives.Length} row(s) are numbers, strings or booleans, the first at index {primitives[0]}; upstream writes the temporal keys of the first row into every row. {ThrowsOnTargets()}");
            return;
        }

        Add(IssueSeverity.Info, Codes.RowNotObject, $"data.values[{notObjects[0]}]",
            $"{notObjects.Length} row(s) are not JSON objects, the first at index {notObjects[0]}; field detection reads object keys.");
    }

    /// <summary>The row values of design §5.3 that upstream may read differently from what the .NET value meant. Each code
    /// is reported once per field, at the first row that has it.</summary>
    private void RowValueChecks(JsonArray values)
    {
        var reported = new HashSet<(string Code, string Field)>();
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is not JsonObject row)
            {
                continue;
            }

            foreach (var (field, value) in row)
            {
                if (RowValueIssue(field, value) is not { } found || !reported.Add((found.Code, field)))
                {
                    continue;
                }

                Add(IssueSeverity.Info, found.Code, $"data.values[{i}].{field}", found.Message, field: field);
            }
        }
    }

    private (string Code, string Message)? RowValueIssue(string field, JsonNode? value)
    {
        switch (value)
        {
            case null when IsAnnotatedTemporal(field):
                return (Codes.NullInTemporalField, $"\"{field}\" is annotated as a temporal type and has a null value; upstream skips or misplaces it on a time axis.");
            case JsonObject or JsonArray:
                return (Codes.NestedValue, $"\"{field}\" holds a nested {(value is JsonObject ? "object" : "array")}; flint-chart reads it as a nominal value.");
            case JsonValue v when v.GetValueKind() == JsonValueKind.Number && IsUnsafeInteger(v.ToJsonString()):
                return (Codes.LargeInteger, $"\"{field}\" holds {v.ToJsonString()}, beyond 2^53; JavaScript reads it as the nearest double.");
            case JsonValue v when v.GetValueKind() == JsonValueKind.String && UnspecifiedDateTime().IsMatch(v.GetValue<string>()):
                return (Codes.UnspecifiedDatetime, $"\"{field}\" holds a date and time without a zone (\"{v.GetValue<string>()}\"); it is read in the local time zone of the process that runs flint-chart.");
            case JsonValue v when v.GetValueKind() == JsonValueKind.String && DayTimeSpan().IsMatch(v.GetValue<string>()):
                return (Codes.TimespanAsString, $"\"{field}\" holds a duration string (\"{v.GetValue<string>()}\"), which upstream infers as temporal; write the duration as a number.");
            default:
                return null;
        }
    }

    private static bool IsUnsafeInteger(string number) =>
        number.AsSpan().IndexOfAny(".eE") < 0 && BigInteger.TryParse(number, out var n) && BigInteger.Abs(n) > MaxSafeInteger;

    private bool IsAnnotatedTemporal(string field) =>
        input.SemanticTypes is { } types && types.TryGetValue(field, out var annotation)
        && annotation?.Type.Name is { } name && CatalogTables.SemanticTypes.TryGetValue(name, out var encodings) && encodings.Contains("temporal");

    /// <summary>An ISO 8601 date and time without <c>Z</c> or an offset, as a <c>DateTime</c> of kind Unspecified is written.</summary>
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex UnspecifiedDateTime();

    /// <summary>A <c>TimeSpan</c> of a day or more (<c>"1.02:03:04"</c>); shorter ones look like a time of day, which infers nominal.</summary>
    [GeneratedRegex(@"^-?\d+\.\d{2}:\d{2}:\d{2}(\.\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex DayTimeSpan();

    private void SemanticTypeChecks()
    {
        if (input.SemanticTypes is null)
        {
            return;
        }

        var firstRow = input.Data?.Values is { Count: > 0 } values ? values[0] as JsonObject : null;
        var bound = (input.ChartSpec?.Encodings ?? []).Values.SelectMany(v => v?.Encodings ?? []).Select(e => e.Field).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var (field, annotation) in input.SemanticTypes)
        {
            if (annotation?.Type.Name is { } reserved && Js.ObjectPrototypeKeys.Contains(reserved) && (bound.Contains(field) || firstRow?.ContainsKey(field) == true))
            {
                Add(IssueSeverity.Error, Codes.ReservedSemanticType, $"semantic_types.{field}",
                    $"\"{reserved}\" is a member of JavaScript's Object.prototype; upstream's registry lookup counts it as registered and finds no entry. {ThrowsOnTargets()}",
                    field: field);
                continue;
            }

            if (firstRow is not null && !firstRow.ContainsKey(field))
            {
                Add(IssueSeverity.Info, Codes.SemanticTypeWithoutField, $"semantic_types.{field}",
                    $"\"{field}\" is not a key of the first row; upstream never reads this annotation.", field: field);
            }

            if (annotation?.Type is { Name: { } name } type && !type.IsRegistered)
            {
                Add(IssueSeverity.Warning, Codes.UnknownSemanticType, $"semantic_types.{field}",
                    $"\"{name}\" is not a registered semantic type of flint-chart {FlintUpstream.Version}; the type is inferred from the data, as without an annotation.",
                    field: field);
            }
        }
    }

    /// <summary>A preset id and an object's <c>extends</c> both resolve through upstream's <c>presetSpec</c>, which
    /// throws on an unknown id (core/theme/presets.ts).</summary>
    private void ThemeChecks()
    {
        var (id, path) = input.ThemeSpec switch
        {
            { Preset.Name: { } name } => (name, "theme_spec"),
            { Spec.Extends: { } extends } => (extends, "theme_spec.extends"),
            _ => (null, ""),
        };
        if (input.ThemeSpec is null || targets.Length == 0)
        {
            return;
        }

        // THEME_PRESETS[id] is a plain-object lookup, so a member of Object.prototype is found and nothing throws.
        if (id is null || CatalogTables.ThemePresets.Contains(id) || Js.ObjectPrototypeKeys.Contains(id))
        {
            PerTarget(Codes.ThemeIgnoredByBackend, "theme_spec", b => CatalogTables.BackendsApplyingTheme.Contains(b) ? null : IssueSeverity.Info,
                (_, on) => $"{string.Join(", ", on.Select(Id))} do not apply themes; the theme has no effect there.");
            return;
        }

        PerTarget(Codes.UnknownThemePreset, path, b => CatalogTables.BackendsApplyingTheme.Contains(b) ? IssueSeverity.Error : IssueSeverity.Info,
            (severity, on) => severity == IssueSeverity.Error
                ? $"\"{id}\" is not a theme preset of flint-chart {FlintUpstream.Version}. {Throws(on)}"
                : $"\"{id}\" is not a theme preset; {string.Join(", ", on.Select(Id))} do not apply themes.");
    }

    private void PropertyChecks(ChartSpec spec)
    {
        if (spec.ChartProperties is null || targets.Length == 0)
        {
            return;
        }

        foreach (var (key, value) in spec.ChartProperties)
        {
            if (value is null)
            {
                continue;
            }

            var path = $"chart_spec.chartProperties.{key}";
            var invalidOn = targets.Where(t => template[t].Properties.TryGetValue(key, out var p) && p.Kind == PropertyKind.Discrete && !Accepts(p, value)).ToArray();
            if (invalidOn.Length > 0)
            {
                Add(IssueSeverity.Warning, Codes.InvalidOptionValue, path,
                    $"{value.ToJsonString()} is not an option of \"{key}\" on {string.Join(", ", invalidOn.Select(Id))}; upstream drops it (invalid-option-value) and uses the default.");
            }

            var undeclaredOn = targets.Where(t => !template[t].Properties.ContainsKey(key)).ToArray();
            if (!CatalogTables.HostStateKeys.Contains(key) && undeclaredOn.Length > 0 && (backend is not null || undeclaredOn.Length == targets.Length))
            {
                Add(IssueSeverity.Info, Codes.UnknownChartProperty, path,
                    $"The {string.Join(", ", undeclaredOn.Select(Id))} template of \"{input.ChartSpec.ChartType.Name}\" does not declare \"{key}\"; it is passed through and may have no effect.");
            }

            if (Js.Kind(value) == JsonValueKind.Number)
            {
                var number = Js.NumberOf(value);
                var outOn = targets.Where(t => template[t].Properties.TryGetValue(key, out var p) && p.Kind == PropertyKind.Continuous
                    && (number < p.Min || number > p.Max)).ToArray();
                if (outOn.Length > 0)
                {
                    var p = template[outOn[0]].Properties[key];
                    Add(IssueSeverity.Info, Codes.ContinuousOutOfRange, path,
                        $"{value.ToJsonString()} is outside the declared range {p.Min}..{p.Max} of \"{key}\" on {string.Join(", ", outOn.Select(Id))}; upstream passes it through unchecked.");
                }
            }
        }
    }

    /// <summary><c>normalizeChartProperties</c>: a value equal (===) to an option value, or a string equal to an option label
    /// ignoring case and surrounding whitespace.</summary>
    private static bool Accepts(PropertyRow property, JsonNode value)
    {
        var kind = Js.Kind(value);
        foreach (var option in property.Options ?? [])
        {
            if (option is null || kind is JsonValueKind.Array or JsonValueKind.Object)
            {
                continue;
            }

            var optionValue = JsonNode.Parse(option);
            var optionKind = Js.Kind(optionValue);
            if (optionKind == kind && kind switch
            {
                JsonValueKind.String => optionValue!.GetValue<string>() == value.GetValue<string>(),
                JsonValueKind.Number => Js.NumberOf(optionValue!) == Js.NumberOf(value),
                _ => true,
            })
            {
                return true;
            }
        }

        return kind == JsonValueKind.String && property.Labels is { } labels
            && labels.Any(l => string.Equals(l.ToLowerInvariant(), Js.Trim(value.GetValue<string>()).ToLowerInvariant(), StringComparison.Ordinal));
    }

    private void LiteralChecks(ChartSpec spec)
    {
        foreach (var (channel, value) in spec.Encodings ?? [])
        {
            if (value is null)
            {
                continue;
            }

            for (var i = 0; i < value.Encodings.Count; i++)
            {
                var e = value.Encodings[i];
                var path = value.IsSeries ? $"chart_spec.encodings.{channel}[{i}]" : $"chart_spec.encodings.{channel}";
                Literal(e.Type?.Name, EncodingType.Literals, $"{path}.type", channel);
                Literal(e.Aggregate?.Name, Aggregate.Literals, $"{path}.aggregate", channel);
                Literal(e.SortOrder?.Name, SortOrder.Literals, $"{path}.sortOrder", channel);
            }
        }

        Literal(input.Options?.ContinuousMarkCrossSection?.SeriesCountAxis?.Name, SeriesCountAxis.Literals, "options.continuousMarkCrossSection.seriesCountAxis", null);
        Literal(input.ThemeSpec?.Spec?.CompileDefaults?.ContinuousMarkCrossSection?.SeriesCountAxis?.Name, SeriesCountAxis.Literals,
            "theme_spec.compileDefaults.continuousMarkCrossSection.seriesCountAxis", null);
    }

    private void Literal(string? value, FrozenSet<string> literals, string path, string? channel)
    {
        if (value is not null && !literals.Contains(value))
        {
            Add(IssueSeverity.Info, Codes.InvalidLiteral, path,
                $"\"{value}\" is not one of {string.Join(", ", literals.Order(StringComparer.Ordinal))} in flint-chart {FlintUpstream.Version}; it is written as given.", channel);
        }
    }

    /// <summary>Upstream sorts by <c>x</c>, <c>y</c>, <c>color</c> or a JSON array of values; anything else is ignored (Vega-Lite
    /// logs <c>sort error</c>). Some ECharts templates also honour <c>size</c> and <c>group</c>.</summary>
    private void SortByChecks(ChartSpec spec)
    {
        if (targets.Length == 0)
        {
            return;
        }

        foreach (var (channel, value) in spec.Encodings ?? [])
        {
            for (var i = 0; i < (value?.Encodings.Count ?? 0); i++)
            {
                if (value!.Encodings[i].SortBy is not { } sortBy || sortBy is "x" or "y" or "color" || IsJsonArray(sortBy))
                {
                    continue;
                }

                var path = value.IsSeries ? $"chart_spec.encodings.{channel}[{i}].sortBy" : $"chart_spec.encodings.{channel}.sortBy";
                PerTarget(Codes.SortByInvalid, path, b => b == FlintBackend.ECharts && sortBy is "size" or "group" ? IssueSeverity.Info : IssueSeverity.Warning,
                    (severity, on) => severity == IssueSeverity.Warning
                        ? $"sortBy \"{sortBy}\" is neither x, y, color nor a JSON array of values; {string.Join(", ", on.Select(Id))} ignore it."
                        : $"sortBy \"{sortBy}\" is honoured only by some ECharts templates.",
                    channel);
            }
        }
    }

    private static bool IsJsonArray(string s)
    {
        try
        {
            return JsonNode.Parse(s) is JsonArray;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The flint-chart MCP server requires x and y when the template declares both, and metric and value on a
    /// KPI Card (<c>requiredChannels</c>); the assemblers accept a document without them.</summary>
    private void RequiredChannelChecks(ChartSpec spec)
    {
        if (spec.Encodings is not { Count: > 0 } encodings || targets.Length == 0)
        {
            return;
        }

        var required = targets
            .SelectMany(t => template[t].Channels.Contains("x") && template[t].Channels.Contains("y") ? ["x", "y"]
                : spec.ChartType.Name == "KPI Card" ? ["metric", "value"] : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal);
        foreach (var channel in required)
        {
            if (!encodings.TryGetValue(channel, out var value) || !IsBound(value))
            {
                Add(IssueSeverity.Info, Codes.RequiredChannelMissing, $"chart_spec.encodings.{channel}",
                    $"\"{channel}\" is not bound; the flint-chart MCP server rejects a \"{spec.ChartType.Name}\" without it, the assemblers accept it.", channel);
            }
        }
    }

    private static bool IsBound(EncodingValue? value) =>
        value is not null && value.Encodings.Any(e => (e.Field is { } f && Js.Trim(f).Length > 0) || e.Aggregate?.Name == "count");

    /// <summary>The layout backends overwrite the options in the catalog's <c>ignoredOptions</c>; the Excel backend reads no
    /// option.</summary>
    private void OptionChecks()
    {
        if (input.Options is null || targets.Length == 0)
        {
            return;
        }

        var options = JsonSerializer.SerializeToNode(input.Options, FlintJsonContext.Default.AssembleOptions)!.AsObject();
        foreach (var (key, _) in options)
        {
            PerTarget(Codes.OptionOverriddenByBackend, $"options.{key}",
                b => b == FlintBackend.Excel || (CatalogTables.IgnoredOptions.TryGetValue(key, out var on) && on.Contains(b)) ? IssueSeverity.Info : null,
                (_, on) => $"{string.Join(", ", on.Select(Id))} {(on.Length == 1 ? "does" : "do")} not use \"{key}\"; the option has no effect there.");
        }
    }

    private void CanvasChecks(ChartSpec spec)
    {
        foreach (var (name, size) in new[] { ("baseSize", spec.BaseSize), ("canvasSize", spec.CanvasSize) })
        {
            if (size is { } s && (s.Width > MaxCanvas || s.Height > MaxCanvas))
            {
                Add(IssueSeverity.Info, Codes.CanvasTooLarge, $"chart_spec.{name}",
                    $"{s.Width} x {s.Height} exceeds {MaxCanvas} px, the limit of the flint-chart MCP server; the assemblers accept it.");
            }
        }
    }

    /// <summary>Reports one issue for the targets where <paramref name="severity"/> applies, at the highest severity.</summary>
    private void PerTarget(string code, string path, Func<FlintBackend, IssueSeverity?> severity, Func<IssueSeverity, FlintBackend[], string> message, string? channel = null)
    {
        var applicable = targets.Select(t => (Backend: t, Severity: severity(t))).Where(x => x.Severity is not null).ToArray();
        if (applicable.Length == 0)
        {
            return;
        }

        var max = applicable.Max(x => x.Severity!.Value);
        Add(max, code, path, message(max, applicable.Where(x => x.Severity == max).Select(x => x.Backend).ToArray()), channel);
    }

    private static string Throws(IEnumerable<FlintBackend> backends) => $"Throws on {string.Join(", ", backends.Select(Id))}.";

    /// <summary>For a throw on every backend: the target backends, or all of them when the template is unresolved.</summary>
    private string ThrowsOnTargets() => targets.Length > 0 ? Throws(targets) : "Every backend throws.";

    private void Add(IssueSeverity severity, string code, string path, string message, string? channel = null, string? field = null) =>
        issues.Add(new ValidationIssue
        {
            Severity = severity,
            Code = code,
            Message = message,
            Path = path,
            Channel = channel is null ? null : new Channel(channel),
            Field = field,
        });
}
