using System.Text.Json.Nodes;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Model;

/// <summary>The preset factories of <see cref="InteractionEntry"/>, typed from flint-chart/interactive InteractionPresetOptions.</summary>
public sealed class InteractionEntryTests
{
    private static string Json(InteractionEntry entry) =>
        FlintJson.Serialize(new ChartAssemblyInput { ChartSpec = new ChartSpec { ChartType = ChartType.BarChart }, InteractionSpec = new InteractionSpec { Interactions = [entry] } });

    // source: flint-chart 1.0.0 d.ts InteractionEntry (the example { "type": "navigate", "options": { "axes": "x", "pan": false } })
    // and dist/interactive InteractionPresetOptions["navigate"] = NavigateOptions
    [Test]
    public void Navigate_WritesTheTypeAndTheOptions()
    {
        var json = Json(InteractionEntry.Navigate(new NavigateOptions { Axes = "x", Pan = false }));
        JsonAssert.Equal("""{"type":"navigate","options":{"axes":"x","pan":false}}""", JsonNode.Parse(json)!["interaction_spec"]!["interactions"]![0]);
    }

    // source: flint-chart 1.0.0 dist/interactive InteractionPresetSpec ({ type; id?; options? }: id lives on the entry and
    // defaults to the type upstream, so null omits it; options without a member set are written as none)
    [Test]
    public void Factory_WritesTheIdOnTheEntryAndNoEmptyOptions()
    {
        var entry = InteractionEntry.BrushX(new SelectOptions(), id: "zoom");
        Assert.That((entry.Type, entry.Id, entry.Options), Is.EqualTo(("brush-x", "zoom", (Dictionary<string, JsonNode?>?)null)));
        Assert.That(InteractionEntry.LegendToggle().Options, Is.Null);
    }

    // source: flint-chart 1.0.0 dist/interactive (hover-group-focus requires groupBy: string | readonly string[], mapped to
    // JsonNode; LassoSelectOptions = Omit<SelectOptions, 'mode'>) and the registry's requiredOptions
    [Test]
    public void Factory_TakesUnionsAsJsonAndRequiresTheOptionsOfARequiredMember()
    {
        var focus = InteractionEntry.HoverGroupFocus(new HoverGroupFocusOptions { GroupBy = new JsonArray("region", "year") });
        JsonAssert.Equal("""{"groupBy":["region","year"]}""", JsonNode.Parse(Json(focus))!["interaction_spec"]!["interactions"]![0]!["options"]);
        Assert.That(typeof(InteractionEntry).GetMethod(nameof(InteractionEntry.HoverGroupFocus))!.GetParameters()[0].HasDefaultValue, Is.False);
        Assert.That(typeof(LassoSelectOptions).GetProperty("Mode"), Is.Null);
        Assert.That(typeof(SelectOptions).GetProperty("Id"), Is.Null);
    }
}
