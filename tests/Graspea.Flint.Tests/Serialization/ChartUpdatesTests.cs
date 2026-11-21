using System.Text.Json;
using System.Text.Json.Nodes;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Serialization;

/// <summary>
/// <c>ChartAssemblyInput.Updates</c> (flint-chart 1.1.0+): the typed update model on the document. U: = flint-chart 1.1.1
/// src/core/types.ts (ChartAssemblyInput.updates at :1446-1456); D: = its d.ts, dist/types-DFtJpR3x.d.ts (ChartUpdateOp at
/// :331-386, ChartUpdate at :389-392).
/// </summary>
public sealed class ChartUpdatesTests
{
    /// <summary>One update with every op kind of D:331-386, a freeform body of each type, and an op this version does not
    /// know.</summary>
    internal const string Document =
        """{"data":{"values":[{"region":"Asia","month":"2025-03","sales":120},{"region":"Europe","month":"2025-04","sales":90},{"region":"Africa","month":"2025-05","sales":60}]},"chart_spec":{"chartType":"Bar Chart","encodings":{"x":{"field":"region"},"y":{"field":"sales"}}},"updates":[{"id":"focus","ops":[{"op":"set-style","targets":[{"select":{"key":{"region":"Asia"}}},{"select":{"where":{"sales":{"range":[80,null]}},"visual":{"kind":"mark","role":"bar"}}}],"value":{"state":"emphasized","mutedOpacity":0.3}},{"op":"annotate-text","target":{"visual":{"kind":"mark","role":"bar"},"elements":[{"value":{"region":"Asia"}}]},"value":{"text":"Peak","anchor":"top","style":{"fontWeight":600}}},{"op":"set-annotation","target":{"select":{"key":{"region":"Europe"}}},"value":null},{"op":"annotate-region","name":"spring","value":{"x":["Asia","Europe"],"text":"Spring","opacity":0.2}},{"op":"annotate-enclosure","name":"pair","value":{"targets":[{"select":{"key":{"region":"Asia"}}},{"select":{"key":{"region":"Europe"}}}],"text":"Both"}},{"op":"set-viewport","axes":"y","value":{"y":[0,150]}},{"op":"set-order","scope":"category","field":"region","values":["Africa","Europe","Asia"]},{"op":"set-overlay","name":"goal","value":{"data":{"values":[{"goal":100}]},"encodings":{"y":{"field":"goal"}},"mark":"rule","style":{"strokeDash":[4,2],"stroke":"#777777"}}},{"op":"set-freeform-overlay","name":"stamp","value":{"coordinateSpace":"plot","body":[{"type":"svg","content":"<circle r=\"4\"/>"},{"type":"clone","targets":[{"select":{"key":{"region":"Asia"}}}],"opacity":0.4},{"type":"glow","radius":3}]}},{"op":"set-data","source":"main","value":{"rows":[{"region":"Asia","month":"2025-06","sales":130}]}},{"op":"set-highlight","target":"Asia"}]}]}""";

    // source: U:1446-1456 (updates?: readonly ChartUpdate[]) and D:331-392: each op reads as the class of its op, an op this
    // version does not know as UnknownChartUpdateOp, and the document is written back as read (design §5.2), with nothing
    // left in Extensions
    [Test]
    public void Deserialize_EveryOpKind_ReadsTypedAndRoundTrips()
    {
        var read = FlintJson.Deserialize(Document);

        Assert.That(read.Extensions, Is.Null);
        Assert.That(read.Updates, Has.Count.EqualTo(1));
        Assert.That(read.Updates![0].Id, Is.EqualTo("focus"));
        Assert.That(read.Updates[0].Ops!.Select(o => o.GetType()), Is.EqualTo(new[]
        {
            typeof(SetStyleOp), typeof(AnnotateTextOp), typeof(SetAnnotationOp), typeof(AnnotateRegionOp), typeof(AnnotateEnclosureOp),
            typeof(SetViewportOp), typeof(SetOrderOp), typeof(SetOverlayOp), typeof(SetFreeformOverlayOp), typeof(SetDataOp), typeof(UnknownChartUpdateOp),
        }));
        Assert.That(((SetFreeformOverlayOp)read.Updates[0].Ops![8]).Value!.Body!.Select(b => b.GetType()),
            Is.EqualTo(new[] { typeof(FreeformSvgBody), typeof(FreeformCloneBody), typeof(UnknownFreeformOverlayBody) }));
        JsonAssert.Equal(Document, FlintJson.Serialize(read));
    }

