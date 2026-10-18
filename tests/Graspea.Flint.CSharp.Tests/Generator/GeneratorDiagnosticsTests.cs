using Graspea.Flint.CSharp.Tests.Support;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Graspea.Flint.CSharp.Tests.Generator;

/// <summary>
/// Every FLINT1xx id of design §12.6.7 with its severity and location: line and column in the JSON file for the spec
/// findings, the attribute, its arguments or the class identifier for the C# ones; one id can carry several severities.
/// </summary>
public sealed class GeneratorDiagnosticsTests
{
    private const string Target = "[Graspea.Flint.FlintChart(\"sales.flint.json\")]\npublic static partial class SalesChart;";

    private static GeneratorRun Run(string source, string spec, LanguageVersion language = LanguageVersion.CSharp12) =>
        GeneratorHarness.Run([source], [("sales.flint.json", spec)], language);

    private static string[] Flint(GeneratorRun run) =>
        run.Diagnostics.Where(d => d.Id.StartsWith("FLINT", StringComparison.Ordinal)).Select(GeneratorHarness.Describe).ToArray();

    /// <summary>The findings other than FLINT130, which a spec without data reports for every field without a type hint.</summary>
    private static string[] Typed(GeneratorRun run) => Flint(run).Where(d => !d.StartsWith("FLINT130", StringComparison.Ordinal)).ToArray();

    private static bool Generated(GeneratorRun run) => run.Result.Results[0].GeneratedSources.Any(s => s.HintName.EndsWith(".flint.g.cs", StringComparison.Ordinal));

    // source: design §12.6.1 (FLINT100, an Error at the attribute, when Graspea.Flint is not referenced); without the
    // library the generated code could not compile, so nothing is generated
    [Test]
    public void Flint100_LibraryNotReferenced_IsAnErrorAndGeneratesNothing()
    {
        var run = GeneratorHarness.Run([Target], [("sales.flint.json", FlintChartGeneratorTests.Sales)], references: Net8Compiler.References.SkipLast(1));

        Assert.That(Flint(run), Is.EqualTo(new[] { "FLINT100 Error Source0.cs(1,2)" }));
        Assert.That(Generated(run), Is.False);
    }

