using System.Text.Json;
using System.Text.Json.Nodes;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Parity;

/// <summary>
/// The hand-written core against the committed IR (design §10.3, §10.4 test 15): every member of the core types has a
/// property with the same wire name, written when set and omitted when unset exactly where the IR makes it optional,
/// with the JSON kind the IR type implies. Red whenever the IR gains, loses or retypes a core member (§9.3).
/// </summary>
public sealed class CoreParityTests
{
    private static readonly JsonArray Rows = [new JsonObject { ["month"] = "2024-01", ["revenue"] = 10 }];

    /// <summary>One wire form of a core type: the IR shape it must match and two instances, with only the required members
    /// set and with every member set.</summary>
    public sealed record Form(string Name, Func<JsonNode, JsonNode> IrShape, Func<JsonNode> Minimal, Func<JsonNode> Full);

    private static JsonNode Type(JsonNode types, string name) => types[name]!;

    private static JsonNode UnionMember(JsonNode types, string name, int index) => types[name]!["members"]![index]!;

    private static JsonNode Wire(ChartAssemblyInput input) => FlintJson.SerializeToNode(input);

    private static ChartAssemblyInput Input(ChartSpec? spec = null, ChartData? data = null) => new()
    {
        Data = data ?? ChartData.FromJson(Rows.DeepClone().AsArray()),
        ChartSpec = spec ?? new ChartSpec { ChartType = ChartType.LineChart },
    };

    private static ChartSpec FullSpec() => new()
    {
        ChartType = ChartType.LineChart,
        Title = "t",
        Subtitle = "s",
        Encodings = { ["x"] = "month" },
        BaseSize = new ChartSize(400, 300),
        CanvasSize = new ChartSize(800, 600),
        ChartProperties = new() { ["interpolate"] = "monotone" },
    };

    private static ChartEncoding FullEncoding() => new()
    {
        Field = "revenue",
        Type = EncodingType.Quantitative,
        Aggregate = Aggregate.Sum,
        SortOrder = SortOrder.Descending,
        SortBy = "y",
        Scheme = "default",
    };

    private static JsonNode InEncoding(ChartEncoding e)
    {
        var spec = new ChartSpec { ChartType = ChartType.LineChart, Encodings = { ["y"] = e } };
        return Wire(Input(spec))["chart_spec"]!["encodings"]!["y"]!;
    }

    private static JsonNode InAnnotation(SemanticAnnotation a)
    {
        var input = Input();
        input.SemanticTypes = new() { ["revenue"] = a };
        return Wire(input)["semantic_types"]!["revenue"]!;
    }

    private static JsonNode InCrossSection(ContinuousMarkCrossSection c)
    {
        var input = Input();
        input.Options = new AssembleOptions { ContinuousMarkCrossSection = c };
        return Wire(input)["options"]!["continuousMarkCrossSection"]!;
    }

