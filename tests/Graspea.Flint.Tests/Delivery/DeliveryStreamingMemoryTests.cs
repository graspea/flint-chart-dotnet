using System.Runtime.CompilerServices;
using Graspea.Flint.Delivery;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Delivery;

[Category("Memory")]
public sealed class DeliveryStreamingMemoryTests
{
    private const int Rows = 1_000_000;
    private const int SampleEvery = 100_000;

    private static long LiveBytes()
    {
        GC.GetTotalMemory(forceFullCollection: true);
        var info = GC.GetGCMemoryInfo();
        return info.HeapSizeBytes - info.FragmentedBytes;
    }

    private static async IAsyncEnumerable<Reading> Generate(long[] samples, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < Rows; i++)
        {
            if (i > 0 && i % SampleEvery == 0)
            {
                samples[i / SampleEvery - 1] = LiveBytes();
            }

            if (i % 1000 == 0)
            {
                await Task.Yield();
            }

            yield return new Reading("Station " + (i % 50), i * 0.25);
        }
    }

    // source: §14.8.5 step 8a (flat allocations); StreamingMemoryTests.cs:16-89 pattern
    [Test]
    public async Task Page_MillionStreamedRows_MemoryDoesNotGrow()
    {
        var samples = new long[Rows / SampleEvery - 1];
        var handle = new ChartHandle<Reading>("r", Chart.Line(DeliveryJson.Default.Reading).X(r => r.Station).Y(r => r.Value).ToTemplate());
        var stream = new RecordingStream(asyncOnly: true, keep: false);
        await new DataPage(new ChartGroup("g", handle), [handle.Stream(Generate(samples))]).WriteAsync(stream);

        Assert.That(stream.Received, Is.GreaterThan(Rows * 40L));
        Assert.That(samples, Has.All.GreaterThan(0));
        Assert.That(samples.Max(), Is.LessThanOrEqualTo(samples[0] + 1024 * 1024), string.Join(", ", samples));
    }
}
