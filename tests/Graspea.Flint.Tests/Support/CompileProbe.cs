using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Graspea.Flint.Tests.Support;

/// <summary>Compiles C# source in memory against the running framework and Graspea.Flint, to pin overload resolution
/// and language-version behaviour that reflection cannot show (design §13.13 item 8).</summary>
public static class CompileProbe
{
    private static readonly Lazy<MetadataReference[]> References = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(Chart).Assembly.Location)
            .Distinct(StringComparer.Ordinal)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray());

    /// <summary>The errors and warnings of compiling <paramref name="sources"/> as a library at <paramref name="version"/>.</summary>
    public static Diagnostic[] Diagnostics(LanguageVersion version, params string[] sources) =>
        Compile(version, sources).GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning).ToArray();

    public static CSharpCompilation Compile(LanguageVersion version, params string[] sources)
    {
        var options = new CSharpParseOptions(version);
        return CSharpCompilation.Create(
            "Probe",
            sources.Select(s => CSharpSyntaxTree.ParseText(s, options)),
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    /// <summary>The first parameter type of the method each call of a method named <paramref name="name"/> binds to, in
    /// source order; null for a call that does not bind.</summary>
    public static string?[] BoundFirstParameters(CSharpCompilation compilation, string name) =>
        BoundMethods(compilation, name).Select(method => method?.OriginalDefinition.Parameters[0].Type.ToDisplayString()).ToArray();

    /// <summary>The parameter type names of the method each call of a method named <paramref name="name"/> binds to, in
    /// source order (<c>IEnumerable,RowOptions</c>); null for a call that does not bind.</summary>
    public static string?[] BoundParameterLists(CSharpCompilation compilation, string name) =>
        BoundMethods(compilation, name).Select(method => method is null ? null : string.Join(",", method.OriginalDefinition.Parameters.Select(p => p.Type.Name))).ToArray();

    private static IEnumerable<IMethodSymbol?> BoundMethods(CSharpCompilation compilation, string name) =>
        compilation.SyntaxTrees.SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .Where(invocation => invocation.Expression is Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax { Name.Identifier.ValueText: var n } && n == name)
            .Select(invocation => compilation.GetSemanticModel(tree).GetSymbolInfo(invocation).Symbol as IMethodSymbol));

    /// <summary>The language version <c>src/Graspea.Flint/Graspea.Flint.csproj</c> pins.</summary>
    public static LanguageVersion LibraryLanguageVersion()
    {
        var project = File.ReadAllText(Path.Combine(CommittedIr.RepositoryRoot(), "src", "Graspea.Flint", "Graspea.Flint.csproj"));
        var match = System.Text.RegularExpressions.Regex.Match(project, "<LangVersion>([^<]+)</LangVersion>");
        if (!match.Success || !LanguageVersionFacts.TryParse(match.Groups[1].Value, out var version))
        {
            throw new InvalidOperationException("Graspea.Flint.csproj sets no LangVersion.");
        }

        return version;
    }
}
