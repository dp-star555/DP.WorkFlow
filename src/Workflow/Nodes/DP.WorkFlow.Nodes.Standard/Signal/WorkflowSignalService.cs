using System.Collections.Concurrent;

namespace DP.WorkFlow;

/// <summary>提供线程安全、UI 无关的命名布尔信号服务。</summary>
public interface IWorkflowSignalService
{
    /// <summary>读取信号；不存在时返回 <see langword="false"/>。</summary>
    bool Read(string signalKey);

    /// <summary>写入信号并唤醒等待者。</summary>
    void Write(string signalKey, bool value);

    /// <summary>等待信号达到期望状态，匹配返回 true，超时返回 false。</summary>
    ValueTask<bool> WaitAsync(
        string signalKey,
        bool expectedValue,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

/// <summary>默认的进程内线程安全信号服务。</summary>
public sealed class WorkflowSignalService : IWorkflowSignalService
{
    private readonly ConcurrentDictionary<string, SignalState> _signals = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool Read(string signalKey) => GetState(signalKey).Value;

    /// <inheritdoc />
    public void Write(string signalKey, bool value) => GetState(signalKey).Write(value);

    /// <inheritdoc />
    public ValueTask<bool> WaitAsync(
        string signalKey,
        bool expectedValue,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        GetState(signalKey).WaitAsync(expectedValue, timeout, cancellationToken);

    private SignalState GetState(string signalKey)
    {
        if (string.IsNullOrWhiteSpace(signalKey))
            throw new ArgumentException("信号键不能为空。", nameof(signalKey));
        return _signals.GetOrAdd(signalKey.Trim(), static _ => new SignalState());
    }

    private sealed class SignalState
    {
        private readonly object _sync = new();
        private bool _value;
        private TaskCompletionSource _changed = CreateCompletionSource();

        public bool Value
        {
            get { lock (_sync) return _value; }
        }

        public void Write(bool value)
        {
            TaskCompletionSource changed;
            lock (_sync)
            {
                if (_value == value) return;
                _value = value;
                changed = _changed;
                _changed = CreateCompletionSource();
            }
            changed.TrySetResult();
        }

        public async ValueTask<bool> WaitAsync(
            bool expectedValue,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(timeout));
            using var timeoutSource = timeout == Timeout.InfiniteTimeSpan
                ? null
                : new CancellationTokenSource(timeout);
            using var linked = timeoutSource is null
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            while (true)
            {
                Task changedTask;
                lock (_sync)
                {
                    if (_value == expectedValue) return true;
                    changedTask = _changed.Task;
                }
                try
                {
                    await changedTask.WaitAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    timeoutSource?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
            }
        }

        private static TaskCompletionSource CreateCompletionSource() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