    // source: U:1446-1456; the member is written after interaction_spec and before the members this version does not model
    [Test]
    public void Serialize_TypedUpdates_WritesTheUpdatesMember()
    {
        var input = new ChartAssemblyInput
        {
            Data = ChartData.FromJson(new JsonArray(new JsonObject { ["region"] = "Asia", ["sales"] = 120 })),
            ChartSpec = new ChartSpec { ChartType = ChartType.BarChart, Encodings = { ["x"] = "region", ["y"] = "sales" } },
            InteractionSpec = new InteractionSpec { Interactions = [] },
            Updates =
            [
                new ChartUpdate
                {
                    Id = "goal",
                    Ops = [new SetOverlayOp { Name = "goal", Value = new ChartOverlaySpec { Mark = "rule", Style = new OverlayStyleSpec { StrokeDash = "dot" } } }],
                },
            ],
            Extensions = new() { ["future"] = JsonDocument.Parse("1").RootElement.Clone() },
        };

        var written = FlintJson.SerializeToNode(input).AsObject();
        Assert.That(written.Select(p => p.Key), Is.EqualTo(new[] { "data", "chart_spec", "interaction_spec", "updates", "future" }));
        Assert.That(written["updates"]!.ToJsonString(),
            Is.EqualTo("""[{"id":"goal","ops":[{"op":"set-overlay","name":"goal","value":{"mark":"rule","style":{"strokeDash":"dot"}}}]}]"""));
    }

    // source: D:331-386 (value: AnnotationSpec | null: a null value clears the note) and docs/generator.md (a required member
    // whose type admits null is written as null when unset)
    [Test]
    public void Serialize_ClearingOp_WritesItsNullValue()
    {
        var input = new ChartAssemblyInput
        {
            ChartSpec = new ChartSpec { ChartType = ChartType.BarChart },
            Updates = [new ChartUpdate { Id = "clear", Ops = [new AnnotateTextOp { Target = UpdateTarget.Key(new() { ["region"] = "Asia" }) }] }],
        };

        Assert.That(FlintJson.SerializeToNode(input)["updates"]!.ToJsonString(),
            Is.EqualTo("""[{"id":"clear","ops":[{"op":"annotate-text","target":{"select":{"key":{"region":"Asia"}}},"value":null}]}]"""));
    }

    // source: flint-chart 1.1.1 src/core/spec-updates.ts:9-15 (reads update.id of every entry, so a null entry throws on the
    // layout backends): the model keeps the null entry for the validator to report
    [Test]
    public void Deserialize_NullEntry_IsKept()
    {
        const string json = """{"chart_spec":{"chartType":"Bar Chart","encodings":{}},"updates":[null,{}]}""";
        var read = FlintJson.Deserialize(json);

        Assert.That(read.Updates, Has.Count.EqualTo(2));
        Assert.That(read.Updates![0], Is.Null);
        Assert.That(FlintJson.Serialize(read), Is.EqualTo(json));
    }

    // source: design §5.1 (a value of another shape is malformed input: JsonException); U:1456 types updates as an array of
    // ChartUpdate objects, each with an op object whose op is a string (D:331-392)
    [TestCase("\"abc\"")]
    [TestCase("""{"length":1}""")]
    [TestCase("[5]")]
    [TestCase("""[{"id":1}]""")]
    [TestCase("""[{"ops":{}}]""")]
    [TestCase("""[{"ops":[{}]}]""")]
    [TestCase("""[{"ops":[{"op":1}]}]""")]
    [TestCase("""[{"ops":["set-style"]}]""")]
    public void Deserialize_OtherShapes_ThrowJsonException(string updates)
    {
        var json = $$"""{"chart_spec":{"chartType":"Bar Chart"},"updates":{{updates}}}""";
        Assert.That((Action)(() => FlintJson.Deserialize(json)), Throws.InstanceOf<JsonException>());
    }
}
