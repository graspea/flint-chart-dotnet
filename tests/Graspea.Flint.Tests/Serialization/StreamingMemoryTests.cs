using System.Runtime.CompilerServices;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Serialization;

[Category("Memory")]
public sealed class StreamingMemoryTests
{
    private const int Rows = 1_000_000;
    private const int SampleEvery = 100_000;

    /// <summary>Live managed bytes after a full, blocking collection. <see cref="GC.GetTotalMemory(bool)"/> forces the
    /// collection; its own result also counts free space between objects, which swings by megabytes between samples in a
    /// full test run, so the heap size of that collection minus its fragmentation is used.</summary>
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

            yield return new Reading(new DateOnly(2024, 1, 1).AddDays(i % 365), i % 2 == 0 ? "Coastal" : "Inland", i * 0.25, i);
        }
    }

    private static IEnumerable<Reading> GenerateSynchronously(long[] samples)
    {
        for (var i = 0; i < Rows; i++)
        {
            if (i > 0 && i % SampleEvery == 0)
            {
                samples[i / SampleEvery - 1] = LiveBytes();
            }

            yield return new Reading(new DateOnly(2024, 1, 1).AddDays(i % 365), i % 2 == 0 ? "Coastal" : "Inland", i * 0.25, i);
        }
    }

    // source: design §13.5 table and §13.11 (Serialize(Stream) with a synchronous source streams through the library's
    // buffer: 1 000 000 rows from a synchronous iterator; managed memory sampled every 100 000 rows stays within the first
    // sample plus 1 MiB)
    [Test]
    public void Serialize_ToStream_MillionStreamedSyncRows_MemoryDoesNotGrow()
    {
        var samples = new long[Rows / SampleEvery - 1];
        var stream = new RecordingStream(keep: false);
        FlintJson.Serialize(stream, new ChartAssemblyInput
        {
            Data = ChartData.Stream(GenerateSynchronously(samples), StreamingContext.Default.Reading),
            ChartSpec = new ChartSpec { ChartType = ChartType.LineChart, Encodings = { ["x"] = "day", ["y"] = "value" } },
        });

        Assert.That(stream.Received, Is.GreaterThan(Rows * 40L));
        Assert.That(samples, Has.All.GreaterThan(0));
        Assert.That(samples.Max(), Is.LessThanOrEqualTo(samples[0] + 1024 * 1024), string.Join(", ", samples));
    }

    // source: design §13.11 (streamed path: 1 000 000 rows from an async iterator to a counting stream; managed memory
    // sampled every 100 000 rows stays within the first sample plus 1 MiB)
    [Test]
    public async Task SerializeAsync_MillionStreamedRows_MemoryDoesNotGrow()
    {
        var samples = new long[Rows / SampleEvery - 1];
        var stream = new RecordingStream(keep: false);
        await FlintJson.SerializeAsync(stream, new ChartAssemblyInput
        {
            Data = ChartData.Stream(Generate(samples), StreamingContext.Default.Reading),
            ChartSpec = new ChartSpec { ChartType = ChartType.LineChart, Encodings = { ["x"] = "day", ["y"] = "value" } },
        });

        Assert.That(stream.Received, Is.GreaterThan(Rows * 40L));
        Assert.That(samples, Has.All.GreaterThan(0));
        Assert.That(samples.Max(), Is.LessThanOrEqualTo(samples[0] + 1024 * 1024), string.Join(", ", samples));
    }
}
