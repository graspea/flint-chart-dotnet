using System.Text.Json.Serialization;

namespace Graspea.Flint;

/// <summary>A size in pixels, written as <c>{"width":…,"height":…}</c> (<c>baseSize</c>, <c>canvasSize</c>, <c>facetFixedPadding</c>).</summary>
/// <param name="Width">The <c>width</c> member.</param>
/// <param name="Height">The <c>height</c> member.</param>
[JsonNumberHandling(JsonNumberHandling.Strict)]
public readonly record struct ChartSize(
    [property: JsonPropertyName("width")] double Width,
    [property: JsonPropertyName("height")] double Height);
