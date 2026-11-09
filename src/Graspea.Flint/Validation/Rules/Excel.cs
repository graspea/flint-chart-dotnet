using System.Text.Json;
using System.Text.Json.Nodes;
using static Graspea.Flint.Validation.Rules.Tri;

namespace Graspea.Flint.Validation.Rules;

/// <summary>One reason the Excel or the Image-Charts assembler throws. <see cref="FromRowsOrTypes"/> is false for reasons
/// that follow from the encodings and chart properties alone (they hold whatever the rows are).</summary>
internal sealed record ThrowReason(string Message, string? Channel, bool FromRowsOrTypes);

/// <summary>
/// The throw conditions of <c>assembleExcel</c> in the pinned package (design §6 <c>excel_unsupported</c>): faceting,
/// static series, each template's <c>validate()</c>, the category/measure resolution of the shared path, and the two
/// throws inside <c>instantiate()</c> (histogram values, radar metrics). Hand-written from src/excel/assemble.ts and
/// src/excel/templates/*.ts of flint-chart 1.0.0 and pinned to the recorded fixture outcomes. Row values are read as
/// given; upstream reads them after its temporal conversion, which changes no value these checks read.
/// A rule that reads a field's type is reported without the rows when the spec decides that type for every channel the
/// rule reads (FieldTypes.SpecDecided; FieldTypes.ContinuityDecided for the rules that reject a continuous field); a rule
/// that reads row values is not.
/// </summary>
internal static class Excel
{
    private static readonly HashSet<string> SharedPathTemplates = new(StringComparer.Ordinal)
    {
        "Area Chart", "Bar Chart", "Grouped Bar Chart", "Stacked Bar Chart", "Line Chart", "Scatter Plot", "Connected Scatter Plot", "Pie Chart", "Donut Chart",
    };

    private static readonly string[] PriceChannels = ["open", "high", "low", "close"];

