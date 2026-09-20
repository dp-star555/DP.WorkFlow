using System.Collections.ObjectModel;

namespace DP.WorkFlow;

/// <summary>工作流应用错误的稳定代码。</summary>
public static class WorkflowErrorCodes
{
    /// <summary>运行轨迹无法导出。</summary>
    public const string RuntimeExportFailed = nameof(RuntimeExportFailed);
}

/// <summary>向表现层传递稳定错误代码、命名参数和技术追踪标识，不携带最终翻译文本。</summary>
public sealed record ApplicationError
{
    /// <summary>创建结构化应用错误。</summary>
    /// <param name="code">稳定机器错误代码。</param>
    /// <param name="arguments">用于本地化模板的命名参数。</param>
    /// <param name="traceId">用于日志关联和技术定位的可选追踪标识。</param>
    public ApplicationError(string code, IReadOnlyDictionary<string, object?>? arguments = null, string? traceId = null)
    {
        Code = string.IsNullOrWhiteSpace(code)
            ? throw new ArgumentException("Error code cannot be empty.", nameof(code))
            : code;
        Arguments = arguments is null ? null : new ReadOnlyDictionary<string, object?>(
            new Dictionary<string, object?>(arguments, StringComparer.Ordinal));
        TraceId = traceId;
    }

    /// <summary>获取稳定机器错误代码。</summary>
    public string Code { get; }
    /// <summary>获取本地化模板使用的命名参数。</summary>
    public IReadOnlyDictionary<string, object?>? Arguments { get; }
    /// <summary>获取日志关联追踪标识。</summary>
    public string? TraceId { get; }
}
