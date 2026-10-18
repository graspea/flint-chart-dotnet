using System.Text.Json;
using System.Text.Json.Nodes;
using static Graspea.Flint.Validation.Rules.Tri;

namespace Graspea.Flint.Validation.Rules;

/// <summary>One reason the Excel assembler throws. <see cref="FromRowsOrTypes"/> is false for reasons that follow from the
/// encodings and chart properties alone (they hold whatever the rows are).</summary>
internal sealed record ExcelReason(string Message, string? Channel, bool FromRowsOrTypes);

/// <summary>
/// The throw conditions of <c>assembleExcel</c> in the pinned package (design §6 <c>excel_unsupported</c>): faceting,
/// static series, each template's <c>validate()</c>, the category/measure resolution of the shared path, and the two
/// throws inside <c>instantiate()</c> (histogram values, radar metrics). Hand-written from src/excel/assemble.ts and
/// src/excel/templates/*.ts of flint-chart 0.5.1 and pinned to the recorded fixture outcomes. Row values are read as
/// given; upstream reads them after its temporal conversion, which changes no value these checks read.
/// </summary>
internal static class Excel
{
    private static readonly HashSet<string> SharedPathTemplates = new(StringComparer.Ordinal)
    {
        "Area Chart", "Bar Chart", "Grouped Bar Chart", "Stacked Bar Chart", "Line Chart", "Scatter Plot", "Connected Scatter Plot", "Pie Chart", "Donut Chart",
    };

    private static readonly string[] PriceChannels = ["open", "high", "low", "close"];

