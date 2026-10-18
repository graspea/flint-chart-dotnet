using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Graspea.Flint.CSharp.Tests.Support;
using Graspea.Flint.Serialization;
using Microsoft.CodeAnalysis;

namespace Graspea.Flint.CSharp.Tests.Contract;

/// <summary>
/// The fidelity rule of design §12.3, proven by the compile round-trip of §12.10: for every document of the corpus, each
/// style and each data mode, the emitted C# compiles without warnings (C# 12, net8.0 reference pack), runs, and builds a
/// document that is <c>DeepEquals</c> to the canonicalised input, with the key order of every <c>data.values</c> row kept.
/// In parameter mode the rows are deserialised from <c>data.values</c> into the generated <c>Row</c>, so the expected rows
/// carry every member of <c>Row</c> in its order, a member missing from an input row written as null.
/// </summary>
[Category("Contract")]
[Category("CompileRoundTrip")]
public sealed class CompileRoundTripTests
{
    public static IEnumerable<TestCaseData> Combinations() =>
        from style in new[] { "Fluent", "Model" }
        from mode in new[] { "Inline", "External", "Parameter" }
        select new TestCaseData(style, mode).SetName($"RoundTrip_EveryDocument({style}, {mode})");

    // source: design §12.3 fidelity rule and §12.10 compile round-trip; the documents are tests/fixtures (§7.9 mapping of
    // upstream's TEST_GENERATORS and the crafted probe cases), samples/out and the README quick start; the expected value is
    // the contract canonicaliser of §10.3 applied to the input
    [TestCaseSource(nameof(Combinations))]
    public void RoundTrip_EveryDocument(string styleName, string modeName)
    {
        var style = Enum.Parse<CodeStyle>(styleName);
        var mode = Enum.Parse<DataMode>(modeName);
        var cases = SpecCorpus.Accepted;
        Assert.That(cases, Has.Count.GreaterThan(900));
        var converted = cases.Select((c, i) => (Case: c, Name: $"Case{i:D4}", Result: SpecConverter.Convert(c.Json, new ConversionOptions($"Case{i:D4}")
        {
            Style = style,
            Data = mode,
            SourceName = "case.flint.json",
            Version = "0.0.0",
        }))).ToList();

        var compilation = Net8Compiler.Compile($"RoundTrip{style}{mode}", converted.Select(c => (c.Name, c.Result.Code)));
        var failures = new List<string>();
        var broken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in Net8Compiler.Problems(compilation).GroupBy(d => d.Location.SourceTree?.FilePath ?? string.Empty))
        {
            broken.Add(group.Key);
            var c = converted.FirstOrDefault(x => x.Name == group.Key);
            failures.Add($"{c.Case?.Id ?? group.Key}: does not compile: {string.Join("; ", group.Take(3))}");
        }

        var (assembly, context) = Net8Compiler.Load(compilation.RemoveSyntaxTrees(compilation.SyntaxTrees.Where(t => broken.Contains(t.FilePath))));
        var skipped = 0;
        try
        {
            foreach (var (c, name, result) in converted.Where(x => !broken.Contains(x.Name)))
            {
                var failure = Check(assembly.GetType(name)!, c, result, mode);
                if (failure == Skipped)
                {
                    skipped++;
                }
                else if (failure is not null)
                {
                    failures.Add($"{c.Id}: {failure}");
                }
            }
        }
        finally
        {
            context.Unload();
        }

