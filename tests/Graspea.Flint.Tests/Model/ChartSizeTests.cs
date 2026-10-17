using System.Text.Json;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Model;

public sealed class ChartSizeTests
{
    // source: d.ts T:2976-2983 (baseSize: {width, height}) and design §10.4 test 14
    [Test]
    public void Serialize_WithoutNamingPolicy_WritesWidthAndHeight()
    {
        var json = JsonSerializer.Serialize(new ChartSize(800, 400), new JsonSerializerOptions { PropertyNamingPolicy = null });
        Assert.That(json, Is.EqualTo("""{"width":800,"height":400}"""));
    }

    // source: design §5.1 (host-options guarantee: wire names and Strict number handling live on the type)
    [Test]
    public void Serialize_UnderHostileOptions_WritesTheSameDocument()
    {
        Assert.That(JsonSerializer.Serialize(new ChartSize(800, 400), HostileOptions.Create()), Is.EqualTo("""{"width":800,"height":400}"""));
    }

    // source: design §4.1 (ChartSize is a value: it reads back equal)
    [Test]
    public void Deserialize_RoundTrips()
    {
        Assert.That(JsonSerializer.Deserialize<ChartSize>("""{"width":480.5,"height":280}"""), Is.EqualTo(new ChartSize(480.5, 280)));
    }
}
