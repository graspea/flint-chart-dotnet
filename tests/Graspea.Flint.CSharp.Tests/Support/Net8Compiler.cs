using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Graspea.Flint.CSharp.Tests.Support;

/// <summary>
/// Compiles emitted C# as a consumer on the floor would (design §12.10): C# 12 against the net8.0 reference pack
/// (Microsoft.NETCore.App.Ref 8) and Graspea.Flint, and loads the result into a collectible load context.
/// </summary>
public static class Net8Compiler
{
    private static readonly Lazy<MetadataReference[]> LazyReferences = new(() =>
        Directory.GetFiles(ReferencePack(), "*.dll")
            .Append(typeof(Chart).Assembly.Location)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray());

    /// <summary>The net8.0 reference pack and Graspea.Flint.</summary>
    public static MetadataReference[] References => LazyReferences.Value;

    /// <summary>The compilation options of every compile: a library, nullable enabled.</summary>
    public static CSharpCompilationOptions Options { get; } = new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, concurrentBuild: true);

    public static CSharpCompilation Compile(string name, IEnumerable<(string Path, string Source)> sources)
    {
        var options = new CSharpParseOptions(LanguageVersion.CSharp12);
        return CSharpCompilation.Create(
            name,
            sources.Select(s => CSharpSyntaxTree.ParseText(s.Source, options, s.Path)),
            References,
            Options);
    }

    /// <summary>The warnings and errors of the compilation.</summary>
    public static Diagnostic[] Problems(CSharpCompilation compilation) =>
        compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning).ToArray();

    /// <summary>Emits and loads the assembly; Graspea.Flint and the framework resolve to the test's own copies.</summary>
    public static (Assembly Assembly, AssemblyLoadContext Context) Load(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(20)));
        }

        stream.Position = 0;
        var context = new AssemblyLoadContext(compilation.AssemblyName, isCollectible: true);
        return (context.LoadFromStream(stream), context);
    }

    /// <summary>System.Text.Json's source generator from the reference pack, as the SDK runs it for a net8.0 project.</summary>
    public static ISourceGenerator JsonSourceGenerator() => JsonGenerator.Value;

    private static readonly Lazy<ISourceGenerator> JsonGenerator = new(() =>
    {
        var analyzers = Path.GetFullPath(Path.Combine(ReferencePack(), "..", "..", "analyzers", "dotnet"));
        var path = Directory.GetFiles(analyzers, "System.Text.Json.SourceGeneration.dll", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Last();
        return new Microsoft.CodeAnalysis.Diagnostics.AnalyzerFileReference(path, new Loader()).GetGenerators(LanguageNames.CSharp).Single();
    });

    private sealed class Loader : Microsoft.CodeAnalysis.IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath)
        {
        }

        public Assembly LoadFromPath(string fullPath) => Assembly.LoadFrom(fullPath);
    }

    /// <summary>
    /// The newest 8.0 reference pack: in the packs folder of the running .NET installation, or in the NuGet package folder,
    /// where the SDK downloads it for the net8.0 builds of the solution.
    /// </summary>
    private static string ReferencePack()
    {
        var dotnetRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", ".."));
        var nuget = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var candidates = new[]
        {
            Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref"),
            Path.Combine(nuget, "microsoft.netcore.app.ref"),
        };
        var pack = candidates.Where(Directory.Exists)
            .SelectMany(Directory.GetDirectories)
            .Select(d => (Dir: d, Version: Version.TryParse(Path.GetFileName(d).Split('-')[0], out var v) ? v : null))
            .Where(p => p.Version is { Major: 8 } && Directory.Exists(Path.Combine(p.Dir, "ref", "net8.0")))
            .OrderByDescending(p => p.Version)
            .Select(p => Path.Combine(p.Dir, "ref", "net8.0"))
            .FirstOrDefault();
        return pack ?? throw new InvalidOperationException(
            $"No Microsoft.NETCore.App.Ref 8.x found under {string.Join(" or ", candidates)}; build the solution's net8.0 targets once (dotnet build) to download it.");
    }
}
