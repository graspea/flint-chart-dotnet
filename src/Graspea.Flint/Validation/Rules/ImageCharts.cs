using static Graspea.Flint.Validation.Rules.Tri;

namespace Graspea.Flint.Validation.Rules;

/// <summary>
/// The throw conditions of <c>assembleImageCharts</c> in the pinned package (docs/validation-codes.md
/// <c>image_charts_unsupported</c>): faceting, and the fields each chart family needs: a category and a measure on the
/// axes, quantitative x and y on a Scatter Plot, a slice and a value field on a Pie or Donut Chart. Hand-written from
/// src/image-charts/assemble.ts of flint-chart 1.0.1 and pinned to the recorded fixture outcomes. Every condition follows
/// from the encodings alone except the quantitative x and y of a Scatter Plot: an axis with an explicit type other than
/// quantitative is decided by the spec; any other axis reads the type inferred from the values.
/// </summary>
internal static class ImageCharts
{
    /// <summary>The chart types that take the axes path (<c>buildAxes</c>): a category on one axis, a measure on the other.</summary>
    private static readonly HashSet<string> AxesTemplates = new(StringComparer.Ordinal)
    {
        "Bar Chart", "Grouped Bar Chart", "Stacked Bar Chart", "Line Chart", "Sparkline", "Area Chart", "Radar Chart",
    };

    internal static List<ThrowReason> Evaluate(ChartAssemblyInput input, FieldTypes types)
    {
        var reasons = new List<ThrowReason>();
        var chartType = input.ChartSpec.ChartType.Name;
        string? F(string channel) => types.FieldOf(channel);

        // encodings[ch]?.field as `??` reads it: an empty field is a value, only a missing one falls through.
        string? Raw(string channel) => types.Encoding(channel)?.Field;

        void Add(bool? condition, string message, string? channel, bool fromRowsOrTypes = false)
        {
            if (condition == true)
            {
                reasons.Add(new ThrowReason(message, channel, fromRowsOrTypes));
            }
        }

        // Unconditional for every chart type (assemble.ts:192), before the rows are read.
        foreach (var channel in new[] { "column", "row" })
        {
            Add(F(channel) is not null, "does not support faceting in one chart", channel);
        }

        if (chartType is "Pie Chart" or "Donut Chart")
        {
            var slice = Raw("color") ?? Raw("x");
            var value = Raw("size") ?? Raw("theta") ?? Raw("y");
            Add(string.IsNullOrEmpty(slice) || string.IsNullOrEmpty(value), "could not resolve the slice field (color, else x) or the value field (size, else theta, else y)", null);
        }
        else if (chartType == "Scatter Plot")
        {
            Add(F("x") is null, "requires quantitative x and y fields", "x");
            Add(F("x") is not null && F("y") is null, "requires quantitative x and y fields", "y");
            if (F("x") is not null && F("y") is not null)
            {
                // One reason per axis: an axis whose type the spec decides is reported without the rows too.
                foreach (var axis in new[] { "x", "y" })
                {
                    Add(NotQuantitative(types.Of(axis)), "requires quantitative x and y fields", axis, fromRowsOrTypes: !SpecDecided(types, axis));
                }
            }
        }
        else if (AxesTemplates.Contains(chartType))
        {
            // The horizontal decision only swaps which of x and y is the category (assemble.ts:333-337), so it never
            // decides whether the fields resolve.
            Add(F("x") is null || F("y") is null, "could not resolve the category or the measure field (x and y)", null);
        }

        return reasons;
    }

    /// <summary>
    /// A channel the Image-Charts template does not declare but <c>assembleImageCharts</c> reads, which is therefore not
    /// reported as <c>unsupported_channel</c>: <c>group</c> with a field, the series of every chart type but Pie and Donut
    /// (assemble.ts:205-210), and on a Pie or Donut Chart <c>x</c>, the slice when <c>color</c> has no field, and
    /// <c>y</c>, the value when neither <c>size</c> nor <c>theta</c> has one (assemble.ts:265-266; only a missing field
    /// falls through, an empty one does not). The curated template leaves them out so that no builder gains a member
    /// (generator/overrides.json <c>imageChartsChannels</c>); <c>CatalogCheck</c> of Graspea.Flint.CSharp mirrors this.
    /// </summary>
    internal static bool ReadsUndeclared(ChartAssemblyInput input, FieldTypes types, string channel)
    {
        string? Raw(string ch) => types.Encoding(ch)?.Field;
        return input.ChartSpec.ChartType.Name is "Pie Chart" or "Donut Chart"
            ? (channel == "x" && Raw("color") is null) || (channel == "y" && Raw("size") is null && Raw("theta") is null)
            : channel == "group" && types.FieldOf("group") is not null;
    }

    /// <summary>An explicit type other than quantitative, without an aggregate, is the resolved type whatever the rows are
    /// (resolve-semantics.ts:549-550): the ISO hack only turns quantitative into temporal, and an aggregate forces
    /// quantitative.</summary>
    private static bool SpecDecided(FieldTypes types, string axis) =>
        types.Encoding(axis) is { } e && e.Aggregate is not { Name.Length: > 0 } && e.Type?.Name is { Length: > 0 } and not "quantitative";
}
