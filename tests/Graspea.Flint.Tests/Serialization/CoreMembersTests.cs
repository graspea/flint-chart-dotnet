using System.Text.Json.Nodes;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Serialization;

// D: = the flint-chart 1.0.0 d.ts, dist/types-B9fiOPP4.d.ts.
public sealed class CoreMembersTests
{
    private const string Document =
        """{"data":{"values":[{"region":"North","margin":4}]},"semantic_types":{"margin":{"semanticType":"Percentage","colors":{"North":"#0793AB"},"polarity":"higher-is-better"}},"chart_spec":{"chartType":"Bar Chart","label":"Figure 2","encodings":{"x":{"field":"region","title":null},"color":{"field":"margin","scheme":["#c0392b","#f2f2f2","#2e75b6"],"title":"Margin","reverse":true,"mid":0,"domain":[-10,10],"bins":5,"colors":{"Win":"#2a9d8f"}}},"source":{"text":"Open data","href":"https://example.com/data"},"chrome":false},"interaction_spec":{"assistedTargeting":{"details":{"fields":["region"],"maxRows":3},"maxDistance":24},"interactions":[{"options":{"axes":"x","pan":false},"type":"navigate"}],"keyboardTargeting":true}}""";

    private static ChartAssemblyInput Input() => new()
    {
        Data = ChartData.FromJson(new JsonArray(new JsonObject { ["region"] = "North", ["margin"] = 4 })),
        SemanticTypes = new()
        {
            ["margin"] = new SemanticAnnotation { Type = SemanticType.Percentage, Colors = new() { ["North"] = "#0793AB" }, Polarity = Polarity.HigherIsBetter },
        },
        ChartSpec = new ChartSpec
        {
            ChartType = ChartType.BarChart,
            Label = "Figure 2",
            Encodings =
            {
                ["x"] = new ChartEncoding { Field = "region", Title = ChannelTitle.None },
                ["color"] = new ChartEncoding { Field = "margin", ColorScheme = new[] { "#c0392b", "#f2f2f2", "#2e75b6" }, Title = "Margin", Reverse = true, Mid = 0, Domain = [-10, 10], Bins = 5, Colors = new() { ["Win"] = "#2a9d8f" } },
            },
            Source = new ChartSource { Text = "Open data", Href = "https://example.com/data" },
            Chrome = ChartChrome.None,
        },
        InteractionSpec = new InteractionSpec
        {
            AssistedTargeting = new AssistedTargeting { Details = new TargetDetails { Fields = ["region"], MaxRows = 3 }, MaxDistance = 24 },
            Interactions = [new InteractionEntry { Type = "navigate", Options = new() { ["axes"] = "x", ["pan"] = false } }],
            KeyboardTargeting = true,
        },
    };

    // source: D:4776-4909 (ChartAssemblyInput: interaction_spec; chart_spec label, source, chrome), D:3619-3644 (ChartEncoding
    // title, reverse, mid, domain, bins, colors), D:2842-2849 (SemanticAnnotation colors, polarity), D:3214-3246
    // (InteractionSpec, InteractionEntry, AssistedTargetingOptions, TargetDetailsOptions); wire names verbatim
    [Test]
    public void Serialize_Upstream100CoreMembers_WritesTheirWireNames()
    {
        JsonAssert.Equal(Document, FlintJson.Serialize(Input()));
    }

    // source: design §5.2 (a document round-trips byte-identical: known members in model order, each union in the form it
    // was read)
    [Test]
    public void Deserialize_Upstream100CoreMembers_RoundTripsByteIdentical()
    {
        var read = FlintJson.Deserialize(Document);
        Assert.That(FlintJson.Serialize(read), Is.EqualTo(Document));
        Assert.That(read.ChartSpec.Encodings["x"].Encodings[0].Title, Is.SameAs(ChannelTitle.None));
        Assert.That(read.InteractionSpec!.Interactions![0].Options!["pan"]!.GetValue<bool>(), Is.False);
    }
}
