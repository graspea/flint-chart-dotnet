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

    /// <summary>The backends that assemble natively, without the layout pipeline: no static series, no sort repair, no
    /// option, theme or chart property read; each has hand-written throw rules (<see cref="Excel"/>, <see cref="ImageCharts"/>).</summary>
    private static readonly FlintBackend[] NativeBackends = [FlintBackend.Excel, FlintBackend.ImageCharts];

    private readonly ChartAssemblyInput input;
    private readonly FlintBackend? backend;

    /// <summary>Set when the rows are not inspected: streamed data or a template (design §13.6).</summary>
    private readonly DeferredRows? deferred;
    private readonly List<ValidationIssue> issues = [];
    private FrozenDictionary<FlintBackend, TemplateRow> template = FrozenDictionary<FlintBackend, TemplateRow>.Empty;
    private FlintBackend[] targets = [];

    internal ValidationRun(ChartAssemblyInput input, FlintBackend? backend, DeferredRows? deferred = null)
    {
        this.input = input;
        this.backend = backend;
        this.deferred = deferred ?? (input.Data?.Streamed is { } streamed ? new DeferredRows(streamed.Keys, Template: false) : null);
    }

    internal static string Id(FlintBackend b) => b switch
    {
        FlintBackend.VegaLite => "vegalite",
        FlintBackend.ECharts => "echarts",
        FlintBackend.ChartJs => "chartjs",
        FlintBackend.Plotly => "plotly",
        FlintBackend.Excel => "excel",
        FlintBackend.ImageCharts => "image-charts",
        _ => b.ToString(),
    };

    /// <summary>The name a message gives a native backend.</summary>
    private static string Name(FlintBackend b) => b switch
    {
        FlintBackend.Excel => "Excel",
        FlintBackend.ImageCharts => "Image-Charts",
        _ => Id(b),
    };

    internal List<ValidationIssue> Run()
    {
        if (input.Data is null && deferred is not { Template: true })
        {
            Add(IssueSeverity.Error, Codes.DataMissing, "data", "data is absent or null; every backend throws reading data.values.");
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
        var reasons = NativeReasons(types);

        if (spec.Encodings is null)
        {
            Add(IssueSeverity.Error, Codes.MissingEncodings, "chart_spec.encodings", "chart_spec.encodings is missing; every backend throws.");
        }
        else
        {
            EncodingChecks(spec, types, reasons);
            if (targets.Any(LayoutBackends.Contains))
            {
                foreach (var v in StaticSeries.Evaluate(input, types, deferred?.Keys))
                {
                    Add(v.Deferred ? IssueSeverity.Warning : IssueSeverity.Error, v.Code, $"chart_spec.encodings.{v.Channel}",
                        $"{v.Message} {Throws(targets.Where(LayoutBackends.Contains))}", v.Channel, v.Field);
                }
            }
        }

        if (deferred is null)
        {
            DataChecks(reasons);
        }
        else
        {
            DeferredChecks(reasons);
        }

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

    /// <summary>The throw reasons of each native target, in backend order; without the rows only those that follow from
    /// the spec alone.</summary>
    private Dictionary<FlintBackend, List<ThrowReason>> NativeReasons(FieldTypes types)
    {
        var reasons = new Dictionary<FlintBackend, List<ThrowReason>>();
        if (input.ChartSpec.Encodings is not { Count: > 0 } || (input.Data is null && deferred is null))
        {
            return reasons;
        }

        foreach (var b in targets.Where(NativeBackends.Contains))
        {
            var all = b == FlintBackend.Excel ? Excel.Evaluate(input, types, template[b]) : ImageCharts.Evaluate(input, types);
            reasons[b] = deferred is null ? all : [.. all.Where(r => !r.FromRowsOrTypes)];
        }

        return reasons;
    }

    /// <summary>The keys of <c>data[0]</c> when they are known: the keys of an object first row, or the metadata keys of
    /// rows not yet seen with a static shape; null otherwise, and the key rules skip.</summary>
    private IReadOnlyList<string>? Keys =>
        deferred is not null ? deferred.Keys : input.Data?.Values is [JsonObject, ..] ? input.Data.FirstRowKeys : null;

    /// <summary>Without the rows: the native reasons that follow from the spec, and one Info naming what was not checked.</summary>
    private void DeferredChecks(Dictionary<FlintBackend, List<ThrowReason>> reasons)
    {
        foreach (var (b, list) in reasons)
        {
            foreach (var reason in list)
            {
                AddNative(b, reason);
            }
        }

        var source = deferred!.Template ? "A template has no rows yet" : "The rows are streamed and not read by validation";
        var keys = deferred.Keys is null
            ? " The row shape is dynamic, so unknown_field, series_field_missing and the row-key part of reserved_semantic_type are skipped too."
            : " Fields are checked against the keys the row type writes.";
        Add(IssueSeverity.Info, Codes.RowsNotInspected, "data",
            $"{source}; the row checks are skipped: row_not_object, inconsistent_row_keys, too_many_rows, data_empty, null_in_temporal_field, large_integer, unspecified_datetime, timespan_as_string, nested_value, and the field types inferred from values that the Excel and Image-Charts rules and series_nominal_entry read.{keys} Validate a sample with every rule: ChartValidator.Validate(template.Bind(rows), backend).");
    }

    private bool HasRows => input.Data?.Values is { Count: > 0 };

    private void EncodingChecks(ChartSpec spec, FieldTypes types, Dictionary<FlintBackend, List<ThrowReason>> reasons)
    {
        var encodings = spec.Encodings;
        if (encodings.Count == 0)
        {
            PerTarget(Codes.EncodingsEmpty, "chart_spec.encodings", b => NativeBackends.Contains(b) ? IssueSeverity.Error : IssueSeverity.Info,
                (severity, on) => severity == IssueSeverity.Error
                    ? $"Encodings are empty; {string.Join(" and ", on.Select(b => $"{Name(b)} throws ({Needs(b)})"))}. {Throws(on)}"
                    : $"Encodings are empty; {string.Join(", ", on.Select(Id))} assemble an empty chart.");
        }

        foreach (var (channel, value) in encodings)
        {
            var path = $"chart_spec.encodings.{channel}";
            if (value is null)
            {
                PerTarget(Codes.ChannelNull, path, b => NativeBackends.Contains(b) ? IssueSeverity.Info : IssueSeverity.Error,
                    (severity, on) => severity == IssueSeverity.Error
                        ? $"The \"{channel}\" encoding is null. {Throws(on)}"
                        : $"The \"{channel}\" encoding is null; {string.Join(" and ", on.Select(Name))} {(on.Length == 1 ? "ignores" : "ignore")} it.",
                    channel);
                continue;
            }

            UnsupportedChannel(channel, path, types, reasons);
            if (!value.IsSeries)
            {
                UnknownField(channel, path, value.Encodings[0]);
            }
        }

        if (HasRows)
        {
            foreach (var (b, list) in reasons)
            {
                foreach (var reason in list)
                {
                    AddNative(b, reason);
                }
            }
        }
    }

    /// <summary>Why a native backend throws on empty encodings, for <c>encodings_empty</c>.</summary>
    private static string Needs(FlintBackend b) =>
        b == FlintBackend.Excel ? "it needs a quantitative measure" : "it needs a category and a measure, or a slice and a value";

    private static string UnsupportedCode(FlintBackend b) => b == FlintBackend.Excel ? Codes.ExcelUnsupported : Codes.ImageChartsUnsupported;

    private void AddNative(FlintBackend b, ThrowReason reason) =>
        Add(IssueSeverity.Error, UnsupportedCode(b), reason.Channel is { } ch ? $"chart_spec.encodings.{ch}" : "chart_spec",
            $"{Name(b)} {reason.Message}; {Name(b)} throws.", reason.Channel);

    private void UnsupportedChannel(string channel, string path, FieldTypes types, Dictionary<FlintBackend, List<ThrowReason>> reasons)
    {
        if (targets.Length == 0)
        {
            return;
        }

        // A declared channel, or one a backend reads though its template leaves it out (ImageCharts.ReadsUndeclared, Excel.ReadsUndeclared).
        bool Reads(FlintBackend t) => template[t].Channels.Contains(channel)
            || (t == FlintBackend.ImageCharts && ImageCharts.ReadsUndeclared(input, types, channel))
            || (t == FlintBackend.Excel && Excel.ReadsUndeclared(input, types, channel, deferred is null));

        if (backend is { } b)
        {
            if (Reads(b) || (reasons.TryGetValue(b, out var own) && own.Any(r => r.Channel == channel)))
            {
                return;
            }

            if (!RetiredChannel([b], channel, path))
            {
                Add(IssueSeverity.Warning, Codes.UnsupportedChannel, path, $"The {Id(b)} template of \"{input.ChartSpec.ChartType.Name}\" does not declare \"{channel}\"; {Id(b)} ignores it.", channel);
            }

            return;
        }

        if (!targets.Any(Reads) && !RetiredChannel(targets, channel, path))
        {
            Add(IssueSeverity.Warning, Codes.UnsupportedChannel, path, $"No backend's template of \"{input.ChartSpec.ChartType.Name}\" declares \"{channel}\"; it is ignored.", channel);
        }
    }

    /// <summary>Reports <c>retired_member</c> when the ledger lists the channel as retired on one of the backends.</summary>
    private bool RetiredChannel(FlintBackend[] on, string channel, string path)
    {
        var retiredOn = on.Where(t => Retired(t, $"channels[{channel}]") is not null).ToArray();
        if (retiredOn.Length > 0)
        {
            Add(IssueSeverity.Warning, Codes.RetiredMember, path,
                $"flint-chart {Retired(retiredOn[0], $"channels[{channel}]")} removed the \"{channel}\" channel from the {string.Join(", ", retiredOn.Select(Id))} template of \"{input.ChartSpec.ChartType.Name}\"; it is ignored there.", channel);
        }

        return retiredOn.Length > 0;
    }

    private void UnknownField(string channel, string path, ChartEncoding encoding)
    {
        if (encoding.Field is not { } field || Keys is not { } keys || keys.Contains(field))
        {
            return;
        }

        var where = deferred is null ? "a key of the first row" : "a key the rows are written with";
        Add(IssueSeverity.Warning, Codes.UnknownField, $"{path}.field", $"\"{field}\" is not {where}; the encoding resolves to nothing.", channel, field);
    }

    private void DataChecks(Dictionary<FlintBackend, List<ThrowReason>> reasons)
    {
        if (input.Data is not { } data || targets.Length == 0 || HasRows)
        {
            return;
        }

        var urlOnly = data.Url is not null && data.Values is null;
        var code = urlOnly ? Codes.DataUrlOnly : Codes.DataEmpty;
        foreach (var b in targets.Where(NativeBackends.Contains))
        {
            var own = reasons.GetValueOrDefault(b) ?? [];
            foreach (var reason in own.Where(r => !r.FromRowsOrTypes))
            {
                AddNative(b, reason);
            }

            var because = own.FirstOrDefault(r => r.FromRowsOrTypes)?.Message;
            var name = Name(b);
            Add(because is not null ? IssueSeverity.Error : IssueSeverity.Info, code, urlOnly ? "data.url" : "data.values",
                because is not null
                    ? $"{name} reads only data.values and has no rows: it {because}; {name} throws."
                    : urlOnly ? $"{name} reads only data.values; the URL is not fetched and the chart has no rows."
                    : own.Count > 0 ? $"data.values has no rows, but {name} throws whatever the rows ({UnsupportedCode(b)})."
                    : $"data.values has no rows; {name} assembles an empty chart.");
        }

        var others = targets.Where(t => !NativeBackends.Contains(t)).ToArray();
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
        if (deferred is not null)
        {
            return;
        }

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
                return (Codes.TimespanAsString, $"\"{field}\" holds a duration string (\"{v.GetValue<string>()}\"), which flint-chart {FlintUpstream.Version} infers as nominal, a category for each value; write the duration as a number.");
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

    /// <summary>
    /// A <c>TimeSpan</c> of a day or more (<c>"1.02:03:04"</c>), which flint-chart 1.0.0 infers as nominal; shorter ones look
    /// like a time of day, which may be one, and are not reported (probe <c>valueInference</c>,
    /// tests/fixtures/flint-chart@1.0.0/probes.json).
    /// </summary>
    [GeneratedRegex(@"^-?\d+\.\d{2}:\d{2}:\d{2}(\.\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex DayTimeSpan();

    private void SemanticTypeChecks()
    {
        if (input.SemanticTypes is null)
        {
            return;
        }

        var keys = Keys;
        // Excel and Image-Charts read a static series as an unbound channel and never look up the annotation of its entries;
        // the layout backends do (normalizeStaticSeries). An empty target set (template unresolved) keeps every entry.
        var nativeOnly = targets.Length > 0 && targets.All(NativeBackends.Contains);
        var bound = (input.ChartSpec?.Encodings ?? []).Values.Where(v => v is not null && !(nativeOnly && v.IsSeries))
            .SelectMany(v => v!.Encodings).Select(e => e.Field).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var (field, annotation) in input.SemanticTypes)
        {
            if (annotation?.Type.Name is { } reserved && Js.ObjectPrototypeKeys.Contains(reserved) && (bound.Contains(field) || keys?.Contains(field) == true))
            {
                Add(IssueSeverity.Error, Codes.ReservedSemanticType, $"semantic_types.{field}",
                    $"\"{reserved}\" is a member of JavaScript's Object.prototype; upstream's registry lookup counts it as registered and finds no entry. {ThrowsOnTargets()}",
                    field: field);
                continue;
            }

            if (keys is not null && !keys.Contains(field))
            {
                Add(IssueSeverity.Info, Codes.SemanticTypeWithoutField, $"semantic_types.{field}",
                    $"\"{field}\" is not {(deferred is null ? "a key of the first row" : "a key the rows are written with")}; upstream never reads this annotation.", field: field);
            }

            if (annotation?.Type is { Name: { } retired } && CatalogTables.Retired.GetValueOrDefault($"catalog.semanticTypes[{retired}]") is { } version)
            {
                Add(IssueSeverity.Warning, Codes.RetiredMember, $"semantic_types.{field}",
                    $"flint-chart {version} removed the semantic type \"{retired}\"; the type is inferred from the data, as without an annotation.",
                    field: field);
            }
            else if (annotation?.Type is { Name: { } name } type && !type.IsRegistered)
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
            // The ledger keys an option by its JSON text (generator/src/diff.ts).
            var option = $"properties[{key}].options[={value.ToJsonString(RelaxedJson)}]";
            var optionRetiredOn = invalidOn.Where(t => Retired(t, option) is not null).ToArray();
            if (optionRetiredOn.Length > 0)
            {
                Add(IssueSeverity.Warning, Codes.RetiredMember, path,
                    $"flint-chart {Retired(optionRetiredOn[0], option)} removed the option {value.ToJsonString()} of \"{key}\" on {string.Join(", ", optionRetiredOn.Select(Id))}; upstream drops it (invalid-option-value) and uses the default.");
                invalidOn = [.. invalidOn.Except(optionRetiredOn)];
            }

            if (invalidOn.Length > 0)
            {
                Add(IssueSeverity.Warning, Codes.InvalidOptionValue, path,
                    $"{value.ToJsonString()} is not an option of \"{key}\" on {string.Join(", ", invalidOn.Select(Id))}; upstream drops it (invalid-option-value) and uses the default.");
            }

            var undeclaredOn = targets.Where(t => !template[t].Properties.ContainsKey(key)).ToArray();
            if (!CatalogTables.HostStateKeys.Contains(key) && undeclaredOn.Length > 0 && (backend is not null || undeclaredOn.Length == targets.Length))
            {
                var retiredOn = undeclaredOn.Where(t => Retired(t, $"properties[{key}]") is not null).ToArray();
                if (retiredOn.Length > 0)
                {
                    Add(IssueSeverity.Warning, Codes.RetiredMember, path,
                        $"flint-chart {Retired(retiredOn[0], $"properties[{key}]")} removed \"{key}\" from the {string.Join(", ", retiredOn.Select(Id))} template of \"{input.ChartSpec.ChartType.Name}\"; it is ignored there.");
                }
                else
                {
                    Add(IssueSeverity.Info, Codes.UnknownChartProperty, path,
                        $"The {string.Join(", ", undeclaredOn.Select(Id))} template of \"{input.ChartSpec.ChartType.Name}\" does not declare \"{key}\"; it is passed through and may have no effect.");
                }
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

    /// <summary>JSON text as <c>JSON.stringify</c> writes a string: only quotes, backslashes and control characters escaped.</summary>
    private static readonly JsonSerializerOptions RelaxedJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The flint-chart version that retired a member of the template on a backend (<c>generator/retired.json</c>),
    /// such as <c>properties[key]</c>; null when the ledger does not list it.</summary>
    private string? Retired(FlintBackend b, string member) =>
        CatalogTables.Retired.GetValueOrDefault($"catalog.chartTypes[{input.ChartSpec.ChartType.Name}].backends.{Id(b)}.{member}");

    /// <summary><c>normalizeChartProperties</c>: a value equal to an option value under <c>sameOptionValue</c> (===, or for
    /// two arrays the same length with every entry ===), or a string equal to an option label ignoring case and surrounding
    /// whitespace.</summary>
    private static bool Accepts(PropertyRow property, JsonNode value)
    {
        foreach (var option in property.Options ?? [])
        {
            if (option is not null && SameOptionValue(JsonNode.Parse(option), value))
            {
                return true;
            }
        }

        return Js.Kind(value) == JsonValueKind.String && property.Labels is { } labels
            && labels.Any(l => string.Equals(l.ToLowerInvariant(), Js.Trim(value.GetValue<string>()).ToLowerInvariant(), StringComparison.Ordinal));
    }

    private static bool SameOptionValue(JsonNode? option, JsonNode value) =>
        Js.Kind(option) == JsonValueKind.Array && Js.Kind(value) == JsonValueKind.Array
            ? option!.AsArray().Count == value.AsArray().Count && option.AsArray().Select((entry, i) => StrictEquals(entry, value[i])).All(e => e)
            : StrictEquals(option, value);

    /// <summary>JavaScript <c>===</c> on JSON values: equal strings, equal numbers, the same boolean, both null; an object or an
    /// array parsed from JSON is never === to another.</summary>
    private static bool StrictEquals(JsonNode? a, JsonNode? b)
    {
        var kind = Js.Kind(a);
        return kind == Js.Kind(b) && kind switch
        {
            JsonValueKind.String => a!.GetValue<string>() == b!.GetValue<string>(),
            JsonValueKind.Number => Js.NumberOf(a!) == Js.NumberOf(b!),
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            _ => false,
        };
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

    /// <summary>
    /// <c>repairEncodingSorts</c> of the pinned package (src/core/resolve-semantics.ts), which the four layout backends run
    /// first: each <c>sortBy</c> and <c>sortOrder</c> that <c>inspectEncodingSort</c> rejects is dropped with the warning
    /// <c>invalid_sort</c>, and the chart keeps its default order. Vega-Lite also sorts by a data field; ECharts, Chart.js
    /// and Plotly reject one (<c>fieldSort: false</c>). Excel and Image-Charts read no sort hint.
    /// </summary>
    private void SortByChecks(ChartSpec spec)
    {
        var layout = targets.Where(LayoutBackends.Contains).ToArray();
        if (layout.Length == 0)
        {
            return;
        }

        // The data fields upstream knows: the keys of every object row; null when the rows are not read and the row type
        // has no static shape.
        IReadOnlySet<string>? fields = deferred is not null
            ? deferred.Keys?.ToHashSet(StringComparer.Ordinal)
            : (input.Data?.Values ?? []).OfType<JsonObject>().SelectMany(r => r.Select(p => p.Key)).ToHashSet(StringComparer.Ordinal);
        foreach (var (channel, value) in spec.Encodings ?? [])
        {
            for (var i = 0; i < (value?.Encodings.Count ?? 0); i++)
            {
                var e = value!.Encodings[i];
                var at = value.IsSeries ? $"chart_spec.encodings.{channel}[{i}]" : $"chart_spec.encodings.{channel}";
                if (e.SortOrder?.Name is { } order && order is not ("ascending" or "descending"))
                {
                    PerTarget(Codes.SortByInvalid, $"{at}.sortOrder", b => LayoutBackends.Contains(b) ? IssueSeverity.Warning : null,
                        (_, on) => $"sortOrder \"{order}\" is neither ascending nor descending; {string.Join(", ", on.Select(Id))} ignore it (invalid_sort).", channel);
                }

                if (e.SortBy is not { } sortBy)
                {
                    continue;
                }

                var reasons = layout.ToDictionary(b => b, b => SortByReason(spec, sortBy, fields, fieldSort: b == FlintBackend.VegaLite));
                PerTarget(Codes.SortByInvalid, $"{at}.sortBy", b => reasons.TryGetValue(b, out var r) && r is { Length: > 0 } ? IssueSeverity.Warning : null,
                    (_, on) => reasons[on[0]]!.StartsWith("contains ", StringComparison.Ordinal) && !reasons[on[0]]!.StartsWith("contains no", StringComparison.Ordinal)
                        ? $"sortBy \"{sortBy}\" {reasons[on[0]]}; {string.Join(", ", on.Select(Id))} ignore them (invalid_sort)."
                        : $"sortBy \"{sortBy}\" {reasons[on[0]]}; {string.Join(", ", on.Select(Id))} drop it, and its sortOrder, and use the default order (invalid_sort).",
                    channel);
            }
        }
    }

    /// <summary>
    /// Why <c>inspectEncodingSort</c> rejects <paramref name="sortBy"/>, or <c>""</c> when it keeps it; null when that depends
    /// on data fields the validator does not know.
    /// </summary>
    private static string? SortByReason(ChartSpec spec, string sortBy, IReadOnlySet<string>? fields, bool fieldSort)
    {
        if (Js.Trim(sortBy).Length == 0)
        {
            return "is not a data field, mapped channel, or JSON category array";
        }

        if (sortBy is "x" or "y" or "color")
        {
            var mapped = spec.Encodings is { } encodings && encodings.TryGetValue(sortBy, out var binding) && binding is not null
                && binding.Encodings.Any(t => t.Field is not null || t.Aggregate?.Name == "count");
            return mapped ? string.Empty : "names a channel that is not mapped";
        }

        if (fields is null && fieldSort)
        {
            return null;
        }

        if (fields?.Contains(sortBy) == true)
        {
            return fieldSort ? string.Empty : "sorts by a raw data field, which this backend does not support";
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(sortBy);
        }
        catch (JsonException)
        {
            return "is not a data field";
        }

        if (parsed is not JsonArray values)
        {
            return "is not a data field or JSON category array";
        }

        var valid = values.Count(IsCategoryValue);
        return valid == values.Count ? string.Empty
            : valid == 0 ? "contains no valid category values"
            : $"contains {values.Count - valid} invalid category value(s)";
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
        value is not null && value.Encodings.Any(IsBound);

    /// <summary>An encoding entry with a non-blank field or a count aggregate.</summary>
    internal static bool IsBound(ChartEncoding e) =>
        (e.Field is { } f && Js.Trim(f).Length > 0) || e.Aggregate?.Name == "count";

    /// <summary>Upstream <c>isCategoryValue</c>: a string, a boolean or a finite number.</summary>
    internal static bool IsCategoryValue(JsonNode? node) =>
        Js.Kind(node) is JsonValueKind.String or JsonValueKind.True or JsonValueKind.False
        || (Js.Kind(node) == JsonValueKind.Number && double.IsFinite(Js.NumberOf(node!)));

    /// <summary>The layout backends overwrite the options in the catalog's <c>ignoredOptions</c>; the native backends (Excel,
    /// Image-Charts) read no option.</summary>
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
                b => NativeBackends.Contains(b) || (CatalogTables.IgnoredOptions.TryGetValue(key, out var on) && on.Contains(b)) ? IssueSeverity.Info : null,
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
            // An empty encoding key is read as given, but a Channel cannot be empty.
            Channel = string.IsNullOrEmpty(channel) ? null : new Channel(channel),
            Field = field,
        });
}