        Assert.That(failures, Is.Empty, $"{failures.Count} of {cases.Count} documents fail:\n{string.Join("\n", failures.Take(40))}");
        Assert.That(cases.Count - skipped, Is.GreaterThan(900), "documents checked");
    }

    /// <summary>The simple names the tool's README lists under name clashes. <c>Row</c> and <c>Json</c> are declared in the
    /// clash namespace too, but the class's own members win over them.</summary>
    private static readonly string[] ClashNames = ["Chart", "ChartData", "SemanticType"];

    // source: design §12.10 (once more, the inline fluent classes compiled inside a namespace that declares Chart, Row,
    // SemanticType and Json types; a namespace member wins over a using import, so every error must be at one of the simple
    // names the README lists, and a new clash cannot appear unnoticed) and src/Graspea.Flint.Tool/README.md
    [Test]
    public void NameClash_ErrorsAreOnlyAtTheListedNames()
    {
        var sources = SpecCorpus.Accepted.Select((c, i) => ($"Case{i:D4}", SpecConverter.Convert(c.Json, new ConversionOptions($"Case{i:D4}") { Namespace = "Clash" }).Code))
            .Append(("Clash", "namespace Clash;\npublic sealed class Chart { }\npublic sealed class Row { }\npublic sealed class SemanticType { }\npublic sealed class Json { }\npublic sealed class ChartData { }\n"));
        var compilation = Net8Compiler.Compile("NameClash", sources);

        var errors = Net8Compiler.Problems(compilation).Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
        var outside = errors.Where(e => !ClashNames.Contains(ClashName(e))).ToList();

        Assert.That(errors, Is.Not.Empty, "the clash namespace no longer clashes; update the README list");
        Assert.That(outside, Is.Empty, string.Join("\n", outside.Take(10).Select(e => $"{e} at '{e.Location.SourceTree!.GetText().ToString(e.Location.SourceSpan)}'")));
    }

    /// <summary>The simple name an error is at: the name itself, or the left side of the member access whose member the
    /// error names (<c>Chart.Line</c> reports CS0117 at <c>Line</c>).</summary>
    private static string ClashName(Microsoft.CodeAnalysis.Diagnostic error)
    {
        var node = error.Location.SourceTree!.GetRoot().FindNode(error.Location.SourceSpan);
        if (node.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax access && access.Name == node)
        {
            return access.Expression.ToString();
        }

        return node.ToString();
    }

    // source: design §12.5.1 and src/Graspea.Flint.Tool/README.md (a class named like a type or namespace that the output
    // writes by its simple name hides it inside the class, so Chart.Bar(Rows) in a class Chart binds to the class itself);
    // every such name of every style and data mode over the corpus must be one of CSharpText.ReservedClassNames
    [Test]
    public void SimpleNamesInTheOutput_AreReservedClassNames()
    {
        var sources = (from style in new[] { CodeStyle.Fluent, CodeStyle.Model }
                       from mode in new[] { DataMode.Inline, DataMode.External, DataMode.Parameter }
                       from c in SpecCorpus.Accepted.Select((c, i) => (c.Json, Name: $"Case{style}{mode}{i:D4}"))
                       select (c.Name, SpecConverter.Convert(c.Json, new ConversionOptions(c.Name) { Style = style, Data = mode }).Code)).ToList();
        var compilation = Net8Compiler.Compile("Reserved", sources);
        var unreserved = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        Parallel.ForEach(compilation.SyntaxTrees, tree =>
        {
            SemanticModel? model = null;
            foreach (var name in tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.SimpleNameSyntax>())
            {
                // The member names are refused as class names already; only the other names need the binder.
                var text = name.Identifier.ValueText;
                if (!IsLookedUp(name) || CSharpText.ReservedClassNames.Contains(text) || SpecConverter.MemberNames.Contains(text) || unreserved.ContainsKey(text))
                {
                    continue;
                }

                model ??= compilation.GetSemanticModel(tree);
                // An attribute name binds to the attribute's constructor.
                var bound = model.GetSymbolInfo(name).Symbol;
                if ((bound is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor ? constructor.ContainingType : bound) is INamespaceOrTypeSymbol symbol
                    && !SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, compilation.Assembly))
                {
                    unreserved.TryAdd(text, true);
                }
            }
        });

        Assert.That(sources, Has.Count.GreaterThan(5400));
        Assert.That(unreserved.Keys.Order(StringComparer.Ordinal), Is.Empty, string.Join(" ", unreserved.Keys.Order(StringComparer.Ordinal)));
    }

    /// <summary>Whether the name is found by lookup from inside the class: not the member of an access or a qualified name,
    /// not in a using directive, not <c>var</c>.</summary>
    private static bool IsLookedUp(Microsoft.CodeAnalysis.CSharp.Syntax.SimpleNameSyntax name) => name.Parent switch
    {
        Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax access => access.Name != name,
        Microsoft.CodeAnalysis.CSharp.Syntax.QualifiedNameSyntax qualified => qualified.Right != name,
        Microsoft.CodeAnalysis.CSharp.Syntax.AliasQualifiedNameSyntax or Microsoft.CodeAnalysis.CSharp.Syntax.MemberBindingExpressionSyntax => false,
        _ => !name.IsVar && !name.Ancestors().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>().Any(),
    };

    // source: design §12.5.4 (the tool refuses exactly what its double read refuses); the corpus documents FlintJson.Deserialize
    // refuses are crafted probe cases of invalid shapes, listed here so a new refusal cannot hide a document
    [Test]
    public void Corpus_RejectsOnlyTheInvalidProbeShapes()
    {
        Assert.That(SpecCorpus.Rejected.Select(r => r[..r.IndexOf(':')]), Is.EquivalentTo(Array.Empty<string>()), string.Join("\n", SpecCorpus.Rejected));
    }

    /// <summary>A document parameter mode cannot bind: a row that is not an object has no <c>Row</c> to deserialise into.</summary>
    private const string Skipped = "skipped";

    private static string? Check(Type type, SpecCase c, ConversionResult result, DataMode mode)
    {
        var input = JsonNode.Parse(c.Json)!.AsObject();
        var build = type.GetMethod("Build", BindingFlags.Public | BindingFlags.Static)!;
        var parameter = build.GetParameters().FirstOrDefault();
        var expected = input.DeepClone().AsObject();
        object?[] arguments;
        try
        {
            if (parameter is null)
            {
                arguments = [];
            }
            else if (parameter.ParameterType == typeof(JsonArray))
            {
                arguments = [JsonNode.Parse(result.Sidecar!)!.AsArray()];
            }
            else
            {
                var rowType = type.GetNestedType("Row")!;
                var values = input["data"] is JsonObject data && data["values"] is JsonArray array ? array : new JsonArray();
                if (values.Any(r => r is not null and not JsonObject))
                {
                    return Skipped;
                }

                arguments = [JsonSerializer.Deserialize(values.ToJsonString(), rowType.MakeArrayType())];

                // A template holds its spec as JSON and reads it back per bind (design §13.3.3), so the spec part of the
                // expected document is what FlintJson reads and writes again; it equals the input except where the input
                // sets a known member to null, which FlintJson reads back as absent.
                var spec = FlintJson.Deserialize(c.Json);
                spec.Data = null;
                expected = FlintJson.SerializeToNode(spec).AsObject();
                expected["data"] = ExpectedData(c.Json, input["data"] as JsonObject, values);
            }
        }
        catch (Exception e)
        {
            return $"cannot prepare the call: {e.GetType().Name}: {e.Message}";
        }

        ChartAssemblyInput built;
        try
        {
            built = (ChartAssemblyInput)build.Invoke(null, arguments)!;
        }
        catch (TargetInvocationException e)
        {
            return $"Build throws {e.InnerException!.GetType().Name}: {e.InnerException.Message}";
        }

        var actual = FlintJson.SerializeToNode(built);
        var canonicalExpected = SpecCorpus.Canonicalise(expected.ToJsonString());
        var canonicalActual = SpecCorpus.Canonicalise(actual.ToJsonString());
        if (!JsonNode.DeepEquals(canonicalExpected, canonicalActual))
        {
            return $"differs\n  expected: {Shorten(canonicalExpected!.ToJsonString())}\n  actual:   {Shorten(canonicalActual!.ToJsonString())}";
        }

        var expectedRows = expected["data"]?["values"] as JsonArray ?? [];
        var actualRows = actual["data"]?["values"] as JsonArray ?? [];
        for (var i = 0; i < expectedRows.Count; i++)
        {
            if (expectedRows[i] is JsonObject row && !row.Select(p => p.Key).SequenceEqual(((JsonObject)actualRows[i]!).Select(p => p.Key)))
            {
                return $"row {i} key order: expected {string.Join(",", row.Select(p => p.Key))}, actual {string.Join(",", ((JsonObject)actualRows[i]!).Select(p => p.Key))}";
            }
        }

        return null;
    }

    /// <summary>What rows of the inferred Row write: every member in Row order, null where the input row lacks the key;
    /// the other members of <c>data</c> kept.</summary>
    private static JsonObject ExpectedData(string json, JsonObject? data, JsonArray values)
    {
        var fields = RowInference.Infer(new SpecDocument(JsonReader.Parse(json)), replay: false).Fields.Select(f => f.Wire).ToList();
        var rows = new JsonArray(values.Select(r => r is JsonObject row
            ? new JsonObject(fields.Select(f => KeyValuePair.Create(f, row[f]?.DeepClone())))
            : (JsonNode?)null).ToArray());
        var result = new JsonObject { ["values"] = rows };
        if (data is not null && data["values"] is JsonArray)
        {
            foreach (var (key, value) in data.Where(p => p.Key != "values"))
            {
                result[key] = value?.DeepClone();
            }
        }

        return result;
    }

    private static string Shorten(string text) => text.Length <= 600 ? text : text[..600] + "…";
}
