using System.Collections.Immutable;
using Graspea.Flint.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Graspea.Flint.CSharp.Tests.Support;

/// <summary>An additional file held in memory.</summary>
public sealed class MemoryText(string path, string text) : AdditionalText
{
    public override string Path { get; } = path;

    public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
}

/// <summary>Analyzer config options with only global (build property) values.</summary>
public sealed class GlobalOptionsProvider(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptionsProvider
{
    public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(values);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Options(new Dictionary<string, string>());

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Options(new Dictionary<string, string>());

    private sealed class Options(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            var found = values.TryGetValue(key, out var v);
            value = v ?? string.Empty;
            return found;
        }
    }
}

/// <summary>A generator run: the driver (for incremental runs), its result and the compilation with the generated sources.</summary>
public sealed record GeneratorRun(GeneratorDriver Driver, GeneratorDriverRunResult Result, Compilation Output)
{
    /// <summary>The generator's diagnostics.</summary>
    public ImmutableArray<Diagnostic> Diagnostics => Result.Diagnostics;

    /// <summary>The generated source with the hint name ending in <paramref name="hint"/>.</summary>
    public string Source(string hint) => Result.Results[0].GeneratedSources.Single(s => s.HintName.EndsWith(hint, StringComparison.Ordinal)).SourceText.ToString();

    /// <summary>The warnings and errors of the compilation with the generated sources.</summary>
    public Diagnostic[] Problems() => Output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning).ToArray();
}

/// <summary>
/// Runs the source generator through <c>CSharpGeneratorDriver</c> (design §12.10): in-memory AdditionalTexts, C# 12 parse
/// options, the net8.0 reference pack and Graspea.Flint, the project directory as a build property, and step tracking for
/// the caching tests.
/// </summary>
public static class GeneratorHarness
{
    public const string ProjectDirectory = "/repo/app/";

    /// <summary>The compilation options of a consumer that opted in to the preview marker.</summary>
    public static CSharpCompilationOptions OptedIn { get; } = Net8Compiler.Options.WithSpecificDiagnosticOptions([new("FLINTEXP001", ReportDiagnostic.Suppress)]);

    public static CSharpCompilation Compilation(IEnumerable<string> sources, LanguageVersion language = LanguageVersion.CSharp12, CSharpCompilationOptions? options = null, IEnumerable<MetadataReference>? references = null)
    {
        var parse = new CSharpParseOptions(language);
        return CSharpCompilation.Create(
            "App",
            sources.Select((s, i) => CSharpSyntaxTree.ParseText(s, parse, $"/repo/app/Source{i}.cs")),
            references ?? Net8Compiler.References,
            options ?? OptedIn);
    }

    /// <summary>A driver; step tracking (for the caching tests) records every step's inputs, which is slow for a thousand
    /// specs, so a large run turns it off.</summary>
    public static GeneratorDriver Driver(IEnumerable<AdditionalText> files, LanguageVersion language = LanguageVersion.CSharp12, string projectDirectory = ProjectDirectory, bool tracking = true) =>
        CSharpGeneratorDriver.Create(
            [new FlintChartGenerator().AsSourceGenerator()],
            files,
            new CSharpParseOptions(language),
            new GlobalOptionsProvider(new Dictionary<string, string> { ["build_property.ProjectDir"] = projectDirectory }),
            new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: tracking));

    /// <summary>Runs the generator over <paramref name="source"/> with the spec files (paths relative to the project).</summary>
    public static GeneratorRun Run(string source, params (string Path, string Text)[] files) => Run([source], files);

    public static GeneratorRun Run(IEnumerable<string> sources, IEnumerable<(string Path, string Text)> files, LanguageVersion language = LanguageVersion.CSharp12, CSharpCompilationOptions? options = null, IEnumerable<MetadataReference>? references = null, bool tracking = true)
    {
        var compilation = Compilation(sources, language, options, references);
        var driver = Driver(files.Select(f => (AdditionalText)new MemoryText(ProjectDirectory + f.Path, f.Text)), language, tracking: tracking);
        return Rerun(driver, compilation);
    }

    public static GeneratorRun Rerun(GeneratorDriver driver, Compilation compilation)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return new GeneratorRun(driver, driver.GetRunResult(), output);
    }

    /// <summary>A diagnostic as <c>id severity file(line,column)</c>, 1-based as the build prints it.</summary>
    public static string Describe(Diagnostic d)
    {
        var span = d.Location.GetLineSpan();
        var file = span.Path.Length == 0 ? "-" : System.IO.Path.GetFileName(span.Path);
        return $"{d.Id} {d.Severity} {file}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1})";
    }
}
