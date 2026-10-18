using System.Runtime.CompilerServices;

namespace Graspea.Flint.Tests.Support;

/// <summary>Asynchronous row sources for the tests: each yields asynchronously and records what the consumer did.</summary>
public sealed class AsyncRows<T>(IEnumerable<T> rows, Action<int>? beforeRow = null) : IAsyncEnumerable<T>
{
    /// <summary>The rows handed out so far, over every enumeration.</summary>
    public int Yielded { get; private set; }

    public int Enumerations { get; private set; }

    public int Disposals { get; private set; }

    public CancellationToken Token { get; private set; }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        Enumerations++;
        Token = cancellationToken;
        return Iterate(cancellationToken);
    }

    private async IAsyncEnumerator<T> Iterate(CancellationToken cancellationToken)
    {
        try
        {
            var index = 0;
            foreach (var row in rows)
            {
                await Task.Yield();
                beforeRow?.Invoke(index++);
                Yielded++;
                yield return row;
            }
        }
        finally
        {
            Disposals++;
        }
    }
}

public static class AsyncRows
{
    public static AsyncRows<T> Of<T>(IEnumerable<T> rows, Action<int>? beforeRow = null) => new(rows, beforeRow);

    /// <summary>A plain async iterator over the rows.</summary>
    public static async IAsyncEnumerable<T> Iterate<T>(IEnumerable<T> rows, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var row in rows)
        {
            await Task.Yield();
            yield return row;
        }
    }
}