    internal static List<ThrowReason> Evaluate(ChartAssemblyInput input, FieldTypes types, TemplateRow template)
    {
        var reasons = new List<ThrowReason>();
        var chartType = input.ChartSpec.ChartType.Name;
        var encodings = input.ChartSpec.Encodings;
        var rows = types.Rows;
        string? F(string channel) => types.FieldOf(channel);
        FieldType T(string channel) => types.Of(channel);

        void Add(bool? condition, string message, string? channel = null, bool fromRowsOrTypes = true)
        {
            if (condition == true)
            {
                reasons.Add(new ThrowReason(message, channel, fromRowsOrTypes));
            }
        }

        if (!template.SupportsFacets)
        {
            foreach (var channel in new[] { "column", "row" })
            {
                Add(F(channel) is not null, "does not support faceting in one native chart", channel, false);
            }
        }

        bool? NonNegativeViolated(string? field) =>
            field is not null && rows.Any(r => Js.Get(r, field, out var p) is var v && Js.IsFiniteNumber(v, p) && Js.ToNumber(v, p) < 0);

        switch (chartType)
        {
            case "Area Chart":
                Add(NotQuantitative(T("y")), "requires a quantitative y field for a native Excel area chart", "y", !types.SpecDecided("y"));
                break;
            case "Bar Chart":
                Add(And(NotQuantitative(T("x")), NotQuantitative(T("y"))), "requires one quantitative measure axis for a native Excel bar chart", null, !(types.SpecDecided("x") && types.SpecDecided("y")));
                break;
            case "Grouped Bar Chart":
                Add(And(NotQuantitative(T("x")), NotQuantitative(T("y"))), "requires one quantitative measure axis for a native Excel bar chart", null, !(types.SpecDecided("x") && types.SpecDecided("y")));
                if (GroupedSeriesChannel(input) is { } series)
                {
                    Add(IsContinuous(T(series)), "does not support continuous grouping in a native Excel grouped bar chart", series, !types.ContinuityDecided(series));
                }
                break;
            case "Stacked Bar Chart":
                Add(And(NotQuantitative(T("x")), NotQuantitative(T("y"))), "requires one quantitative measure axis for a native Excel bar chart", null, !(types.SpecDecided("x") && types.SpecDecided("y")));
                Add(IsContinuous(T("color")), "does not support continuous color in a native Excel stacked bar chart", "color", !types.ContinuityDecided("color"));
                break;
            case "Line Chart":
                Add(NotQuantitative(T("y")), "requires a quantitative y field for a native Excel line chart", "y", !types.SpecDecided("y"));
                Add(IsContinuous(T("color")), "does not support continuous color on a native Excel line chart", "color", !types.ContinuityDecided("color"));
                break;
            case "Scatter Plot":
                foreach (var axis in new[] { "x", "y" })
                {
                    Add(NotQuantitative(T(axis)), "requires quantitative x and y fields for a native Excel XY chart", axis, !types.SpecDecided(axis));
                }
                Add(F("shape") is not null, "does not support a shape encoding in native Excel charts", "shape", false);
                Add(IsContinuous(T("color")), "does not support continuous color in a native Excel Scatter or Bubble chart", "color", !types.ContinuityDecided("color"));
                break;
            case "Connected Scatter Plot":
                foreach (var axis in new[] { "x", "y" })
                {
                    Add(NotQuantitative(T(axis)), "requires quantitative x and y fields for a native Excel XY chart", axis, !types.SpecDecided(axis));
                }
                Add(F("shape") is not null, "does not support a shape encoding in native Excel charts", "shape", false);
                Add(F("order") is null, "requires an explicit order field for a native Excel connected scatter chart", "order", false);
                Add(F("detail") is not null, "does not support an unlegended detail series in a native Excel connected scatter chart", "detail", false);
                Add(IsContinuous(T("color")), "does not support continuous color in a native Excel connected scatter chart", "color", !types.ContinuityDecided("color"));
                break;
            case "Boxplot":
                Add(F("x") is null, "requires a categorical x field for a native Excel box and whisker chart", "x", false);
                Add(IsTemporal(T("x")), "requires a categorical x field for a native Excel box and whisker chart", "x", !types.SpecDecided("x"));
                Add(F("y") is null, "requires a quantitative y field for a native Excel box and whisker chart", "y", false);
                Add(NotQuantitative(T("y")), "requires a quantitative y field for a native Excel box and whisker chart", "y", !types.SpecDecided("y"));
                Add(F("color") is not null, "does not yet support color-grouped native Excel box and whisker charts", "color", false);
                Add(Property(input, "whiskerMethod") == "minmax", "does not support min-max whiskers", null, false);
                break;
            case "Candlestick Chart":
                Candlestick(input, types, Add);
                break;
            case "Funnel Chart":
                Add(F("y") is null, "requires a categorical y stage field for a native Excel funnel chart", "y", false);
                Add(IsQuantitative(T("y")), "requires a categorical y stage field for a native Excel funnel chart", "y", !types.SpecDecided("y"));
                Add(NotQuantitative(T("size")), "requires a quantitative size field for a native Excel funnel chart", "size", !types.SpecDecided("size"));
                Add(NonNegativeViolated(F("size")), "requires non-negative values for a native Excel funnel chart", "size");
                break;
            case "Histogram":
                var valueChannel = F("x") is not null ? "x" : "y";
                Add(F(valueChannel) is null, "requires a quantitative value field for a native Excel histogram", valueChannel, false);
                Add(NotQuantitative(T(valueChannel)), "requires a quantitative value field for a native Excel histogram", valueChannel, !types.SpecDecided(valueChannel));
                // instantiate() reads fieldOf('x') ?? fieldOf('y') (histogram.ts:25): an empty x field, which validate() skips,
                // is the value field there, read as the rows' "" key.
                var (readChannel, readField) = types.Encoding("x")?.Field is { } rawX ? ("x", rawX) : ("y", F("y"));
                Add(F(valueChannel) is not null && readField is { } histogramField && !rows.Any(r => Js.Get(r, histogramField, out var p) is var v && Js.IsFiniteNumber(v, p)),
                    readField is "" ? "requires finite numeric values for a histogram, and reads the empty x field as the value field (the rows' \"\" key)" : "requires finite numeric values for a histogram",
                    readChannel);
                break;
            case "Pyramid Chart":
                Pyramid(types, rows, Add, NonNegativeViolated);
                break;
            case "Radar Chart":
                Add(F("x") is null, "requires a categorical metric x field for a native Excel radar chart", "x", false);
                Add(Or(IsQuantitative(T("x")), IsTemporal(T("x"))), "requires a categorical metric x field for a native Excel radar chart", "x", !types.ContinuityDecided("x"));
                Add(F("y") is null, "requires a quantitative y field for a native Excel radar chart", "y", false);
                Add(NotQuantitative(T("y")), "requires a quantitative y field for a native Excel radar chart", "y", !types.SpecDecided("y"));
                Add(F("color") is not null ? IsContinuous(T("color")) : false, "requires a discrete color field for native Excel radar series", "color", !types.ContinuityDecided("color"));
                Add(F("y") is { } radarValue && rows.Any(r => Js.ToNumber(Js.Get(r, radarValue, out var p), p) < 0), "requires non-negative values for a native Excel radar chart", "y");
                Add(F("x") is { } metric && rows.Select(r => Js.ToJsString(Js.Get(r, metric, out var p), p)).Distinct(StringComparer.Ordinal).Count() < 3,
                    "requires at least three metric axes for a radar chart", "x");
                break;
            case "Sunburst Chart":
                Add(F("color") is null, "requires a categorical color field for a native Excel sunburst chart", "color", false);
                Add(IsQuantitative(T("color")), "requires a categorical color field for a native Excel sunburst chart", "color", !types.SpecDecided("color"));
                Add(NotQuantitative(T("size")), "requires a quantitative size field for a native Excel sunburst chart", "size", !types.SpecDecided("size"));
                Add(F("group") is not null ? IsQuantitative(T("group")) : false, "requires a categorical group field for a native Excel sunburst hierarchy", "group", !types.SpecDecided("group"));
                Add(F("detail") is not null && F("group") is null, "requires a group field before detail in a native Excel sunburst hierarchy", "detail", false);
                Add(F("detail") is not null ? IsQuantitative(T("detail")) : false, "requires a categorical detail field for a native Excel sunburst hierarchy", "detail", !types.SpecDecided("detail"));
                Add(NonNegativeViolated(F("size")), "requires non-negative values for a native Excel sunburst chart", "size");
                break;
            case "Treemap":
                Add(F("color") is null, "requires a categorical color field for a native Excel treemap", "color", false);
                Add(IsQuantitative(T("color")), "requires a categorical color field for a native Excel treemap", "color", !types.SpecDecided("color"));
                Add(NotQuantitative(T("size")), "requires a quantitative size field for a native Excel treemap", "size", !types.SpecDecided("size"));
                Add(F("detail") is not null ? IsQuantitative(T("detail")) : false, "requires a categorical detail field for a native Excel treemap hierarchy", "detail", !types.SpecDecided("detail"));
                Add(NonNegativeViolated(F("size")), "requires non-negative values for a native Excel treemap", "size");
                break;
            case "Waterfall Chart":
                Waterfall(input, types, rows, Add);
                break;
        }

        if (SharedPathTemplates.Contains(chartType))
        {
            // The shared path needs a category and a measure field: color/x and size/theta/y without axes, else x and y.
            if (chartType is "Pie Chart" or "Donut Chart")
            {
                bool Bound(string ch) => encodings.TryGetValue(ch, out var v) && v is not null;
                var category = Bound("color") ? "color" : Bound("x") ? "x" : null;
                var measure = Bound("size") ? "size" : Bound("theta") ? "theta" : Bound("y") ? "y" : null;
                Add(category is null || F(category) is null || measure is null || F(measure) is null, "could not resolve the category or the measure field", null, false);
            }
            else
            {
                Add(F("x") is null || F("y") is null, "could not resolve the category or the measure field", null, false);
            }
        }

        return reasons;
    }

