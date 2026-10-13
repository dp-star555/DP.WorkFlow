using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace DP.WorkFlow;

/// <summary>
/// 参数与对象状态检查。.NET 8 上转给 <c>ArgumentNullException.ThrowIfNull</c> 等 BCL 方法，
/// .NET Framework 4.8 没有这些方法，在这里实现同样的行为（异常类型、参数名一致）。
/// </summary>
internal static class Guard
{
    /// <summary>参数为空时抛出 <see cref="ArgumentNullException"/>。</summary>
    public static void NotNull([NotNull] object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
#if NETFRAMEWORK
        if (argument is null) throw new ArgumentNullException(paramName);
#else
        ArgumentNullException.ThrowIfNull(argument, paramName);
#endif
    }

    /// <summary>参数为空时抛出 <see cref="ArgumentNullException"/>，为空字符串或只含空白时抛出 <see cref="ArgumentException"/>。</summary>
    public static void NotNullOrWhiteSpace([NotNull] string? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
#if NETFRAMEWORK
        if (argument is null) throw new ArgumentNullException(paramName);
        if (string.IsNullOrWhiteSpace(argument)) throw new ArgumentException("值不能为空字符串或只包含空白。", paramName);
#else
        ArgumentException.ThrowIfNullOrWhiteSpace(argument, paramName);
#endif
    }

    /// <summary>对象已释放时抛出 <see cref="ObjectDisposedException"/>。</summary>
    public static void NotDisposed([DoesNotReturnIf(true)] bool disposed, object instance)
    {
#if NETFRAMEWORK
        if (disposed) throw new ObjectDisposedException(instance?.GetType().FullName);
#else
        ObjectDisposedException.ThrowIf(disposed, instance);
#endif
    }
}
