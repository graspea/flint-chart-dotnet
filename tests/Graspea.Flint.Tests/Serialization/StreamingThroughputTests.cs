using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IAsyncEnumerable<Reading>))]
internal sealed partial class ThroughputContext : JsonSerializerContext;

/// <summary>The non-gating throughput measurement of design §13.11: run it on a release build with
/// <c>dotnet test -c Release -f net10.0 --filter Category=Throughput</c> and record the ratio it prints.</summary>
[Explicit("Measurement, not a gate (design §13.11).")]
[Category("Throughput")]
public sealed class StreamingThroughputTests
{
    private const int Rows = 1_000_000;

    private static async IAsyncEnumerable<Reading> Generate()
    {
        for (var i = 0; i < Rows; i++)
        {
            if (i % 1000 == 0)
            {
                await Task.Yield();
            }

            yield return new Reading(new DateOnly(2024, 1, 1).AddDays(i % 365), i % 2 == 0 ? "Coastal" : "Inland", i * 0.25, i);
        }
    }

    private static async Task<TimeSpan> Time(Func<Stream, Task> write)
    {
        var best = TimeSpan.MaxValue;
        for (var run = 0; run < 5; run++)
        {
            var stream = new RecordingStream(keep: false);
            var watch = Stopwatch.StartNew();
            await write(stream);
            watch.Stop();
            best = watch.Elapsed < best ? watch.Elapsed : best;
        }

        return best;
    }

    // source: design §13.11 (streamed throughput: at most 2x the time of JsonSerializer.SerializeAsync(stream, rows,
    // typeInfo) for the same rows, typeInfo a JsonTypeInfo<IAsyncEnumerable<TRow>> from the test project's context)
    [Test]
    public async Task SerializeAsync_Streamed_ComparedWithSystemTextJson()
    {
        var baseline = await Time(stream => JsonSerializer.SerializeAsync(stream, Generate(), ThroughputContext.Default.IAsyncEnumerableReading));
        var streamed = await Time(stream => FlintJson.SerializeAsync(stream, new ChartAssemblyInput
        {
            Data = ChartData.Stream(Generate(), StreamingContext.Default.Reading),
            ChartSpec = new ChartSpec { ChartType = ChartType.LineChart, Encodings = { ["x"] = "day", ["y"] = "value" } },
        }));
        var ratio = streamed.TotalMilliseconds / baseline.TotalMilliseconds;
        TestContext.Out.WriteLine($"rows={Rows} baseline={baseline.TotalMilliseconds:F0} ms streamed={streamed.TotalMilliseconds:F0} ms ratio={ratio:F2}");
        Assert.That(ratio, Is.LessThanOrEqualTo(2.0));
    }
}