    /// <summary>
    /// The channel <c>assembleExcel</c> reads as the series of a Grouped Bar Chart: <c>normalizeChartEncodingAliases</c>
    /// (src/core/static-series.ts) moves <c>color</c> to <c>group</c> when <c>group</c> is not bound, so <c>group</c> while
    /// it is bound (an encoding of any shape, an empty field and a static series included, as the objects the normalized
    /// encodings hold are all truthy), else <c>color</c> while that is bound, else none. Null on any other chart type.
    /// </summary>
    internal static string? GroupedSeriesChannel(ChartAssemblyInput input)
    {
        if (input.ChartSpec.ChartType.Name != "Grouped Bar Chart")
        {
            return null;
        }

        bool Bound(string ch) => input.ChartSpec.Encodings.TryGetValue(ch, out var v) && v is not null;
        return Bound("group") ? "group" : Bound("color") ? "color" : null;
    }

    /// <summary>
    /// Whether Excel reads <paramref name="channel"/> though its template does not declare it (flint-chart 1.0.1
    /// src/excel/assemble.ts, probes.json <c>channelReads</c>). On the shared path: <c>color</c> of a Grouped Bar Chart with
    /// no <c>group</c> bound and a <c>color</c> with a field, the group (<see cref="GroupedSeriesChannel"/>); off Pie and Donut, <c>group</c> with a field,
    /// the series, or without a field beside a <c>color</c> with a field that is not quantitative, a categorical series it displaces (:270-275); <c>strokeDash</c> with a
    /// field (:293), which splits the series (:403-448), and on a Scatter or Connected Scatter Plot without a series field
    /// only shows the legend (:587-591); <c>size</c> and <c>order</c> with a field on the XY path (:294-295, :320-377): a
    /// Connected Scatter Plot reads <c>size</c>, a Scatter Plot <c>order</c>, and a Line Chart both when <c>x</c> and
    /// <c>y</c> are quantitative (:309-314), assumed while the rows that decide it are not known; on a Pie or Donut Chart
    /// <c>x</c> while no <c>color</c> is bound (the slice) and <c>y</c> while neither <c>size</c> nor <c>theta</c> is (the
    /// value, :277-281). A Histogram reads <c>y</c> as the value without an <c>x</c> field (templates/histogram.ts:19, :25).
    /// Bound is any non-null value (the normalized encodings are truthy objects; a Grouped Bar Chart <c>color</c> also needs a field); a field is a non-empty one of a single
    /// encoding. <c>CatalogCheck</c> of Graspea.Flint.CSharp mirrors this for a template.
    /// </summary>
    internal static bool ReadsUndeclared(ChartAssemblyInput input, FieldTypes types, string channel, bool rowsKnown)
    {
        var chartType = input.ChartSpec.ChartType.Name;
        if (!SharedPathTemplates.Contains(chartType))
        {
            return chartType == "Histogram" && channel == "y" && types.FieldOf("x") is null;
        }

        bool Bound(string ch) => input.ChartSpec.Encodings.TryGetValue(ch, out var v) && v is not null;
        bool? Measure(string axis) => rowsKnown || types.SpecDecided(axis) ? IsQuantitative(types.Of(axis)) : null;
        bool NumericLine() => chartType == "Line Chart" && And(Measure("x"), Measure("y")) != false;
        var pie = chartType is "Pie Chart" or "Donut Chart";
        return channel switch
        {
            "color" => GroupedSeriesChannel(input) == "color" && types.FieldOf("color") is not null,
            "group" => !pie && (types.FieldOf("group") is not null || (types.FieldOf("color") is not null && Measure("color") != true)),
            "strokeDash" => types.FieldOf("strokeDash") is not null
                && (chartType is not ("Scatter Plot" or "Connected Scatter Plot") || (Bound("group") ? types.FieldOf("group") : types.FieldOf("color")) is null),
            "size" => types.FieldOf("size") is not null && (chartType == "Connected Scatter Plot" || NumericLine()),
            "order" => types.FieldOf("order") is not null && (chartType == "Scatter Plot" || NumericLine()),
            "x" => pie && !Bound("color"),
            "y" => pie && !Bound("size") && !Bound("theta"),
            _ => false,
        };
    }

