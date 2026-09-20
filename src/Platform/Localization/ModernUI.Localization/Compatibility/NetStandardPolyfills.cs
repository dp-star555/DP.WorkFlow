#if NETSTANDARD2_0
using System.Collections;

namespace System.Runtime.CompilerServices
{
    internal sealed class IsExternalInit;
}

namespace System.Collections.Generic
{
    /// <summary>为 netstandard2.0 提供与现代 .NET 一致的只读集合契约。</summary>
    public interface IReadOnlySet<T> : IReadOnlyCollection<T>
    {
        bool Contains(T item);
        bool IsProperSubsetOf(IEnumerable<T> other);
        bool IsProperSupersetOf(IEnumerable<T> other);
        bool IsSubsetOf(IEnumerable<T> other);
        bool IsSupersetOf(IEnumerable<T> other);
        bool Overlaps(IEnumerable<T> other);
        bool SetEquals(IEnumerable<T> other);
    }

    internal sealed class ReadOnlySet<T>(HashSet<T> source) : IReadOnlySet<T>
    {
        public int Count => source.Count;
        public bool Contains(T item) => source.Contains(item);
        public bool IsProperSubsetOf(IEnumerable<T> other) => source.IsProperSubsetOf(other);
        public bool IsProperSupersetOf(IEnumerable<T> other) => source.IsProperSupersetOf(other);
        public bool IsSubsetOf(IEnumerable<T> other) => source.IsSubsetOf(other);
        public bool IsSupersetOf(IEnumerable<T> other) => source.IsSupersetOf(other);
        public bool Overlaps(IEnumerable<T> other) => source.Overlaps(other);
        public bool SetEquals(IEnumerable<T> other) => source.SetEquals(other);
        public IEnumerator<T> GetEnumerator() => source.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
#endif
