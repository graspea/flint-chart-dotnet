using System.Text.Json;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Model;

public sealed class ContinuousMarkCrossSectionConverterTests
{
    // source: d.ts T:3233 (number form) and design §10.4 test 14 (12.5 <-> 12.5)
    [Test]
    public void Number_RoundTrips()
    {
        ContinuousMarkCrossSection value = 12.5;
        Assert.That(JsonSerializer.Serialize(value), Is.EqualTo("12.5"));
        var read = JsonSerializer.Deserialize<ContinuousMarkCrossSection>("12.5")!;
        Assert.That(read.Uniform, Is.EqualTo(12.5));
        Assert.That(read.X, Is.Null);
    }

    // source: d.ts T:3236-3254 (object form) and design §10.4 test 14 ({x:1,y:2,seriesCountAxis:"auto"} <-> SeriesCountAxis.Auto)
    [Test]
    public void Object_RoundTripsWithSeriesCountAxis()
    {
        var value = ContinuousMarkCrossSection.Axes(1, 2, seriesCountAxis: SeriesCountAxis.Auto);
        JsonAssert.Equal("""{"x":1,"y":2,"seriesCountAxis":"auto"}""", JsonSerializer.Serialize(value));
        var read = JsonSerializer.Deserialize<ContinuousMarkCrossSection>("""{"x":1,"y":2,"elasticity":0.3,"maxStretch":1.5,"seriesCountAxis":"auto"}""")!;
        Assert.That(read.Uniform, Is.Null);
        Assert.That((read.X, read.Y, read.Elasticity, read.MaxStretch), Is.EqualTo((1d, 2d, 0.3, 1.5)));
        Assert.That(read.SeriesCountAxis, Is.EqualTo(SeriesCountAxis.Auto));
    }

    // source: design §5.1 (host-options guarantee; converters ignore the caller's options)
    [Test]
    public void Serialize_UnderHostileOptions_WritesTheSameDocument()
    {
        var value = ContinuousMarkCrossSection.Axes(1, 2, maxStretch: 1.5);
        Assert.That(JsonSerializer.Serialize(value, HostileOptions.Create()), Is.EqualTo(JsonSerializer.Serialize(value)));
        Assert.That(JsonSerializer.Serialize((ContinuousMarkCrossSection)12.5, HostileOptions.Create()), Is.EqualTo("12.5"));
    }

    // source: design §5.2 (unknown keys round-trip through Extensions and are written last)
    [Test]
    public void Object_UnknownKeys_RoundTripAfterKnownMembers()
    {
        var read = JsonSerializer.Deserialize<ContinuousMarkCrossSection>("""{"future":true,"x":1,"y":2}""")!;
        Assert.That(read.Extensions, Is.Not.Null);
        Assert.That(JsonSerializer.Serialize(read), Is.EqualTo("""{"x":1,"y":2,"future":true}"""));
    }

    // source: design §5.1 (a value that is neither a number nor an object is malformed input: JsonException)
    [TestCase("\"wide\"")]
    [TestCase("[1,2]")]
    public void Deserialize_OtherShapes_ThrowJsonException(string json)
    {
        Assert.That((Action)(() => JsonSerializer.Deserialize<ContinuousMarkCrossSection>(json)), Throws.InstanceOf<JsonException>());
    }
}