    private static string? Property(ChartAssemblyInput input, string key) =>
        input.ChartSpec.ChartProperties is { } p && p.TryGetValue(key, out var v) && Js.Kind(v) == JsonValueKind.String ? v!.GetValue<string>() : null;

    private static void Candlestick(ChartAssemblyInput input, FieldTypes types, Action<bool?, string, string?, bool> add)
    {
        var x = types.FieldOf("x");
        var xType = types.Of("x");
        add(x is null, "requires a temporal or ordered x field for a native Excel stock chart", "x", false);
        add(xType == FieldType.Unknown ? null : xType is not (FieldType.Temporal or FieldType.Ordinal or FieldType.TemporalOrOrdinal), "requires a temporal or ordered x field for a native Excel stock chart", "x", !types.SpecDecided("x"));
        foreach (var channel in PriceChannels)
        {
            add(types.FieldOf(channel) is null, $"requires a quantitative {channel} field for a native Excel stock chart", channel, false);
            add(NotQuantitative(types.Of(channel)), $"requires a quantitative {channel} field for a native Excel stock chart", channel, !types.SpecDecided(channel));
        }

        // One reason per bound channel (candlestick.ts:31-33 throws on any of them), so that none is also unsupported_channel.
        foreach (var channel in new[] { "color", "group", "detail" })
        {
            add(types.FieldOf(channel) is not null, "does not support color, group, or detail encodings in a native Excel stock chart", channel, false);
        }
        if (x is null || PriceChannels.Any(c => types.FieldOf(c) is null))
        {
            return;
        }

        add(types.Rows.Count == 0, "requires at least one OHLC row for a native Excel stock chart", null, true);
        var previous = double.NegativeInfinity;
        for (var i = 0; i < types.Rows.Count; i++)
        {
            var row = types.Rows[i];
            // new Date(v).getTime(): unknown as soon as a value is a string V8's ISO parser is not known to read.
            if (DateTime(Js.Get(row, x, out var present), present) is not { } time)
            {
                return;
            }

            if (double.IsNaN(time))
            {
                add(true, $"requires a valid date at row {i + 1} for a native Excel stock chart", "x", true);
                return;
            }

            if (time <= previous)
            {
                add(true, "requires x values sorted in strictly increasing chronological order for a native Excel stock chart", "x", true);
                return;
            }

            previous = time;
            var prices = PriceChannels.Select(c => (Value: Js.Get(row, types.FieldOf(c)!, out var p), Present: p)).ToArray();
            var numbers = prices.Select(p => Js.ToNumber(p.Value, p.Present)).ToArray();
            if (prices.Any(p => Js.IsNullish(p.Value, p.Present) || (Js.Kind(p.Value) == JsonValueKind.String && p.Value!.GetValue<string>().Length == 0))
                || !numbers.All(double.IsFinite))
            {
                add(true, $"requires finite open, high, low, and close values at row {i + 1} for a native Excel stock chart", null, true);
                return;
            }

            var (open, high, low, close) = (numbers[0], numbers[1], numbers[2], numbers[3]);
            if (high < Math.Max(open, Math.Max(low, close)) || low > Math.Min(open, Math.Min(high, close)))
            {
                add(true, $"requires coherent OHLC values at row {i + 1}", null, true);
                return;
            }
        }
    }