    public static IEnumerable<TestCaseData> Forms()
    {
        Form[] forms =
        [
            new("ChartAssemblyInput", t => Type(t, "ChartAssemblyInput"), () => Wire(Input()), () =>
            {
                var input = Input(FullSpec());
                input.SemanticTypes = new() { ["revenue"] = SemanticType.Amount };
                input.ThemeSpec = ThemePreset.Economist;
                input.Options = new AssembleOptions();
                input.FieldDisplayNames = new() { ["revenue"] = "Revenue" };
                return Wire(input);
            }),
            new("ChartSpec", t => Type(t, "ChartSpec"), () => Wire(Input())["chart_spec"]!, () => Wire(Input(FullSpec()))["chart_spec"]!),
            new("ChartEncoding", t => Type(t, "ChartEncoding"), () => InEncoding(new ChartEncoding()), () => InEncoding(FullEncoding())),
            new("SemanticAnnotation", t => Type(t, "SemanticAnnotation"), () => InAnnotation(SemanticType.Percentage), () => InAnnotation(new SemanticAnnotation
            {
                Type = SemanticType.Percentage,
                IntrinsicDomain = [0, 100],
                Unit = "%",
                DivergingMidpoint = 50,
                SortOrder = ["low", "high"],
            })),
            new("ChartSize", t => Type(t, "ChartSize"), () => Wire(Input(FullSpec()))["chart_spec"]!["baseSize"]!, () => Wire(Input(FullSpec()))["chart_spec"]!["baseSize"]!),
            new("ChartData (values)", t => UnionMember(t, "ChartData", 0), () => Wire(Input())["data"]!, () => Wire(Input())["data"]!),
            new("ChartData (url)", t => UnionMember(t, "ChartData", 1), () => Wire(Input(data: ChartData.FromUrl("https://example.com/rows.json")))["data"]!, () => Wire(Input(data: ChartData.FromUrl("https://example.com/rows.json")))["data"]!),
            new("ContinuousMarkCrossSection (number)", t => UnionMember(t, "ContinuousMarkCrossSection", 0), () => InCrossSection(12.5), () => InCrossSection(12.5)),
            new("ContinuousMarkCrossSection (axes)", t => UnionMember(t, "ContinuousMarkCrossSection", 1), () => InCrossSection(ContinuousMarkCrossSection.Axes(1, 2)), () => InCrossSection(ContinuousMarkCrossSection.Axes(1, 2, 0.3, 1.5, SeriesCountAxis.Auto))),
        ];
        return forms.Select(f => new TestCaseData(f).SetName($"HandWrittenCore_MatchesIr({f.Name})"));
    }

    // source: ir/flint-ir.json types (ChartAssemblyInput, ChartData, ChartSpec, ChartEncoding, SemanticAnnotation, ChartSize,
    // ContinuousMarkCrossSection): wire names, optionality and kinds; a trivial annotation is its string form (design §5.2)
    [TestCaseSource(nameof(Forms))]
    public void HandWrittenCore_MatchesIr(Form form)
    {
        var shape = form.IrShape(CommittedIr.Root["types"]!);
        var minimal = form.Minimal();
        var full = form.Full();

        if (shape["kind"]!.GetValue<string>() != "object")
        {
            Assert.That(full.GetValueKind(), Is.AnyOf(ExpectedKinds(shape)), "JSON kind of the whole value");
            return;
        }

        // A member typed `never` marks the other member of a {values} | {url} union; it is never written.
        var props = shape["props"]!.AsArray().Where(p => p!["type"]!["kind"]!.GetValue<string>() != "never").ToArray();
        var all = props.Select(p => p!["name"]!.GetValue<string>()).ToArray();
        var required = props.Where(p => !p!["optional"]!.GetValue<bool>()).Select(p => p!["name"]!.GetValue<string>()).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Keys(full), Is.EquivalentTo(all), "members written when every member is set");
            Assert.That(Keys(minimal), Is.EquivalentTo(required), "members written when only the required ones are set");
            foreach (var p in props)
            {
                var name = p!["name"]!.GetValue<string>();
                var value = full.GetValueKind() == JsonValueKind.Object ? full[name] : null;
                if (value is not null)
                {
                    Assert.That(value.GetValueKind(), Is.AnyOf(ExpectedKinds(p["type"]!)), $"JSON kind of {name}");
                }
            }
        }
    }

    /// <summary>The written member names; the string form of an annotation stands for <c>{"semanticType":…}</c>.</summary>
    private static string[] Keys(JsonNode node) =>
        node.GetValueKind() == JsonValueKind.String ? ["semanticType"] : node.AsObject().Select(p => p.Key).ToArray();

    private static object[] ExpectedKinds(JsonNode irType) => irType["kind"]!.GetValue<string>() switch
    {
        "string" or "literalUnion" => [JsonValueKind.String],
        "number" => [JsonValueKind.Number],
        "boolean" => [JsonValueKind.True, JsonValueKind.False],
        "array" or "tuple" => [JsonValueKind.Array],
        "object" or "record" => [JsonValueKind.Object],
        _ => Enum.GetValues<JsonValueKind>().Cast<object>().ToArray(),
    };
}
