using System.Collections.ObjectModel;
using Graspea.Flint.Serialization;
using Graspea.Flint.Validation;
using Graspea.Flint.Validation.Rules;

namespace Graspea.Flint.Delivery;

/// <summary>A named, ordered set of charts a browser loads together.</summary>
public sealed class ChartGroup
{
    /// <summary>Creates a group.</summary>
    /// <param name="name">The group name, matching <c>[A-Za-z0-9_.-]+</c>.</param>
    /// <param name="charts">The charts in order: at least one, none null, no two with one id.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="charts"/> is null.</exception>
    /// <exception cref="ArgumentException">The name is outside the alphabet, there is no chart, an entry is null, or an id repeats.</exception>
    public ChartGroup(string name, params ChartHandle[] charts)
    {
        DeliveryNames.Check(name, nameof(name), "group name");
        ArgumentNullException.ThrowIfNull(charts);
        if (charts.Length == 0)
        {
            throw new ArgumentException("A group needs at least one chart.", nameof(charts));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chart in charts)
        {
            if (chart is null)
            {
                throw new ArgumentException("A group must not hold a null chart.", nameof(charts));
            }

            if (!ids.Add(chart.Id))
            {
                throw new ArgumentException($"The chart id '{chart.Id}' occurs twice in the group.", nameof(charts));
            }
        }

        Name = name;
        Charts = new ReadOnlyCollection<ChartHandle>((ChartHandle[])charts.Clone());
    }

    /// <summary>The group name.</summary>
    public string Name { get; }

    /// <summary>The charts in the order given; a read-only snapshot.</summary>
    public IReadOnlyList<ChartHandle> Charts { get; }

    /// <summary>Validates every chart's template for <paramref name="backend"/>. Each issue keeps its code and moves its path
    /// under the chart's input in the manifest, as <c>charts.&lt;id&gt;.input.&lt;path&gt;</c>. Issues come in the group's
    /// order, then each template's own order, followed by that chart's fixed-definition Info issues (<c>fixed_*</c>).</summary>
    /// <param name="backend">The backend to validate for, or null for every backend that has the chart's template, Excel and
    /// Image-Charts included; pass the backend the browser draws with.</param>
    /// <returns>The issues; <see cref="ValidationResult.IsValid"/> is false when any is an error.</returns>
    public ValidationResult Validate(FlintBackend? backend = null)
    {
        var issues = new List<ValidationIssue>();
        foreach (var handle in Charts)
        {
            var fixedIssues = FixedDefinitionChecks.Evaluate(FlintJson.Deserialize(handle.SpecUtf8.Span));
            foreach (var issue in handle.ValidateTemplate(backend).Issues.Concat(fixedIssues))
            {
                issues.Add(issue.WithPath($"charts.{handle.Id}.input.{issue.Path}"));
            }
        }

        return new ValidationResult(issues);
    }
}