    /// <summary><c>new Date(v).getTime()</c>: NaN when invalid, null when only V8 could tell.</summary>
    private static double? DateTime(JsonNode? value, bool present)
    {
        if (!present)
        {
            return double.NaN;
        }

        switch (Js.Kind(value))
        {
            case JsonValueKind.Null:
            case JsonValueKind.False:
                return 0;
            case JsonValueKind.True:
                return 1;
            case JsonValueKind.Number:
                var n = Js.NumberOf(value!);
                return Math.Abs(n) <= 8.64e15 ? Math.Truncate(n) : double.NaN;
            case JsonValueKind.String:
                var s = value!.GetValue<string>();
                return Js.IsoTime(s) ?? (Js.DateParses(s) == false ? double.NaN : null);
            default:
                return null;
        }
    }

    private static void Pyramid(FieldTypes types, IReadOnlyList<JsonNode?> rows, Action<bool?, string, string?, bool> add, Func<string?, bool?> negative)
    {
        var xMeasure = IsQuantitative(types.Of("x"));
        var yMeasure = IsQuantitative(types.Of("y"));
        if (xMeasure is null || yMeasure is null)
        {
            return;
        }

        if (xMeasure == yMeasure)
        {
            add(true, "requires exactly one quantitative measure axis for a native Excel pyramid chart", null, !(types.SpecDecided("x") && types.SpecDecided("y")));
            return;
        }

        var category = xMeasure == true ? "y" : "x";
        add(types.FieldOf(category) is null, "requires a discrete category axis for a native Excel pyramid chart", category, false);
        add(IsQuantitative(types.Of(category)), "requires a discrete category axis for a native Excel pyramid chart", category, true);
        var group = types.FieldOf("color");
        add(group is null, "requires a discrete color field for a native Excel pyramid chart", "color", false);
        add(IsContinuous(types.Of("color")), "requires a discrete color field for a native Excel pyramid chart", "color", !types.ContinuityDecided("color"));
        if (group is not null)
        {
            var groups = rows.Select(r => Js.Get(r, group, out var p) is var v && !Js.IsNullish(v, p) ? Js.SetKey(v, p) : null)
                .Where(k => k is not null).Distinct().Count();
            add(groups != 2, $"requires exactly two groups for a native Excel pyramid chart (found {groups})", "color", true);
        }

        add(negative(types.FieldOf(xMeasure == true ? "x" : "y")), "requires non-negative values for a native Excel pyramid chart", null, true);
    }

