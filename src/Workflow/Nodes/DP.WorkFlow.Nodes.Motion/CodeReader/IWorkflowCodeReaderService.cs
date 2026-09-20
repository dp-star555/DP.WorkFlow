namespace DP.WorkFlow;

/// <summary>扫码匹配模式。</summary>
public enum CodeReaderScanMatchMode { Any = 0, Exact = 1, Contains = 2, StartsWith = 3, EndsWith = 4 }

/// <summary>表示一次扫码结果，字段与旧版统一结果对应。</summary>
public sealed record CodeReaderScanResult(string Code, byte[]? RawBytes, DateTime Time, string? SourceAddress)
{
    /// <summary>获取是否包含有效码值。</summary>
    public bool Success => !string.IsNullOrWhiteSpace(Code);
}

/// <summary>描述等待扫码请求。</summary>
public sealed record CodeReaderWaitRequest(string ReaderKey, int TimeoutMs, bool TriggerBeforeWait, bool AutoOpenWhenClosed, CodeReaderScanMatchMode MatchMode, string ExpectedCode);

/// <summary>提供与具体扫码设备无关且保留旧版语义的能力。</summary>
public interface IWorkflowCodeReaderService
{
    /// <summary>打开设备并返回是否成功。</summary>
    ValueTask<bool> OpenAsync(string readerKey, CancellationToken cancellationToken);
    /// <summary>关闭设备并返回是否成功。</summary>
    ValueTask<bool> CloseAsync(string readerKey, CancellationToken cancellationToken);
    /// <summary>按配置自动打开并触发扫码。</summary>
    ValueTask<bool> TriggerAsync(string readerKey, bool autoOpenWhenClosed, CancellationToken cancellationToken);
    /// <summary>按触发、自动打开、匹配模式和超时配置等待扫码。</summary>
    ValueTask<CodeReaderScanResult?> WaitScanAsync(CodeReaderWaitRequest request, CancellationToken cancellationToken);
}
