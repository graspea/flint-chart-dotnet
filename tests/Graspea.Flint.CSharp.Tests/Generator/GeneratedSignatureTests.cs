using System.Reflection;
using Graspea.Flint.CSharp.Tests.Support;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Graspea.Flint.CSharp.Tests.Generator;

/// <summary>
/// The generated member signatures that user code calls are a versioned contract once the generator's contract is
/// declared stable (design D7.9): a reflection snapshot over the generator sample (samples/Graspea.Flint.Samples.Generators),
/// so that a change to <c>Row</c>, <c>Template</c>, <c>Build</c>, <c>BuildAsync</c>, <c>WriteAsync</c> or <c>Validate</c>
/// fails here.
/// </summary>
public sealed class GeneratedSignatureTests
{
    // source: design §12.6.3 (the member table) and D7.9 (the signatures follow the contract rules; bodies stay free),
    // over the sample's specs: Charts/revenue.flint.json (mode A) and Charts/units.flint.json (mode B with a context)
    [Test]
    public void Signatures_OfTheGeneratorSample_MatchTheSnapshot()
    {
        var folder = Repository.Path("samples", "Graspea.Flint.Samples.Generators");
        var specs = Directory.GetFiles(Path.Combine(folder, "Charts"), "*.flint.json").Order(StringComparer.Ordinal)
            .Select(f => (AdditionalText)new MemoryText(GeneratorHarness.ProjectDirectory + "Charts/" + Path.GetFileName(f), File.ReadAllText(f)));
        var compilation = GeneratorHarness.Compilation([File.ReadAllText(Path.Combine(folder, "Charts.cs"))]);
        var driver = CSharpGeneratorDriver.Create(
            [new Graspea.Flint.Generators.FlintChartGenerator().AsSourceGenerator(), Net8Compiler.JsonSourceGenerator()],
            specs,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            new GlobalOptionsProvider(new Dictionary<string, string> { ["build_property.ProjectDir"] = GeneratorHarness.ProjectDirectory }));
        var run = GeneratorHarness.Rerun(driver, compilation);
        Assert.That(run.Problems(), Is.Empty);

        var (assembly, context) = Net8Compiler.Load((CSharpCompilation)run.Output);
        try
        {
            var lines = new[] { "RevenueChart", "UnitsChart" }
                .SelectMany(name => Describe(assembly.GetType($"Graspea.Flint.Samples.Generators.{name}")!))
                .ToList();
            Assert.That(lines, Is.EqualTo(new[]
            {
                "class RevenueChart",
                "  record RevenueChart.Row",
                "    RevenueChart.Row(DateOnly Month, decimal Revenue, string Region)",
                "    DateOnly Month { get; init; }",
                "    decimal Revenue { get; init; }",
                "    string Region { get; init; }",
                "  static ChartTemplate<RevenueChart.Row> Template { get; }",
                "  static ChartAssemblyInput Build(IEnumerable<RevenueChart.Row> rows)",
                "  static ValueTask<ChartAssemblyInput> BuildAsync(IAsyncEnumerable<RevenueChart.Row> rows, CancellationToken cancellationToken = default)",
                "  static Task WriteAsync(Stream utf8Json, IAsyncEnumerable<RevenueChart.Row> rows, CancellationToken cancellationToken = default)",
                "  static ValidationResult Validate(FlintBackend? backend = null)",
                "class UnitsChart",
                "  static ChartTemplate<Sale> Template { get; }",
                "  static ChartAssemblyInput Build(IEnumerable<Sale> rows)",
                "  static ValueTask<ChartAssemblyInput> BuildAsync(IAsyncEnumerable<Sale> rows, CancellationToken cancellationToken = default)",
                "  static Task WriteAsync(Stream utf8Json, IAsyncEnumerable<Sale> rows, CancellationToken cancellationToken = default)",
                "  static ValidationResult Validate(FlintBackend? backend = null)",
            }), string.Join("\n", lines));
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary>The public members of a generated class and of its nested Row record, in declaration order.</summary>
    private static IEnumerable<string> Describe(Type type)
    {
        yield return $"class {Name(type)}";
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public).OrderBy(t => t.MetadataToken))
        {
            yield return $"  record {Name(nested)}";
            foreach (var constructor in nested.GetConstructors().Where(c => c.GetParameters().Length > 0 && c.GetParameters()[0].ParameterType != nested))
            {
                yield return $"    {Name(nested)}({Parameters(constructor)})";
            }

            foreach (var property in nested.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).OrderBy(p => p.MetadataToken))
            {
                yield return $"    {Name(property.PropertyType)} {property.Name} {{ get; {(property.SetMethod?.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit") == true ? "init; " : string.Empty)}}}";
            }
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Static).OrderBy(p => p.MetadataToken))
        {
            yield return $"  static {Name(property.PropertyType)} {property.Name} {{ get; }}";
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName).OrderBy(m => m.MetadataToken))
        {
            yield return $"  static {Name(method.ReturnType)} {method.Name}({Parameters(method)})";
        }
    }

    private static string Parameters(MethodBase method) =>
        string.Join(", ", method.GetParameters().Select(p => $"{Name(p.ParameterType)} {p.Name}{(p.HasDefaultValue ? $" = {(p.DefaultValue is null ? (p.ParameterType.IsValueType && Nullable.GetUnderlyingType(p.ParameterType) is null ? "default" : "null") : p.DefaultValue)}" : string.Empty)}"));

    private static string Name(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Name(underlying) + "?";
        }

        var keyword = type.IsEnum ? null : Type.GetTypeCode(type) switch
        {
            TypeCode.String => "string",
            TypeCode.Decimal => "decimal",
            TypeCode.Double => "double",
            TypeCode.Int32 => "int",
            TypeCode.Int64 => "long",
            TypeCode.Boolean => "bool",
            _ => null,
        };
        if (keyword is not null)
        {
            return keyword;
        }

        var name = type.Name;
        if (type.IsGenericType)
        {
            name = $"{name[..name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>";
        }

        return type.DeclaringType is { } declaring ? $"{Name(declaring)}.{name}" : name;
    }
}
