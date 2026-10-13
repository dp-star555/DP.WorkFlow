#if NETFRAMEWORK
using System.Collections.Concurrent;

namespace DP.WorkFlow;

/// <summary>
/// .NET Framework 4.8 缺少的实例方法，以同名扩展方法补齐，调用处与 .NET 8 写法一致。只在 4.8 目标上编译。
/// </summary>
internal static class NetFrameworkExtensions
{
    public static string[] Split(this string text, char separator, StringSplitOptions options) =>
        text.Split(new[] { separator }, options);

    public static string[] Split(this string text, char separator, int count, StringSplitOptions options = StringSplitOptions.None) =>
        text.Split(new[] { separator }, count, options);

    public static string[] Split(this string text, string separator, StringSplitOptions options = StringSplitOptions.None) =>
        text.Split(new[] { separator }, options);

    public static bool Contains(this string text, string value, StringComparison comparison) =>
        text.IndexOf(value, comparison) >= 0;

    public static bool Contains(this string text, char value) => text.IndexOf(value) >= 0;

    public static string ReplaceLineEndings(this string text, string replacementText) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", replacementText);

    public static string ReplaceLineEndings(this string text) => text.ReplaceLineEndings(Environment.NewLine);

    public static bool StartsWith(this string text, char value) => text.Length > 0 && text[0] == value;

    public static bool EndsWith(this string text, char value) => text.Length > 0 && text[text.Length - 1] == value;

    public static int IndexOf(this string text, char value, StringComparison comparison) =>
        text.IndexOf(value.ToString(), comparison);

    public static string Replace(this string text, string oldValue, string? newValue, StringComparison comparison)
    {
        if (comparison == StringComparison.Ordinal) return text.Replace(oldValue, newValue);
        var builder = new System.Text.StringBuilder(text.Length);
        var start = 0;
        int index;
        while ((index = text.IndexOf(oldValue, start, comparison)) >= 0)
        {
            builder.Append(text, start, index - start).Append(newValue);
            start = index + oldValue.Length;
        }
        return builder.Append(text, start, text.Length - start).ToString();
    }

    public static int GetHashCode(this string text, StringComparison comparison) => comparison switch
    {
        StringComparison.Ordinal => StringComparer.Ordinal.GetHashCode(text),
        StringComparison.OrdinalIgnoreCase => StringComparer.OrdinalIgnoreCase.GetHashCode(text),
        StringComparison.CurrentCulture => StringComparer.CurrentCulture.GetHashCode(text),
        StringComparison.CurrentCultureIgnoreCase => StringComparer.CurrentCultureIgnoreCase.GetHashCode(text),
        StringComparison.InvariantCulture => StringComparer.InvariantCulture.GetHashCode(text),
        _ => StringComparer.InvariantCultureIgnoreCase.GetHashCode(text)
    };

