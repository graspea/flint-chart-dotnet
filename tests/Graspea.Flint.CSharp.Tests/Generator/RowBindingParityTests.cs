using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Graspea.Flint.CSharp.Tests.Support;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Graspea.Flint.CSharp.Tests.Generator;

/// <summary>
/// Mode B's candidate members (design §12.6.5) equal the keys System.Text.Json writes: <c>JsonTypeInfo.Properties</c> of
/// the same type at run time, on the reflection path (the options of <c>RowOptions</c>) and through a source-generated
/// context.
/// </summary>
public sealed class RowBindingParityTests
{
    private const string Rows = """
        using System.Collections.Generic;
        using System.Text.Json.Serialization;
        namespace Rows;
        public class Base
        {
            public string Inherited { get; set; } = "";
            public virtual string Shadowed { get; set; } = "";
            protected string Protected { get; set; } = "";
        }
        public sealed class Mixed : Base
        {
            public string Plain { get; set; } = "";
            [JsonPropertyName("custom_name")] public double Renamed { get; set; }
            [JsonIgnore] public string Hidden { get; set; } = "";
            [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string Kept { get; set; } = "";
            [JsonInclude] public string IncludedField = "";
            public string PublicField = "";
            public string GetOnly => "x";
            public string SetOnly { set { } }
            private string Private { get; set; } = "";
            public static string Static { get; set; } = "";
            public string this[int i] => "";
            public override string Shadowed { get; set; } = "";
            public string NoPublicGetter { private get; set; } = "";
        }
        public sealed record Positional(string FiscalYear, int URLCount, System.DateOnly? Day);
        public struct Point
        {
            public double X { get; set; }
            public double Y { get; set; }
        }
        public sealed class Fields
        {
            public double FirstValue;
            public string SecondValue { get; set; } = "";
            [JsonIgnore] public int Hidden;
        }
        [JsonSerializable(typeof(Positional))]
        [JsonSerializable(typeof(Point))]
        [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
        public partial class CamelJson : JsonSerializerContext;
        [JsonSerializable(typeof(Fields))]
        [JsonSerializable(typeof(Positional))]
        [JsonSourceGenerationOptions(IncludeFields = true, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
        public partial class SnakeJson : JsonSerializerContext;
        """;

    /// <summary>The names a member of each type could be looked up by, written or not, so that a candidate the generator
    /// misses or invents shows up as a difference.</summary>
    private static readonly Dictionary<string, string[]> Probes = new()
    {
        ["Mixed"] = ["inherited", "shadowed", "protected", "plain", "custom_name", "renamed", "hidden", "kept", "includedField", "publicField", "getOnly", "setOnly", "private", "static", "noPublicGetter", "extra"],
        ["Positional"] = ["fiscalYear", "urlCount", "uRLCount", "day", "fiscal_year", "url_count"],
        ["Point"] = ["x", "y"],
        ["Fields"] = ["first_value", "second_value", "hidden", "firstValue"],
    };

    public static IEnumerable<TestCaseData> Cases() => new[]
    {
        new TestCaseData("Mixed", null).SetName("Candidates_EqualTheWrittenKeys(Mixed, reflection)"),
        new TestCaseData("Positional", null).SetName("Candidates_EqualTheWrittenKeys(Positional, reflection)"),
        new TestCaseData("Point", null).SetName("Candidates_EqualTheWrittenKeys(Point, reflection)"),
        new TestCaseData("Positional", "CamelJson").SetName("Candidates_EqualTheWrittenKeys(Positional, CamelJson)"),
        new TestCaseData("Point", "CamelJson").SetName("Candidates_EqualTheWrittenKeys(Point, CamelJson)"),
        new TestCaseData("Fields", "SnakeJson").SetName("Candidates_EqualTheWrittenKeys(Fields, SnakeJson)"),
        new TestCaseData("Positional", "SnakeJson").SetName("Candidates_EqualTheWrittenKeys(Positional, SnakeJson)"),
    };

    // source: System.Text.Json at run time: JsonTypeInfo.Properties (written members) of the compiled type, by reflection
    // with RowOptions' settings (camelCase, nothing ignored) and through System.Text.Json's source-generated context; the
    // generator's view is the set of annotation keys it reports as not written (FLINT116, semantic_type_without_field)
    [TestCaseSource(nameof(Cases))]
    public void Candidates_EqualTheWrittenKeys(string type, string? context)
    {
        var written = Written(type, context);
        Assert.That(written, Is.Not.Empty);

        var keys = Probes[type].Union(written).ToList();
        var spec = $"{{\"semantic_types\":{{{string.Join(",", keys.Select(k => $"\"{k}\":\"Category\""))}}},\"chart_spec\":{{\"chartType\":\"Bar Chart\",\"encodings\":{{\"x\":\"{keys[0]}\"}}}}}}";
        var attribute = context is null ? $"[Graspea.Flint.FlintChart<Rows.{type}>(\"rows.flint.json\")]" : $"[Graspea.Flint.FlintChart<Rows.{type}>(\"rows.flint.json\", JsonContext = typeof(Rows.{context}))]";
        var run = GeneratorHarness.Run([Rows, attribute + " public static partial class RowsChart;"], [("rows.flint.json", spec)]);

        var notWritten = run.Diagnostics.Select(d => d.GetMessage()).Where(m => m.EndsWith("(semantic_type_without_field)", StringComparison.Ordinal))
            .Select(m => m[1..m.IndexOf('"', 1)]).ToList();
        Assert.That(keys.Except(notWritten), Is.EquivalentTo(written));
    }

    /// <summary>The keys System.Text.Json writes for the type, from the compiled assembly.</summary>
    private static List<string> Written(string type, string? context)
    {
        var compilation = GeneratorHarness.Compilation([Rows]);
        var driver = CSharpGeneratorDriver.Create([Net8Compiler.JsonSourceGenerator()], parseOptions: new CSharpParseOptions(LanguageVersion.CSharp12));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        Assert.That(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
        var (assembly, load) = Net8Compiler.Load((CSharpCompilation)output);
        try
        {
            var rowType = assembly.GetType($"Rows.{type}")!;
            JsonTypeInfo info;
            if (context is null)
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
                };
                info = options.GetTypeInfo(rowType);
            }
            else
            {
                var instance = (JsonSerializerContext)assembly.GetType($"Rows.{context}")!.GetProperty("Default")!.GetValue(null)!;
                info = instance.GetTypeInfo(rowType)!;
            }

            return info.Properties.Where(p => p.Get is not null && !p.IsExtensionData).Select(p => p.Name).ToList();
        }
        finally
        {
            load.Unload();
        }
    }
}