    // source: design §12.6.1 (FLINT100 compares the generator's informational version with the referenced Graspea.Flint's,
    // build metadata after + ignored) and decision 14 (the same version)
    [Test]
    public void Flint100_OtherLibraryVersion_IsAnErrorNamingBothVersions()
    {
        var fake = CSharpCompilation.Create(
            "Graspea.Flint",
            [CSharpSyntaxTree.ParseText("[assembly: System.Reflection.AssemblyInformationalVersion(\"9.9.9+abc\")] namespace Graspea.Flint { public static class Chart { } }")],
            Net8Compiler.References.SkipLast(1),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        Assert.That(fake.Emit(image).Success, Is.True);
        var run = GeneratorHarness.Run([Target], [("sales.flint.json", FlintChartGeneratorTests.Sales)], references: Net8Compiler.References.SkipLast(1).Append(MetadataReference.CreateFromImage(image.ToArray())));

        Assert.That(Flint(run), Is.EqualTo(new[] { "FLINT100 Error Source0.cs(1,2)" }));
        Assert.That(run.Diagnostics[0].GetMessage(), Does.StartWith("Graspea.Flint 9.9.9 is referenced, but the generator is "));
    }

    // source: design §12.6.4 (a path that matches no AdditionalFiles item is FLINT101 at the attribute argument; a candidate
    // that differs only in case is named) and §12.6.7 (the members are still generated, so user code compiles)
    [Test]
    public void Flint101_PathNotInAdditionalFiles_NamesACaseOnlyCandidate()
    {
        var run = GeneratorHarness.Run([Target], [("Sales.flint.json", FlintChartGeneratorTests.Sales)]);

        Assert.That(Flint(run), Is.EqualTo(new[] { "FLINT101 Error Source0.cs(1,27)" }));
        Assert.That(run.Diagnostics[0].GetMessage(), Does.Contain("/repo/app/Sales.flint.json differs only in case."));
        Assert.That(Generated(run), Is.True);
        Assert.That(run.Problems(), Is.Empty);
    }

    // source: design §12.6.4 (paths are relative to the project directory, use / or \, may leave it with .., and are
    // normalised) and §12.6.7 (FLINT102 at the line and column in the JSON file)
    [Test]
    public void Flint102_InvalidJson_IsAtTheLineAndColumn()
    {
        var run = GeneratorHarness.Run(["[Graspea.Flint.FlintChart(\"..\\\\app\\\\.\\\\sales.flint.json\")] public static partial class SalesChart;"], [("sales.flint.json", "{\n  \"chart_spec\": {,\n}")]);

        Assert.That(Flint(run), Is.EqualTo(new[] { "FLINT102 Error sales.flint.json(2,18)" }));
        Assert.That(run.Problems(), Is.Empty);
    }

    // source: design §12.6.7 (FLINT103: the JSON is not a ChartAssemblyInput, where FlintJson.Deserialize throws)
    [Test]
    public void Flint103_NotAChartAssemblyInput_IsAtTheNode()
    {
        Assert.That(Flint(Run(Target, "{\"chart_spec\":{\"chartType\":1}}")), Is.EqualTo(new[] { "FLINT103 Error sales.flint.json(1,28)" }));
    }

    // source: design §12.6.2 and §12.6.7 (FLINT104 at the class identifier: not a non-generic static partial class, a
    // containing type not partial, or a generated member name declared already); nothing is generated
    [TestCase("public partial class SalesChart;", "SalesChart")]
    [TestCase("public static class SalesChart { }", "SalesChart")]
    [TestCase("public static partial class SalesChart<T>;", "SalesChart")]
    [TestCase("public static partial class SalesChart { public static void Build() { } }", "SalesChart")]
    public void Flint104_InvalidTarget_IsAtTheIdentifier(string declaration, string identifier)
    {
        var source = "[Graspea.Flint.FlintChart(\"sales.flint.json\")] " + declaration;
        var run = Run(source, FlintChartGeneratorTests.Sales);

        Assert.That(Flint(run), Is.EqualTo(new[] { $"FLINT104 Error Source0.cs(1,{source.IndexOf(identifier, StringComparison.Ordinal) + 1})" }));
        Assert.That(Generated(run), Is.False);
    }

    // source: design §12.6.2 (every containing type must be partial; FLINT104)
    [Test]
    public void Flint104_ContainingTypeNotPartial_IsAnError()
    {
        var run = Run("public class Outer { [Graspea.Flint.FlintChart(\"sales.flint.json\")] public static partial class SalesChart; }", FlintChartGeneratorTests.Sales);

        Assert.That(Flint(run).Select(d => d[..14]), Is.EqualTo(new[] { "FLINT104 Error" }));
        Assert.That(run.Diagnostics[0].GetMessage(), Does.Contain("Outer, which must be partial"));
    }

    // source: design §12.6.7 (FLINT105, invalid attribute use: the path not a non-empty constant at the argument; more than
    // one FlintChart attribute, once, at the attribute; RowNaming together with JsonContext; a JsonContext that is not a
    // JsonSerializerContext, at the JsonContext argument)
    [Test]
    public void Flint105_InvalidAttributeUse_IsReportedOnce()
    {
        Assert.That(Flint(Run("[Graspea.Flint.FlintChart(\"\")] public static partial class SalesChart;", FlintChartGeneratorTests.Sales)), Is.EqualTo(new[] { "FLINT105 Error Source0.cs(1,27)" }));

        var twice = Run("public sealed record Sale(string Month);\n[Graspea.Flint.FlintChart(\"sales.flint.json\")][Graspea.Flint.FlintChart<Sale>(\"sales.flint.json\")] public static partial class SalesChart;", FlintChartGeneratorTests.Sales);
        Assert.That(Flint(twice), Is.EqualTo(new[] { "FLINT105 Error Source0.cs(2,2)" }));

        var both = Run(FlintChartGeneratorTests.Sale + "\n[Graspea.Flint.FlintChart<Sale>(\"sales.flint.json\", RowNaming = Graspea.Flint.FlintRowNaming.Verbatim, JsonContext = typeof(SalesJson))] public static partial class SalesChart;", FlintChartGeneratorTests.SaleSpec);
        Assert.That(Flint(both).Where(d => d.StartsWith("FLINT105", StringComparison.Ordinal)), Is.EqualTo(new[] { "FLINT105 Error Source0.cs(12,2)" }));

        var notContext = Run(FlintChartGeneratorTests.Sale + "\n[Graspea.Flint.FlintChart<Sale>(\"sales.flint.json\", JsonContext = typeof(string))] public static partial class SalesChart;", FlintChartGeneratorTests.SaleSpec);
        Assert.That(Flint(notContext).Where(d => d.StartsWith("FLINT105", StringComparison.Ordinal)), Is.EqualTo(new[] { "FLINT105 Error Source0.cs(12,53)" }));
    }

    // source: design §12.6.3 (the generated members are public or internal, so a row type less accessible than internal
    // would appear in their signatures, CS0053 and CS0051) and §12.6.7 (FLINT105 at the type argument; nothing is
    // generated where the generated code could not compile)
    [TestCase("public static partial class SChart { private sealed record Sale(string Month, double Revenue); }", "private")]
    [TestCase("file sealed record Sale(string Month, double Revenue);\npublic static partial class SChart;", "file-local")]
    public void Flint105_RowTypeBelowInternal_IsAnErrorAndGeneratesNothing(string declarations, string accessibility)
    {
        var source = "namespace App;\n" + declarations.Replace("public static partial class SChart", "[Graspea.Flint.FlintChart<SChart.Sale>(\"sales.flint.json\")] public static partial class SChart", StringComparison.Ordinal);
        if (accessibility == "file-local")
        {
            source = source.Replace("SChart.Sale", "Sale", StringComparison.Ordinal);
        }

        var run = Run(source, """{"chart_spec":{"chartType":"Bar Chart","encodings":{"x":"month","y":{"field":"revenue","type":"quantitative"}}}}""");

        var finding = run.Diagnostics.Single(d => d.Id.StartsWith("FLINT1", StringComparison.Ordinal));
        Assert.That(finding.Id, Is.EqualTo("FLINT105"));
        Assert.That(finding.GetMessage(), Does.Contain($"is {accessibility}; the generated members need a row type that is at least internal"));
        Assert.That(Generated(run), Is.False);
        Assert.That(run.Problems().Where(p => p.Location.SourceTree?.FilePath.EndsWith(".flint.g.cs", StringComparison.Ordinal) == true), Is.Empty);
    }

    // source: design §12.6.5 (FLINT106, a Warning at the JsonContext argument: no [JsonSerializable] names TRow, directly,
    // as an array element or as a generic type argument); the context names keys in camelCase, as the spec does
    [TestCase("[System.Text.Json.Serialization.JsonSerializable(typeof(int))]", "FLINT106 Warning Source0.cs(4,53)")]
    [TestCase("[System.Text.Json.Serialization.JsonSerializable(typeof(System.Collections.Generic.List<Sale>))]", null)]
    [TestCase("[System.Text.Json.Serialization.JsonSerializable(typeof(Sale[]))]", null)]
    public void Flint106_ContextWithoutTheRowType_IsAWarning(string serializable, string? expected)
    {
        var source = "namespace App;\npublic sealed class Sale { public string Month { get; set; } = \"\"; public double Revenue { get; set; } }\n"
            + serializable + " [System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)] public partial class Ctx : System.Text.Json.Serialization.JsonSerializerContext;\n"
            + "[Graspea.Flint.FlintChart<Sale>(\"sales.flint.json\", JsonContext = typeof(Ctx))] public static partial class SalesChart;";

        Assert.That(Flint(Run(source, FlintChartGeneratorTests.Sales)), Is.EqualTo(expected is null ? Array.Empty<string>() : new[] { expected }));
    }

    // source: design §12.6.2 (below C# 12, one FLINT107 per target at the class identifier and no members)
    [Test]
    public void Flint107_BelowCSharp12_IsOneErrorAndNoMembers()
    {
        var run = Run(Target, FlintChartGeneratorTests.Sales, LanguageVersion.CSharp11);

        Assert.That(Flint(run), Is.EqualTo(new[] { "FLINT107 Error Source0.cs(2,29)" }));
        Assert.That(Generated(run), Is.False);
    }

    // source: design §12.6.7 (the catalog checks at the node in the JSON file: FLINT111 at the encoding key, FLINT112 at the
    // property value, FLINT113 at the annotation value, FLINT114 at the theme_spec value, FLINT116 at the node, FLINT140 at
    // each unknown key; the validator code in the message)
    [Test]
    public void CatalogChecks_AreAtTheirJsonNodes()
    {
        const string spec = """
            {
              "semantic_types": { "a": "Revenue" },
              "chart_spec": {
                "chartType": "Line Chart",
                "zoom": 2,
                "encodings": { "x": "a", "y": "b", "goal": "c" },
                "chartProperties": { "interpolate": "wobbly", "glow": true }
              },
              "theme_spec": "neon"
            }
            """;
        var run = Run(Target, spec);

        Assert.That(Typed(run), Is.EquivalentTo(new[]
        {
            "FLINT111 Warning sales.flint.json(6,40)",
            "FLINT112 Warning sales.flint.json(7,41)",
            "FLINT113 Warning sales.flint.json(2,28)",
            "FLINT114 Error sales.flint.json(9,17)",
            "FLINT116 Info sales.flint.json(7,59)",
            "FLINT140 Info sales.flint.json(5,5)",
        }));
        Assert.That(run.Diagnostics.Single(d => d.Id == "FLINT112").GetMessage(), Does.EndWith("(invalid_option_value)"));
        Assert.That(run.Problems(), Is.Empty);
    }

    // source: design §12.6.7 (FLINT110 at the chartType value; FLINT115 at the offending node; one id with the validator's
    // severity per case: unknown_theme_preset is an Error where Vega-Lite or Plotly has the template, an Info otherwise)
    [Test]
    public void CatalogChecks_ThrowClassAndPerCaseSeverities()
    {
        Assert.That(Typed(Run(Target, "{\"chart_spec\":{\"chartType\":\"Image Chart\",\"encodings\":{\"x\":\"a\"}}}")), Is.EqualTo(new[] { "FLINT110 Error sales.flint.json(1,28)" }));
        Assert.That(Typed(Run(Target, "{\"chart_spec\":{\"chartType\":\"Line Chart\",\"encodings\":{\"x\":\"a\",\"y\":[\"b\"]}}}")), Is.EqualTo(new[] { "FLINT115 Error sales.flint.json(1,62)" }));

        var withoutTheme = Catalog.ChartTypes.First(c => c.Templates.All(t => !Catalog.BackendsApplyingTheme.Contains(t.Backend)));
        var channel = withoutTheme.Templates[0].Channels[0];
        var info = Run(Target, $"{{\"theme_spec\":\"neon\",\"chart_spec\":{{\"chartType\":\"{withoutTheme.Name}\",\"encodings\":{{\"{channel}\":\"a\"}}}}}}");
        Assert.That(info.Diagnostics.Where(d => d.Id == "FLINT114").Select(d => d.Severity), Is.EqualTo(new[] { DiagnosticSeverity.Info }));
        Assert.That(Typed(Run(Target, "{\"theme_spec\":\"neon\",\"chart_spec\":{\"chartType\":\"Bar Chart\",\"encodings\":{\"x\":\"a\",\"y\":\"b\"}}}")), Is.EqualTo(new[] { "FLINT114 Error sales.flint.json(1,15)" }));
    }

    private const string Row = """
        namespace App;
        public sealed class Sale
        {
            public string Region { get; set; } = "";
            [System.Text.Json.Serialization.JsonPropertyName("region")] public string Code { get; set; } = "";
            public string Total { get; set; } = "";
            public object Extra { get; set; } = 0;
            [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? Note { get; set; }
        }
        [Graspea.Flint.FlintChart<Sale>("sales.flint.json")]
        public static partial class SalesChart;
        """;

    // source: design §12.6.5 and §12.6.7 (mode B at the attribute's type argument: FLINT120 Warning for a field without a
    // member, FLINT121 Warning for an incompatible type, FLINT123 Error for two members, FLINT124 Info for an unchecked
    // type; FLINT122 Error at the member that may be omitted when null)
    [Test]
    public void ModeB_BindingFindings_AreAtTheTypeArgumentOrTheMember()
    {
        const string spec = """{"chart_spec":{"chartType":"Bubble Chart","encodings":{"x":"region","y":{"field":"total","type":"quantitative"},"color":"missing","size":{"field":"extra","type":"quantitative"}}}}""";
        var run = Run(Row, spec);

        Assert.That(Flint(run), Is.EquivalentTo(new[]
        {
            "FLINT120 Warning Source0.cs(10,27)",
            "FLINT121 Warning Source0.cs(10,27)",
            "FLINT122 Error Source0.cs(8,144)",
            "FLINT123 Error Source0.cs(10,27)",
            "FLINT124 Info Source0.cs(10,27)",
        }));
        Assert.That(run.Diagnostics.Single(d => d.Id == "FLINT120").GetMessage(), Does.StartWith("\"missing\" (sales.flint.json(1,"));
        Assert.That(run.Problems(), Is.Empty);
    }

    // source: design §12.6.7 and src/Graspea.Flint.Generators/README.md (a finding at a C# location is in that syntax tree,
    // so #pragma warning disable and the tree's .editorconfig severity apply to it, as to any compiler warning)
    [Test]
    public void ModeB_FindingAtTheAttribute_HonoursAPragma()
    {
        var source = Row.Replace("[Graspea.Flint.FlintChart<Sale>", "#pragma warning disable FLINT120\n[Graspea.Flint.FlintChart<Sale>", StringComparison.Ordinal) + "\n#pragma warning restore FLINT120\n";
        var run = Run(source, """{"chart_spec":{"chartType":"Bar Chart","encodings":{"x":"region","y":"missing"}}}""");

        var finding = run.Diagnostics.Single(d => d.Id == "FLINT120");
        Assert.That(finding.Location.SourceTree, Is.SameAs(run.Output.SyntaxTrees.First()));
        Assert.That(finding.IsSuppressed, Is.True);
    }

    // source: System.Text.Json's JsonIgnoreCondition (.NET 9 adds WhenWriting = 4, never written, and WhenReading = 5,
    // always written; written here as numbers, as the net8.0 reference pack lacks the names) and design §12.6.5 (an ignored
    // member is no candidate: FLINT120; a condition that may omit the member: FLINT122)
    [TestCase(4, "FLINT120")]
    [TestCase(5, null)]
    [TestCase(9, "FLINT122")]
    public void ModeB_JsonIgnoreCondition_IsReadAsSystemTextJsonWritesIt(int condition, string? expected)
    {
        var source = $$"""
            namespace App;
            public sealed record Sale(string Month, [property: System.Text.Json.Serialization.JsonIgnore(Condition = (System.Text.Json.Serialization.JsonIgnoreCondition){{condition}})] double Revenue);
            [Graspea.Flint.FlintChart<Sale>("sales.flint.json")]
            public static partial class SalesChart;
            """;
        var run = Run(source, """{"chart_spec":{"chartType":"Bar Chart","encodings":{"x":"month","y":{"field":"revenue","type":"quantitative"}}}}""");

        Assert.That(run.Diagnostics.Where(d => d.Id is "FLINT120" or "FLINT122").Select(d => d.Id), Is.EqualTo(expected is null ? Array.Empty<string>() : new[] { expected }));
    }

    // source: design D7.7 (FLINT120 is an Error for a static-series entry, as series_field_missing)
    [Test]
    public void ModeB_SeriesEntryWithoutMember_IsAnError()
    {
        var run = Run(Row, """{"chart_spec":{"chartType":"Line Chart","encodings":{"x":"region","y":["total","cost"]}}}""");

        Assert.That(run.Diagnostics.Where(d => d.Id == "FLINT120").Select(d => d.Severity), Is.EqualTo(new[] { DiagnosticSeverity.Error }));
    }

    // source: design §12.6.6 and §12.6.7 (mode A: FLINT130 Info at the JSON field typed string for lack of values and
    // hints; FLINT131 Warning at data.values when rows are not uniform objects; FLINT133 Info at an unused data.url)
    [Test]
    public void ModeA_InferenceFindings_AreAtTheirJsonNodes()
    {
        Assert.That(Flint(Run(Target, "{\"chart_spec\":{\"chartType\":\"Bar Chart\",\"encodings\":{\"x\":\"a\",\"y\":{\"field\":\"b\",\"type\":\"quantitative\"}}}}")),
            Is.EqualTo(new[] { "FLINT130 Info sales.flint.json(1,57)" }));
        Assert.That(Flint(Run(Target, "{\"data\":{\"values\":[{\"a\":1},{\"b\":2}]},\"chart_spec\":{\"chartType\":\"Bar Chart\",\"encodings\":{\"x\":\"a\",\"y\":\"b\"}}}")),
            Is.EqualTo(new[] { "FLINT131 Warning sales.flint.json(1,19)" }));
        Assert.That(Flint(Run(Target, "{\"data\":{\"url\":\"https://example.org/x.json\"},\"chart_spec\":{\"chartType\":\"Bar Chart\",\"encodings\":{\"x\":{\"field\":\"a\",\"type\":\"nominal\"},\"y\":{\"field\":\"b\",\"type\":\"quantitative\"}}}}")),
            Is.EqualTo(new[] { "FLINT133 Info sales.flint.json(1,16)" }));
    }
}