    private static void Waterfall(ChartAssemblyInput input, FieldTypes types, IReadOnlyList<JsonNode?> rows, Action<bool?, string, string?, bool> add)
    {
        add(types.FieldOf("x") is null, "requires a category x field for a native Excel waterfall chart", "x", false);
        add(NotQuantitative(types.Of("y")), "requires a quantitative y field for a native Excel waterfall chart", "y", !types.SpecDecided("y"));
        var roles = types.FieldOf("color");
        add(roles is not null ? IsQuantitative(types.Of("color")) : false, "requires color to contain start/delta/end roles for a native Excel waterfall chart", "color", !types.SpecDecided("color"));
        var valueField = types.FieldOf("y");
        var values = valueField is null
            ? []
            : rows.Select(r => Js.ToNumber(Js.Get(r, valueField, out var p), p)).Where(double.IsFinite).ToArray();
        if (roles is not null)
        {
            var nonInitialTotal = rows.Skip(1).Any(r =>
            {
                var v = Js.Get(r, roles, out var p);
                var role = (Js.IsNullish(v, p) ? string.Empty : Js.ToJsString(v)).ToLowerInvariant();
                return role is "start" or "end" or "total";
            });
            add(nonInitialTotal, "cannot mark non-initial total points because Office.js does not expose Waterfall total-point semantics", "color", true);
        }
        else
        {
            var explicitMode = Property(input, "totals") is "none" or "first" or "last" or "both" ? Property(input, "totals") : null;
            var mode = explicitMode ?? (LastReconciles(values) ? "both" : "first");
            // An explicit mode is used as given (resolveTotalsMode, src/chart-types/waterfall.ts:51-56): no row is read.
            add(mode is "last" or "both", "cannot mark a final total because Office.js does not expose Waterfall total-point semantics", null, explicitMode is null);
        }
    }

    /// <summary><c>waterfallLastReconciles</c> (src/chart-types/waterfall.ts): the last value equals the sum of the others.</summary>
    private static bool LastReconciles(double[] values)
    {
        if (values.Length < 2)
        {
            return false;
        }

        var sum = values[..^1].Sum();
        return Math.Abs(values[^1] - sum) <= Math.Max(1e-6, 5e-3 * Math.Abs(sum));
    }
}
