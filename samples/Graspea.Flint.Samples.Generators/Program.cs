// The source generator sample (design §12.2): builds the charts the generator wrote for Charts/*.flint.json, validates
// them and prints each as one JSON line; the streamed write must give the bytes of the materialised document. Exits 1
// when a chart has an Error.
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Graspea.Flint.Serialization;
using Graspea.Flint.Validation;

namespace Graspea.Flint.Samples.Generators;

public static class Program
{
    private const string Reflection = "Mode A writes rows by reflection.";

    [RequiresUnreferencedCode(Reflection)]
    [RequiresDynamicCode(Reflection)]
    public static async Task<int> Main()
    {
        var failed = false;
        foreach (var (name, input, validation) in await ModeA().ConfigureAwait(false))
        {
            failed |= Report(name, input, validation);
        }

        foreach (var (name, input, validation) in await ModeB().ConfigureAwait(false))
        {
            failed |= Report(name, input, validation);
        }

        return failed ? 1 : 0;
    }

    /// <summary>Mode A: rows of the generated Row record; the members use reflection, as this method declares.</summary>
    [RequiresUnreferencedCode(Reflection)]
    [RequiresDynamicCode(Reflection)]
    private static async Task<(string, ChartAssemblyInput, ValidationResult)[]> ModeA()
    {
        RevenueChart.Row[] rows =
        [
            new(new DateOnly(2024, 1, 1), 1200.50m, "EU"),
            new(new DateOnly(2024, 2, 1), 1350.00m, "EU"),
            new(new DateOnly(2024, 1, 1), 980.25m, "US"),
            new(new DateOnly(2024, 2, 1), 1100.75m, "US"),
        ];
        var input = RevenueChart.Build(rows);
        await Streamed(input, stream => RevenueChart.WriteAsync(stream, Async(rows))).ConfigureAwait(false);
        return [("revenue", input, RevenueChart.Validate())];
    }

    /// <summary>Mode B through a JsonSerializerContext: trimming- and AOT-safe, so this method carries no annotation.</summary>
    private static async Task<(string, ChartAssemblyInput, ValidationResult)[]> ModeB()
    {
        #region readme:generator-use
        Sale[] rows = [new(SalesRegion.North, 120), new(SalesRegion.South, 80), new(SalesRegion.North, 45)];
        ChartAssemblyInput input = UnitsChart.Build(rows);
        ValidationResult checks = UnitsChart.Validate(FlintBackend.VegaLite);
        #endregion

        var fromAsync = await UnitsChart.BuildAsync(Async(rows)).ConfigureAwait(false);
        if (FlintJson.Serialize(fromAsync) != FlintJson.Serialize(input))
        {
            throw new InvalidOperationException("BuildAsync differs from Build.");
        }

        await Streamed(input, stream => UnitsChart.WriteAsync(stream, Async(rows))).ConfigureAwait(false);
        return [("units", input, checks)];
    }

    private static async Task Streamed(ChartAssemblyInput input, Func<Stream, Task> write)
    {
        using var stream = new MemoryStream();
        await write(stream).ConfigureAwait(false);
        if (Encoding.UTF8.GetString(stream.ToArray()) != FlintJson.Serialize(input))
        {
            throw new InvalidOperationException("The streamed document differs from the materialised one.");
        }
    }

    private static bool Report(string name, ChartAssemblyInput input, ValidationResult template)
    {
        var validation = ChartValidator.Validate(input);
        foreach (var issue in template.Issues.Concat(validation.Issues).Where(i => i.Severity == IssueSeverity.Error))
        {
            Console.Error.WriteLine($"{name}: {issue.Severity} {issue.Code}: {issue.Message}");
        }

        Console.WriteLine(FlintJson.Serialize(input));
        return !template.IsValid || !validation.IsValid;
    }

    private static async IAsyncEnumerable<T> Async<T>(IEnumerable<T> rows)
    {
        foreach (var row in rows)
        {
            await Task.Yield();
            yield return row;
        }
    }
}
