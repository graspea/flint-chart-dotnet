using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Graspea.Flint.Generators;

/// <summary>
/// Generates a typed chart builder for every <c>static partial class</c> marked <c>[FlintChart("spec.flint.json")]</c>
/// (mode A, a <c>Row</c> record inferred from the spec) or <c>[FlintChart&lt;TRow&gt;("spec.flint.json")]</c> (mode B, the
/// user's row type), and reports a spec that does not match the catalog or the row type as FLINT1xx diagnostics.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class FlintChartGenerator : IIncrementalGenerator
{
    /// <summary>The tracking names of the pipeline steps (design §12.6.8).</summary>
    internal static class Steps
    {
        public const string Targets = "FlintTargets";
        public const string GenericTargets = "FlintGenericTargets";
        public const string Paths = "FlintPaths";
        public const string Specs = "FlintSpecs";
        public const string Language = "FlintLanguage";
        public const string Reference = "FlintReference";
        public const string RowShapes = "FlintRowShapes";
        public const string Models = "FlintModels";
    }

    private static readonly string Version = VersionOf(typeof(FlintChartGenerator).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static output =>
        {
            output.AddEmbeddedAttributeDefinition();
            output.AddSource(AttributeSource.HintName, AttributeSource.Text);
        });

        var targets = context.SyntaxProvider
            .ForAttributeWithMetadataName("Graspea.Flint.FlintChartAttribute", static (node, _) => node is TypeDeclarationSyntax, static (c, ct) => TargetReader.Read(c, generic: false, ct))
            .WithTrackingName(Steps.Targets);
        var genericTargets = context.SyntaxProvider
            .ForAttributeWithMetadataName("Graspea.Flint.FlintChartAttribute`1", static (node, _) => node is TypeDeclarationSyntax, static (c, ct) => TargetReader.Read(c, generic: true, ct))
            .WithTrackingName(Steps.GenericTargets);

        var projectDirectory = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) => options.GlobalOptions.TryGetValue("build_property.ProjectDir", out var directory) ? directory : string.Empty);
        var paths = targets.Collect().Combine(genericTargets.Collect()).Combine(projectDirectory)
            .Select(static (pair, _) => pair.Left.Left.Concat(pair.Left.Right)
                .Where(t => !string.IsNullOrEmpty(t.Path))
                .Select(t => SpecPaths.Resolve(pair.Right, t.Path!))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToEquatableArray())
            .WithTrackingName(Steps.Paths);

        // A cheap path check before any read: only *.flint.json files and the paths an attribute names are parsed.
        var specs = context.AdditionalTextsProvider.Combine(paths)
            .Where(static pair => SpecPaths.IsSpecFile(pair.Left.Path) || pair.Right.Any(p => SpecPaths.Same(p, SpecPaths.Normalize(pair.Left.Path))))
            .Select(static (pair, ct) => pair.Left.GetText(ct) is { } text ? new SpecModel(SpecPaths.Normalize(pair.Left.Path), pair.Left.Path, text.ToString(), ct) : null)
            .Where(static spec => spec is not null)
            .Select(static (spec, _) => spec!)
            .WithTrackingName(Steps.Specs);

        var language = context.ParseOptionsProvider
            .Select(static (options, _) => options is not CSharpParseOptions csharp || csharp.LanguageVersion >= LanguageVersion.CSharp12)
            .WithTrackingName(Steps.Language);
        var reference = context.CompilationProvider
            .Select(static (compilation, _) => ReferencedVersion(compilation))
            .WithTrackingName(Steps.Reference);

        // The row type's shape comes from the current compilation, not the attribute transform, which is not guaranteed to
        // run again when the row type changes in another file (design §12.6.8).
        var shaped = genericTargets.Combine(context.CompilationProvider)
            .Select(static (pair, ct) => new TargetWithShape(pair.Left, RowShapeReader.Read(pair.Left, pair.Right, ct)))
            .WithTrackingName(Steps.RowShapes);
        var all = targets.Select(static (t, _) => new TargetWithShape(t, null)).Collect()
            .Combine(shaped.Collect())
            .SelectMany(static (pair, _) => pair.Left.AddRange(pair.Right));

        var inputs = specs.Collect().Combine(projectDirectory).Combine(language).Combine(reference)
            .Select(static (pair, _) => new EmitInputs(Deduplicate(pair.Left.Left.Left), pair.Left.Left.Right, pair.Left.Right, pair.Right, Version));
        var models = all.Combine(inputs)
            .Select(static (pair, ct) => Emitter.Emit(pair.Left, pair.Right, ct))
            .WithTrackingName(Steps.Models);

        context.RegisterSourceOutput(models.Collect(), static (output, models) => AddSources(output, models));

        // The diagnostics are reported at the syntax tree of their C# location, so that #pragma warning and the tree's
        // .editorconfig severities apply; this step runs on every compilation change, but only maps paths to trees.
        var diagnostics = models.Select(static (m, _) => new KeyedDiagnostics(m.ClassKey, m.Diagnostics)).Collect();
        context.RegisterSourceOutput(diagnostics.Combine(context.CompilationProvider), static (output, pair) => Report(output, pair.Left, pair.Right));
    }

    private static void Report(SourceProductionContext output, ImmutableArray<KeyedDiagnostics> models, Compilation compilation)
    {
        var reported = new HashSet<DiagnosticInfo>();
        Dictionary<string, SyntaxTree>? trees = null;
        foreach (var model in models.OrderBy(m => m.ClassKey, StringComparer.Ordinal))
        {
            foreach (var diagnostic in model.Diagnostics)
            {
                if (reported.Add(diagnostic))
                {
                    if (diagnostic.Location is not null && trees is null)
                    {
                        trees = new Dictionary<string, SyntaxTree>(StringComparer.Ordinal);
                        foreach (var tree in compilation.SyntaxTrees)
                        {
                            trees[tree.FilePath] = tree;
                        }
                    }

                    output.ReportDiagnostic(diagnostic.ToDiagnostic(trees));
                }
            }
        }
    }

    private static void AddSources(SourceProductionContext output, ImmutableArray<EmitModel> models)
    {
        var classes = new HashSet<string>(StringComparer.Ordinal);
        var hints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models.OrderBy(m => m.ClassKey, StringComparer.Ordinal))
        {
            if (model.Source is null || !classes.Add(model.ClassKey))
            {
                continue;
            }

            var hint = model.HintBase;
            for (var n = 2; !hints.Add(hint); n++)
            {
                hint = $"{model.HintBase}{n}";
            }

            output.AddSource($"{hint}.flint.g.cs", model.Source);
        }
    }

    /// <summary>MSBuild keeps duplicate AdditionalFiles items; the first of each normalised path wins (design §12.6.4).</summary>
    private static ImmutableArray<SpecModel> Deduplicate(ImmutableArray<SpecModel> specs)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return specs.Where(s => seen.Add(s.NormalizedPath)).ToImmutableArray();
    }

    /// <summary>The informational version of the referenced Graspea.Flint, without build metadata; null when it is not
    /// referenced.</summary>
    private static string? ReferencedVersion(Compilation compilation)
    {
        var assembly = compilation.GetTypeByMetadataName("Graspea.Flint.Chart")?.ContainingAssembly;
        if (assembly is null)
        {
            return null;
        }

        var informational = assembly.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyInformationalVersionAttribute")?
            .ConstructorArguments.FirstOrDefault().Value as string;
        return VersionOf(informational ?? assembly.Identity.Version.ToString());
    }

    private static string VersionOf(string? informational)
    {
        var version = informational ?? "0.0.0";
        var plus = version.IndexOf('+');
        return plus < 0 ? version : version.Substring(0, plus);
    }
}
