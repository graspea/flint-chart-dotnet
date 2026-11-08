using System.Text.Json;
using System.Text.Json.Nodes;

namespace Graspea.Flint.Validation.Rules;

/// <summary>The Info issues a fixed chart definition earns when it is delivered without its rows: what flint-chart infers
/// from the rows present, and what a browser overrides. Reported by <c>ChartGroup.Validate</c> only.</summary>
internal static class FixedDefinitionChecks
{
    internal static List<ValidationIssue> Evaluate(ChartAssemblyInput input)
    {
        var issues = new List<ValidationIssue>();
        if (input.ChartSpec is not { } spec)
        {
            return issues;
        }

        if (spec.Encodings is { } encodings)
        {
            foreach (var (name, value) in encodings)
            {
                if (value is null)
                {
                    continue;
                }

                Channel? channel = name.Length == 0 ? null : new Channel(name);
                for (var i = 0; i < value.Encodings.Count; i++)
                {
                    var e = value.Encodings[i];
                    if (e is null)
                    {
                        continue;
                    }

                    var at = value.IsSeries ? $"chart_spec.encodings.{name}[{i}]" : $"chart_spec.encodings.{name}";
                    if (e.Type is null && ValidationRun.IsBound(e))
                    {
                        issues.Add(Info(Codes.FixedTypeMissing, $"{at}.type",
                            $"\"{name}\" has no type; flint-chart {FlintUpstream.Version} infers it from the rows present, so it can change with the rows.", channel, e.Field));
                    }

                    if (e.Aggregate is { } aggregate)
                    {
                        issues.Add(Info(Codes.FixedAggregate, $"{at}.aggregate",
                            $"\"{name}\" aggregates with \"{aggregate.Name}\"; flint-chart aggregates the rows present, so send final values instead.", channel, e.Field));
                    }

                    if (e.Type is { Name: "nominal" or "ordinal" } type && !HasExplicitOrder(e.SortBy))
                    {
                        issues.Add(Info(Codes.FixedOrderMissing, $"{at}.sortBy",
                            $"\"{name}\" is {type.Name} without an explicit category order (a JSON array sortBy, SortBy.ExplicitOrder); its categories follow the rows present.", channel, e.Field));
                    }
                }
            }
        }

        if (spec.ChartProperties is { } properties)
        {
            foreach (var (key, value) in properties)
            {
                if (key is "chartType" or "arrange" or "pivot" && value is not null)
                {
                    issues.Add(Info(Codes.FixedHostOverride, $"chart_spec.chartProperties.{key}",
                        $"\"{key}\" is a host-state override; flint-chart applies it only when the chart passes its gate and otherwise reverts silently."));
                }
            }
        }

        if (spec.BaseSize is not null)
        {
            issues.Add(Size("baseSize"));
        }

        if (spec.CanvasSize is not null)
        {
            issues.Add(Size("canvasSize"));
        }

        return issues;
    }

    private static ValidationIssue Size(string member) =>
        Info(Codes.FixedSize, $"chart_spec.{member}",
            $"{member} is set; in the browser the layout follows the container (mountChart derives canvasSize from availableSize).");

    private static bool HasExplicitOrder(string? sortBy)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            return false;
        }

        try
        {
            return JsonNode.Parse(sortBy) is JsonArray values && values.Any(ValidationRun.IsCategoryValue);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ValidationIssue Info(string code, string path, string message, Channel? channel = null, string? field = null) => new()
    {
        Severity = IssueSeverity.Info,
        Code = code,
        Message = message,
        Path = path,
        Channel = channel,
        Field = field,
    };
}
