#if NETFRAMEWORK
using System.Runtime.CompilerServices;

namespace DP.WorkFlow
{
    /// <summary>.NET 5 起 BCL 自带的按引用比较器；4.8 上的同名实现，只在 4.8 目标上编译。</summary>
    internal sealed class ReferenceEqualityComparer : IEqualityComparer<object?>, System.Collections.IEqualityComparer
    {
        private ReferenceEqualityComparer() { }

        public static ReferenceEqualityComparer Instance { get; } = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object? obj) => RuntimeHelpers.GetHashCode(obj!);
    }

    /// <summary>.NET 5 起 BCL 自带的无结果 <c>TaskCompletionSource</c>；4.8 上包装 <see cref="TaskCompletionSource{TResult}"/>。</summary>
    internal sealed class TaskCompletionSource
    {
        private readonly TaskCompletionSource<bool> _inner;

        public TaskCompletionSource() => _inner = new TaskCompletionSource<bool>();

        public TaskCompletionSource(TaskCreationOptions creationOptions) => _inner = new TaskCompletionSource<bool>(creationOptions);

        public Task Task => _inner.Task;

        public void SetResult() => _inner.SetResult(true);

        public bool TrySetResult() => _inner.TrySetResult(true);

        public void SetCanceled() => _inner.SetCanceled();

        public bool TrySetCanceled() => _inner.TrySetCanceled();

        public bool TrySetCanceled(CancellationToken cancellationToken) => _inner.TrySetCanceled(cancellationToken);

        public void SetException(Exception exception) => _inner.SetException(exception);

        public bool TrySetException(Exception exception) => _inner.TrySetException(exception);
    }

    /// <summary>.NET 6 起 BCL 自带的最小堆优先队列；4.8 上只实现工作流用到的成员（二叉堆，优先级小者先出）。</summary>
    internal sealed class PriorityQueue<TElement, TPriority>
    {
        private readonly List<(TElement Element, TPriority Priority)> _heap = new();
        private readonly IComparer<TPriority> _comparer;

        public PriorityQueue() : this(null) { }

        public PriorityQueue(IComparer<TPriority>? comparer) => _comparer = comparer ?? Comparer<TPriority>.Default;

        public int Count => _heap.Count;

        public void Enqueue(TElement element, TPriority priority)
        {
            _heap.Add((element, priority));
            var index = _heap.Count - 1;
            while (index > 0)
            {
                var parent = (index - 1) / 2;
                if (_comparer.Compare(_heap[index].Priority, _heap[parent].Priority) >= 0) break;
                (_heap[index], _heap[parent]) = (_heap[parent], _heap[index]);
                index = parent;
            }
        }

        public TElement Dequeue() =>
            TryDequeue(out var element, out _) ? element : throw new InvalidOperationException("队列为空。");

        public bool TryDequeue(out TElement element, out TPriority priority)
        {
            if (_heap.Count == 0)
            {
                element = default!;
                priority = default!;
                return false;
            }
            (element, priority) = _heap[0];
            var last = _heap[_heap.Count - 1];
            _heap.RemoveAt(_heap.Count - 1);
            if (_heap.Count > 0)
            {
                _heap[0] = last;
                var index = 0;
                while (true)
                {
                    var left = index * 2 + 1;
                    if (left >= _heap.Count) break;
                    var right = left + 1;
                    var smallest = right < _heap.Count && _comparer.Compare(_heap[right].Priority, _heap[left].Priority) < 0 ? right : left;
                    if (_comparer.Compare(_heap[smallest].Priority, _heap[index].Priority) >= 0) break;
                    (_heap[index], _heap[smallest]) = (_heap[smallest], _heap[index]);
                    index = smallest;
                }
            }
            return true;
        }

        public bool TryPeek(out TElement element, out TPriority priority)
        {
            if (_heap.Count == 0)
            {
                element = default!;
                priority = default!;
                return false;
            }
            (element, priority) = _heap[0];
            return true;
        }

        public void Clear() => _heap.Clear();
    }
}
#endif
