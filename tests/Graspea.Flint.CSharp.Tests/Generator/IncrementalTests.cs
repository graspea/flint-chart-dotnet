using Graspea.Flint.CSharp.Tests.Support;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Graspea.Flint.CSharp.Tests.Generator;

/// <summary>The incremental pipeline of design §12.6.8: an unchanged input leaves every named step cached.</summary>
public sealed class IncrementalTests
{
    private static readonly string[] Steps =
        ["FlintTargets", "FlintGenericTargets", "FlintPaths", "FlintSpecs", "FlintLanguage", "FlintReference", "FlintRowShapes", "FlintModels"];

    private const string Targets = """
        namespace App;
        [Graspea.Flint.FlintChart("a.flint.json")] public static partial class AChart;
        [Graspea.Flint.FlintChart("b.flint.json")] public static partial class BChart;
        [Graspea.Flint.FlintChart<Sale>("a.flint.json")] public static partial class SaleChart;
        [Graspea.Flint.FlintChart<Other>("b.flint.json")] public static partial class OtherChart;
        """;

    private const string Rows = """
        namespace App;
        public sealed record Sale(string Month, double Revenue);
        """;

    private const string OtherRows = """
        namespace App;
        public sealed record Other(string Month, double Revenue);
        """;

    private static string Spec(string chartType) =>
        $"{{\"data\":{{\"values\":[{{\"month\":\"Jan\",\"revenue\":1}}]}},\"chart_spec\":{{\"chartType\":\"{chartType}\",\"encodings\":{{\"x\":\"month\",\"y\":\"revenue\"}}}}}}";

    private static readonly AdditionalText A = new MemoryText(GeneratorHarness.ProjectDirectory + "a.flint.json", Spec("Bar Chart"));
    private static readonly AdditionalText B = new MemoryText(GeneratorHarness.ProjectDirectory + "b.flint.json", Spec("Line Chart"));
    private static readonly AdditionalText Settings = new MemoryText(GeneratorHarness.ProjectDirectory + "appsettings.json", "{}");

    private static (GeneratorRun Run, CSharpCompilation Compilation) First()
    {
        var compilation = GeneratorHarness.Compilation([Targets, Rows, OtherRows]);
        return (GeneratorHarness.Rerun(GeneratorHarness.Driver([A, B, Settings]), compilation), compilation);
    }

    private static IEnumerable<IncrementalStepRunReason> Reasons(GeneratorRun run, string step) =>
        run.Result.Results[0].TrackedSteps[step].SelectMany(s => s.Outputs).Select(o => o.Reason);

    private static IncrementalStepRunReason[] Reasons(GeneratorRun run, string step, Func<object, bool> which) =>
        run.Result.Results[0].TrackedSteps[step].SelectMany(s => s.Outputs).Where(o => which(o.Value)).Select(o => o.Reason).ToArray();

    // source: design §12.10 (trackIncrementalGeneratorSteps: true with an unrelated edit leaves every named step Cached or
    // Unchanged) and §12.6.8 (a .json AdditionalFile other than *.flint.json is not parsed)
    [Test]
    public void UnrelatedEdit_LeavesEveryStepCachedOrUnchanged()
    {
        var (first, compilation) = First();
        Assert.That(first.Result.Results[0].TrackedSteps["FlintSpecs"].SelectMany(s => s.Outputs).ToList(), Has.Count.EqualTo(2), "appsettings.json is not parsed");

        var second = GeneratorHarness.Rerun(first.Driver, compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("class Unrelated { }", new CSharpParseOptions(LanguageVersion.CSharp12))));

        foreach (var step in Steps)
        {
            Assert.That(Reasons(second, step), Is.Not.Empty.And.All.AnyOf(IncrementalStepRunReason.Cached, IncrementalStepRunReason.Unchanged), step);
        }
    }

    // source: design §12.10 (editing one spec marks only its FlintSpecs and FlintModels entries Modified)
    [Test]
    public void SpecEdit_ModifiesOnlyThatSpecAndItsModels()
    {
        var (first, compilation) = First();
        var edited = new MemoryText(A.Path, Spec("Area Chart"));

        var second = GeneratorHarness.Rerun(first.Driver.ReplaceAdditionalText(A, edited), compilation);

        Assert.That(Reasons(second, "FlintSpecs", v => Property(v, "FilePath") == A.Path), Is.EqualTo(new[] { IncrementalStepRunReason.Modified }));
        Assert.That(Reasons(second, "FlintSpecs", v => Property(v, "FilePath") == B.Path), Has.Length.EqualTo(1).And.All.AnyOf(IncrementalStepRunReason.Cached, IncrementalStepRunReason.Unchanged));
        Assert.That(Reasons(second, "FlintModels", v => ClassKey(v) is "global::App.AChart" or "global::App.SaleChart"), Has.Length.EqualTo(2).And.All.EqualTo(IncrementalStepRunReason.Modified));
        Assert.That(Reasons(second, "FlintModels", v => ClassKey(v) is "global::App.BChart" or "global::App.OtherChart"), Has.Length.EqualTo(2).And.All.EqualTo(IncrementalStepRunReason.Unchanged));
        Assert.That(second.Source("App.AChart.flint.g.cs"), Does.Contain("Chart.Area<Row>()"));
    }

    // source: design §12.10 (editing TRow marks only its FlintRowShapes entry Modified); the other row type is in a file of
    // its own, since a member's location is part of its shape
    [Test]
    public void RowTypeEdit_ModifiesOnlyItsRowShape()
    {
        var (first, compilation) = First();
        var rows = compilation.SyntaxTrees.Single(t => t.ToString().Contains("record Sale", StringComparison.Ordinal));
        var editedRows = CSharpSyntaxTree.ParseText(Rows.Replace("record Sale(string Month, double Revenue)", "record Sale(string Month, double Revenue, string Region)", StringComparison.Ordinal), new CSharpParseOptions(LanguageVersion.CSharp12), rows.FilePath);

        var second = GeneratorHarness.Rerun(first.Driver, compilation.ReplaceSyntaxTree(rows, editedRows));

        Assert.That(Reasons(second, "FlintRowShapes", v => ClassKey(v) == "global::App.SaleChart"), Is.EqualTo(new[] { IncrementalStepRunReason.Modified }));
        Assert.That(Reasons(second, "FlintRowShapes", v => ClassKey(v) == "global::App.OtherChart"), Is.EqualTo(new[] { IncrementalStepRunReason.Unchanged }));
        Assert.That(Reasons(second, "FlintSpecs"), Is.Not.Empty.And.All.AnyOf(IncrementalStepRunReason.Cached, IncrementalStepRunReason.Unchanged));
    }

    /// <summary>A string property of a step output (the models are internal to the generator).</summary>
    private static string? Property(object value, string name) => value.GetType().GetProperty(name)?.GetValue(value) as string;

    /// <summary>The class key of a step output: an EmitModel, or a target with its shape.</summary>
    private static string? ClassKey(object value) =>
        Property(value, "ClassKey") ?? (value.GetType().GetProperty("Target")?.GetValue(value) is { } target ? Property(target, "ClassKey") : null);
}
