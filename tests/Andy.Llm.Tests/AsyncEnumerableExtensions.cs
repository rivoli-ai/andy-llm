using System.Runtime.CompilerServices;

namespace Andy.Llm.Tests;

/// <summary>
/// Helper extension methods for testing async enumerables
/// </summary>
public static class AsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        this IEnumerable<T> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in source)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            yield return item;
            await Task.Yield();
        }
    }
}
