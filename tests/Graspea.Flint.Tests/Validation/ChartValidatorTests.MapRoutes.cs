using System.Text.Json.Nodes;
using Graspea.Flint.Validation;

namespace Graspea.Flint.Tests.Validation;

public sealed partial class ChartValidatorTests
{
    /// <summary>A Map of two points (a stop index and a width each) with the given encodings.</summary>
    private static ChartAssemblyInput RouteMap(params (string Channel, EncodingValue Value)[] encodings)
    {
        var input = new ChartAssemblyInput
        {
            Data = ChartData.FromJson([
                new JsonObject { ["lon"] = 10, ["lat"] = 50, ["stop"] = 1, ["width"] = 1 },
                new JsonObject { ["lon"] = 14, ["lat"] = 52, ["stop"] = 2, ["width"] = 2 },
            ]),
            ChartSpec = new ChartSpec { ChartType = ChartType.Map },
        };
        foreach (var (channel, value) in encodings)
        {
            input.ChartSpec.Encodings[channel] = value;
        }

        return input;
    }

    private static string[] RouteWarnings(ChartAssemblyInput input, FlintBackend? backend) =>
        Issues(input, backend, "map_width_without_routes").Select(i => $"{i.Severity} {i.Path}").ToArray();

    // source: flint-chart 1.1.1 src/vegalite/templates/map.ts:1042-1051 (without routes the Vega-Lite Map deletes the
    // strokeWidth encoding and warns map-width-without-routes) with src/vegalite/templates/map-routes.ts:109-115 (no routes
    // unless order, longitude and latitude resolve a field), src/plotly/templates/map.ts:183-190 and :362-366 (the Plotly
    // Map draws nothing without longitude and latitude, and no route without an order field, so the width has no effect);
    // a channel resolves with a non-empty field or a count aggregate (src/vegalite/assemble.ts:1226-1236,
    // src/core/resolve-semantics.ts:643-655); probe case "map with a strokeWidth and no order" of
    // tests/fixtures/flint-chart@1.1.1 (vegalite warns map-width-without-routes, plotly assembles without a warning)
    [Test]
    public void Warnings_MapWidthWithoutRoutes()
    {
        var expected = new[] { "Warning chart_spec.encodings.strokeWidth" };
        string[] none = [];
        using (Assert.EnterMultipleScope())
        {
            foreach (var backend in new FlintBackend?[] { FlintBackend.VegaLite, FlintBackend.Plotly, null })
            {
                Assert.That(RouteWarnings(RouteMap(("longitude", "lon"), ("latitude", "lat"), ("strokeWidth", "width")), backend), Is.EqualTo(expected), $"no order, {backend}");
                Assert.That(RouteWarnings(RouteMap(("longitude", "lon"), ("latitude", "lat"), ("order", "stop"), ("strokeWidth", "width")), backend), Is.EqualTo(none), $"routes, {backend}");
            }

            Assert.That(RouteWarnings(RouteMap(("longitude", "lon"), ("latitude", "lat"), ("order", new ChartEncoding { Aggregate = Aggregate.Count }), ("strokeWidth", "width")), FlintBackend.VegaLite), Is.EqualTo(none), "a count order resolves");
            Assert.That(RouteWarnings(RouteMap(("longitude", "lon"), ("latitude", "lat"), ("order", ""), ("strokeWidth", "width")), FlintBackend.VegaLite), Is.EqualTo(expected), "an empty order field");
            Assert.That(RouteWarnings(RouteMap(("longitude", "lon"), ("latitude", "lat"), ("strokeWidth", new ChartEncoding { Aggregate = Aggregate.Count })), FlintBackend.VegaLite), Is.EqualTo(expected), "a count width");
            Assert.That(RouteWarnings(RouteMap(("longitude", "lon"), ("latitude", "lat"), ("strokeWidth", "breadth")), FlintBackend.VegaLite), Is.EqualTo(expected), "a width field that is not a row key");
            Assert.That(RouteWarnings(RouteMap(("longitude", "lon"), ("latitude", "lat"), ("strokeWidth", "")), FlintBackend.VegaLite), Is.EqualTo(none), "an empty width field");
            Assert.That(RouteWarnings(RouteMap(("latitude", "lat"), ("order", "stop"), ("strokeWidth", "width")), FlintBackend.Plotly), Is.EqualTo(expected), "no longitude");
            Assert.That(RouteWarnings(RouteMap(("longitude", "lon"), ("latitude", "lat"), ("strokeWidth", "width")), FlintBackend.ECharts), Is.EqualTo(none), "no ECharts Map");

            Assert.That(Issues(RouteMap(("longitude", "lon"), ("latitude", "lat"), ("strokeWidth", "width")), null, "map_width_without_routes").Single().Message,
                Is.EqualTo("strokeWidth sets the width of a route's legs, and without order bound the map has none; vegalite, plotly drop it (map-width-without-routes)."));
            Assert.That(Issues(RouteMap(("order", "stop"), ("strokeWidth", "width")), FlintBackend.Plotly, "map_width_without_routes").Single().Message,
                Is.EqualTo("strokeWidth sets the width of a route's legs, and without longitude and latitude bound the map has none; plotly drops it."));
            Assert.That(ChartValidator.Validate(RouteMap(("longitude", "lon"), ("latitude", "lat"), ("strokeWidth", "width")), null).IsValid, Is.True);
        }
    }
}