    internal static List<ExcelReason> Evaluate(ChartAssemblyInput input, FieldTypes types, TemplateRow template)
    {
        var reasons = new List<ExcelReason>();
        var chartType = input.ChartSpec.ChartType.Name;
        var encodings = input.ChartSpec.Encodings;
        var rows = types.Rows;
        string? F(string channel) => types.FieldOf(channel);
        FieldType T(string channel) => types.Of(channel);

        void Add(bool? condition, string message, string? channel = null, bool fromRowsOrTypes = true)
        {
            if (condition == true)
            {
                reasons.Add(new ExcelReason(message, channel, fromRowsOrTypes));
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
                Add(NotQuantitative(T("y")), "requires a quantitative y field for a native Excel area chart", "y");
                break;
            case "Bar Chart":
                Add(And(NotQuantitative(T("x")), NotQuantitative(T("y"))), "requires one quantitative measure axis for a native Excel bar chart");
                break;
            case "Grouped Bar Chart":
                Add(And(NotQuantitative(T("x")), NotQuantitative(T("y"))), "requires one quantitative measure axis for a native Excel bar chart");
                Add(IsContinuous(T("group")), "does not support continuous grouping in a native Excel grouped bar chart", "group");
                break;
            case "Stacked Bar Chart":
                Add(And(NotQuantitative(T("x")), NotQuantitative(T("y"))), "requires one quantitative measure axis for a native Excel bar chart");
                Add(IsContinuous(T("color")), "does not support continuous color in a native Excel stacked bar chart", "color");
                break;
            case "Line Chart":
                Add(NotQuantitative(T("y")), "requires a quantitative y field for a native Excel line chart", "y");
                Add(IsContinuous(T("color")), "does not support continuous color on a native Excel line chart", "color");
                break;
            case "Scatter Plot":
                Add(Or(NotQuantitative(T("x")), NotQuantitative(T("y"))), "requires quantitative x and y fields for a native Excel XY chart");
                Add(F("shape") is not null, "does not support a shape encoding in native Excel charts", "shape", false);
                Add(IsContinuous(T("color")), "does not support continuous color in a native Excel Scatter or Bubble chart", "color");
                break;
            case "Connected Scatter Plot":
                Add(Or(NotQuantitative(T("x")), NotQuantitative(T("y"))), "requires quantitative x and y fields for a native Excel XY chart");
                Add(F("shape") is not null, "does not support a shape encoding in native Excel charts", "shape", false);
                Add(F("order") is null, "requires an explicit order field for a native Excel connected scatter chart", "order", false);
                Add(F("detail") is not null, "does not support an unlegended detail series in a native Excel connected scatter chart", "detail", false);
                Add(IsContinuous(T("color")), "does not support continuous color in a native Excel connected scatter chart", "color");
                break;
            case "Boxplot":
                Add(F("x") is null, "requires a categorical x field for a native Excel box and whisker chart", "x", false);
                Add(IsTemporal(T("x")), "requires a categorical x field for a native Excel box and whisker chart", "x");
                Add(F("y") is null, "requires a quantitative y field for a native Excel box and whisker chart", "y", false);
                Add(NotQuantitative(T("y")), "requires a quantitative y field for a native Excel box and whisker chart", "y");
                Add(F("color") is not null, "does not yet support color-grouped native Excel box and whisker charts", "color", false);
                Add(Property(input, "whiskerMethod") == "minmax", "does not support min-max whiskers", null, false);
                break;
            case "Candlestick Chart":
                Candlestick(input, types, Add);
                break;
            case "Funnel Chart":
                Add(F("y") is null, "requires a categorical y stage field for a native Excel funnel chart", "y", false);
                Add(IsQuantitative(T("y")), "requires a categorical y stage field for a native Excel funnel chart", "y");
                Add(NotQuantitative(T("size")), "requires a quantitative size field for a native Excel funnel chart", "size");
                Add(NonNegativeViolated(F("size")), "requires non-negative values for a native Excel funnel chart", "size");
                break;
            case "Histogram":
                var valueChannel = F("x") is not null ? "x" : "y";
                Add(F(valueChannel) is null, "requires a quantitative value field for a native Excel histogram", valueChannel, false);
                Add(NotQuantitative(T(valueChannel)), "requires a quantitative value field for a native Excel histogram", valueChannel);
                Add(F(valueChannel) is { } histogramField && !rows.Any(r => Js.Get(r, histogramField, out var p) is var v && Js.IsFiniteNumber(v, p)),
                    "requires finite numeric values for a histogram", valueChannel);
                break;
            case "Pyramid Chart":
                Pyramid(types, rows, Add, NonNegativeViolated);
                break;
            case "Radar Chart":
                Add(F("x") is null, "requires a categorical metric x field for a native Excel radar chart", "x", false);
                Add(Or(IsQuantitative(T("x")), IsTemporal(T("x"))), "requires a categorical metric x field for a native Excel radar chart", "x");
                Add(F("y") is null, "requires a quantitative y field for a native Excel radar chart", "y", false);
                Add(NotQuantitative(T("y")), "requires a quantitative y field for a native Excel radar chart", "y");
                Add(F("color") is not null ? IsContinuous(T("color")) : false, "requires a discrete color field for native Excel radar series", "color");
                Add(F("y") is { } radarValue && rows.Any(r => Js.ToNumber(Js.Get(r, radarValue, out var p), p) < 0), "requires non-negative values for a native Excel radar chart", "y");
                Add(F("x") is { } metric && rows.Select(r => Js.ToJsString(Js.Get(r, metric, out var p), p)).Distinct(StringComparer.Ordinal).Count() < 3,
                    "requires at least three metric axes for a radar chart", "x");
                break;
            case "Sunburst Chart":
                Add(F("color") is null, "requires a categorical color field for a native Excel sunburst chart", "color", false);
                Add(IsQuantitative(T("color")), "requires a categorical color field for a native Excel sunburst chart", "color");
                Add(NotQuantitative(T("size")), "requires a quantitative size field for a native Excel sunburst chart", "size");
                Add(F("group") is not null ? IsQuantitative(T("group")) : false, "requires a categorical group field for a native Excel sunburst hierarchy", "group");
                Add(F("detail") is not null && F("group") is null, "requires a group field before detail in a native Excel sunburst hierarchy", "detail", false);
                Add(F("detail") is not null ? IsQuantitative(T("detail")) : false, "requires a categorical detail field for a native Excel sunburst hierarchy", "detail");
                Add(NonNegativeViolated(F("size")), "requires non-negative values for a native Excel sunburst chart", "size");
                break;
            case "Treemap":
                Add(F("color") is null, "requires a categorical color field for a native Excel treemap", "color", false);
                Add(IsQuantitative(T("color")), "requires a categorical color field for a native Excel treemap", "color");
                Add(NotQuantitative(T("size")), "requires a quantitative size field for a native Excel treemap", "size");
                Add(F("detail") is not null ? IsQuantitative(T("detail")) : false, "requires a categorical detail field for a native Excel treemap hierarchy", "detail");
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

    private static string? Property(ChartAssemblyInput input, string key) =>
        input.ChartSpec.ChartProperties is { } p && p.TryGetValue(key, out var v) && Js.Kind(v) == JsonValueKind.String ? v!.GetValue<string>() : null;

    private static void Candlestick(ChartAssemblyInput input, FieldTypes types, Action<bool?, string, string?, bool> add)
    {
        var x = types.FieldOf("x");
        var xType = types.Of("x");
        add(x is null, "requires a temporal or ordered x field for a native Excel stock chart", "x", false);
        add(xType == FieldType.Unknown ? null : xType is not (FieldType.Temporal or FieldType.Ordinal or FieldType.TemporalOrOrdinal), "requires a temporal or ordered x field for a native Excel stock chart", "x", true);
        foreach (var channel in PriceChannels)
        {
            add(types.FieldOf(channel) is null, $"requires a quantitative {channel} field for a native Excel stock chart", channel, false);
            add(NotQuantitative(types.Of(channel)), $"requires a quantitative {channel} field for a native Excel stock chart", channel, true);
        }

        add(types.FieldOf("color") is not null || types.FieldOf("group") is not null || types.FieldOf("detail") is not null,
            "does not support color, group, or detail encodings in a native Excel stock chart", null, false);
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
            add(true, "requires exactly one quantitative measure axis for a native Excel pyramid chart", null, true);
            return;
        }

        var category = xMeasure == true ? "y" : "x";
        add(types.FieldOf(category) is null, "requires a discrete category axis for a native Excel pyramid chart", category, false);
        add(IsQuantitative(types.Of(category)), "requires a discrete category axis for a native Excel pyramid chart", category, true);
        var group = types.FieldOf("color");
        add(group is null, "requires a discrete color field for a native Excel pyramid chart", "color", false);
        add(IsContinuous(types.Of("color")), "requires a discrete color field for a native Excel pyramid chart", "color", true);
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
        add(NotQuantitative(types.Of("y")), "requires a quantitative y field for a native Excel waterfall chart", "y", true);
        var roles = types.FieldOf("color");
        add(roles is not null ? IsQuantitative(types.Of("color")) : false, "requires color to contain start/delta/end roles for a native Excel waterfall chart", "color", true);
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
            var mode = Property(input, "totals") is ("none" or "first" or "last" or "both") and var explicitMode
                ? explicitMode
                : LastReconciles(values) ? "both" : "first";
            add(mode is "last" or "both", "cannot mark a final total because Office.js does not expose Waterfall total-point semantics", null, true);
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
