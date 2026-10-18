using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Rows;

public sealed class RowSourceTests
{
    // source: design §13.4 (FromBatches flattens batches in order; empty batches allowed)
    [Test]
    public async Task FromBatches_FlattensInOrderAndSkipsEmptyBatches()
    {
        IReadOnlyList<int>[] batches = [[1, 2], [], [3], [], [4, 5, 6]];
        var rows = new List<int>();
        await foreach (var row in RowSource.FromBatches(AsyncRows.Of(batches)))
        {
            rows.Add(row);
        }

        Assert.That(rows, Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6 }));
    }

    // source: design §13.4 (the consumer's cancellation token is forwarded to the batch source, [EnumeratorCancellation])
    [Test]
    public async Task FromBatches_ForwardsTheConsumersToken()
    {
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<int>[] batches = [[1]];
        var source = AsyncRows.Of(batches);
        await foreach (var _ in RowSource.FromBatches(source).WithCancellation(cancellation.Token))
        {
        }

        Assert.That(source.Token, Is.EqualTo(cancellation.Token));
    }

    // source: design §13.9 (each enumeration enumerates the batch source again; nothing is cached)
    [Test]
    public async Task FromBatches_EachEnumerationReadsTheSourceAgain()
    {
        IReadOnlyList<int>[] batches = [[1, 2]];
        var source = AsyncRows.Of(batches);
        var rows = RowSource.FromBatches(source);
        Assert.That((await ChartData.FromRowsAsync(rows)).Values, Has.Count.EqualTo(2));
        Assert.That((await ChartData.FromRowsAsync(rows)).Values, Has.Count.EqualTo(2));
        Assert.That(source.Enumerations, Is.EqualTo(2));
    }

    // source: design §13.4 (argument check)
    [Test]
    public void FromBatches_Null_Throws()
    {
        Assert.That(() => RowSource.FromBatches<int>(null!), Throws.ArgumentNullException);
    }
}