    public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value) where TKey : notnull
    {
        if (dictionary.ContainsKey(key)) return false;
        dictionary.Add(key, value);
        return true;
    }

    public static bool Remove<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, out TValue value) where TKey : notnull
    {
        if (!dictionary.TryGetValue(key, out value!)) return false;
        dictionary.Remove(key);
        return true;
    }

    public static TValue? GetValueOrDefault<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dictionary, TKey key) =>
        dictionary.TryGetValue(key, out var value) ? value : default;

    public static TValue GetValueOrDefault<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue) =>
        dictionary.TryGetValue(key, out var value) ? value : defaultValue;

    public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
    {
        key = pair.Key;
        value = pair.Value;
    }

    public static bool TryDequeue<T>(this Queue<T> queue, out T result)
    {
        if (queue.Count == 0) { result = default!; return false; }
        result = queue.Dequeue();
        return true;
    }

    public static bool TryPeek<T>(this Queue<T> queue, out T result)
    {
        if (queue.Count == 0) { result = default!; return false; }
        result = queue.Peek();
        return true;
    }

    public static bool TryPop<T>(this Stack<T> stack, out T result)
    {
        if (stack.Count == 0) { result = default!; return false; }
        result = stack.Pop();
        return true;
    }

    public static bool TryPeek<T>(this Stack<T> stack, out T result)
    {
        if (stack.Count == 0) { result = default!; return false; }
        result = stack.Peek();
        return true;
    }

    public static bool TryRemove<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dictionary, KeyValuePair<TKey, TValue> item) where TKey : notnull =>
        ((ICollection<KeyValuePair<TKey, TValue>>)dictionary).Remove(item);

    public static IEnumerable<TSource> DistinctBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector,
        IEqualityComparer<TKey>? comparer = null)
    {
        var seen = new HashSet<TKey>(comparer);
        foreach (var item in source)
            if (seen.Add(keySelector(item)))
                yield return item;
    }

    public static TSource? MaxBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector) =>
        ExtremeBy(source, keySelector, 1);

    public static TSource? MinBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector) =>
        ExtremeBy(source, keySelector, -1);

    private static TSource? ExtremeBy<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector, int sign)
    {
        var comparer = Comparer<TKey>.Default;
        using var enumerator = source.GetEnumerator();
        if (!enumerator.MoveNext()) return default;
        var best = enumerator.Current;
        var bestKey = keySelector(best);
        while (enumerator.MoveNext())
        {
            var key = keySelector(enumerator.Current);
            if (comparer.Compare(key, bestKey) * sign > 0)
            {
                best = enumerator.Current;
                bestKey = key;
            }
        }
        return best;
    }

    public static IEnumerable<TSource[]> Chunk<TSource>(this IEnumerable<TSource> source, int size)
    {
        if (size < 1) throw new ArgumentOutOfRangeException(nameof(size));
        var chunk = new List<TSource>(size);
        foreach (var item in source)
        {
            chunk.Add(item);
            if (chunk.Count == size)
            {
                yield return chunk.ToArray();
                chunk.Clear();
            }
        }
        if (chunk.Count > 0) yield return chunk.ToArray();
    }

    public static IEnumerable<TSource> TakeLast<TSource>(this IEnumerable<TSource> source, int count)
    {
        var buffer = new Queue<TSource>();
        foreach (var item in source)
        {
            buffer.Enqueue(item);
            if (buffer.Count > count) buffer.Dequeue();
        }
        return count <= 0 ? Enumerable.Empty<TSource>() : buffer;
    }

    public static IEnumerable<TSource> SkipLast<TSource>(this IEnumerable<TSource> source, int count)
    {
        var buffer = new Queue<TSource>();
        foreach (var item in source)
        {
            buffer.Enqueue(item);
            if (buffer.Count > count) yield return buffer.Dequeue();
        }
    }

    public static IEnumerable<(TFirst First, TSecond Second)> Zip<TFirst, TSecond>(this IEnumerable<TFirst> first, IEnumerable<TSecond> second) =>
        first.Zip(second, (a, b) => (a, b));

    public static HashSet<TSource> ToHashSet<TSource>(this IEnumerable<TSource> source, IEqualityComparer<TSource>? comparer) =>
        new(source, comparer);

    public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>> source,
        IEqualityComparer<TKey>? comparer = null) where TKey : notnull =>
        source.ToDictionary(pair => pair.Key, pair => pair.Value, comparer);

    public static Task WaitAsync(this Task task, CancellationToken cancellationToken) =>
        task.WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken);

    public static Task WaitAsync(this Task task, TimeSpan timeout) =>
        task.WaitAsync(timeout, CancellationToken.None);

    public static async Task WaitAsync(this Task task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (task.IsCompleted || (timeout == Timeout.InfiniteTimeSpan && !cancellationToken.CanBeCanceled))
        {
            await task.ConfigureAwait(false);
            return;
        }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = Task.Delay(timeout, cancellation.Token);
        var completed = await Task.WhenAny(task, delay).ConfigureAwait(false);
        if (completed == task)
        {
            cancellation.Cancel();
            await task.ConfigureAwait(false);
            return;
        }
        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException();
    }

    public static Task<TResult> WaitAsync<TResult>(this Task<TResult> task, CancellationToken cancellationToken) =>
        task.WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken);

    public static Task<TResult> WaitAsync<TResult>(this Task<TResult> task, TimeSpan timeout) =>
        task.WaitAsync(timeout, CancellationToken.None);

    public static async Task<TResult> WaitAsync<TResult>(this Task<TResult> task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await ((Task)task).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        return await task.ConfigureAwait(false);
    }

    public static Task CancelAsync(this CancellationTokenSource source)
    {
        source.Cancel();
        return Task.CompletedTask;
    }
}
#endif
