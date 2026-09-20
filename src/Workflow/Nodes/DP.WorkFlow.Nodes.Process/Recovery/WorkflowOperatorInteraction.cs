namespace DP.WorkFlow;

/// <summary>工程师预设的人工交互形态。</summary>
public enum WorkflowOperatorPromptKind
{
    /// <summary>从配置选项中选择一项。</summary>
    Choice,
    /// <summary>确认已完成当前人工步骤，不等于设备条件已验证。</summary>
    Confirmation
}

/// <summary>当前人工任务的只读快照；响应必须携带此任务身份。</summary>
/// <param name="Id">本次任务身份，旧窗口不能响应新任务。</param>
/// <param name="Kind">选择或步骤确认。</param>
/// <param name="Title">标题。</param>
/// <param name="Message">工程师配置的操作指引。</param>
/// <param name="Options">独立只读的选择集合；确认任务为空。</param>
public sealed record WorkflowOperatorPrompt(Guid Id, WorkflowOperatorPromptKind Kind,
    string Title, string Message, IReadOnlyList<OperatorChoiceOption> Options);

/// <summary>人员关闭了窗口但没有确认或选择；不得当作默认同意。</summary>
public sealed class WorkflowOperatorPromptDismissedException : InvalidOperationException
{
    /// <summary>创建未作出确认的人工任务异常。</summary>
    public WorkflowOperatorPromptDismissedException() : base("人工处理窗口已关闭，未提交选择或确认。") { }
}

/// <summary>
/// UI无关的人工任务控制器。同一实例串行展示任务，排队与当前任务均可取消，响应按身份去重。
/// 不执行设备动作、不选择恢复入口；桌面适配器只呈现CurrentPrompt并提交响应。
/// </summary>
public sealed class WorkflowOperatorInteraction : IWorkflowOperatorService, IDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private PendingPrompt? _current;
    private bool _disposed;

    /// <summary>当前任务变化通知；订阅方应重新读取CurrentPrompt，不依赖事件到达顺序。</summary>
    public event EventHandler? PromptChanged;

    /// <summary>当前尚未完成的任务；没有任务或已取消时为空。</summary>
    public WorkflowOperatorPrompt? CurrentPrompt
    {
        get
        {
            lock (_sync)
                return !_disposed && _current is { } pending && !pending.Completion.Task.IsCompleted && !pending.Token.IsCancellationRequested
                    ? pending.Prompt : null;
        }
    }

    /// <inheritdoc />
    public async ValueTask<string> AskChoiceAsync(string title, string message,
        IReadOnlyList<OperatorChoiceOption> options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Count < 2 || options.Count > 32)
            throw new ArgumentException("人工选择须包含2至32个选项，建议设备人员仅配置少量必要方案。", nameof(options));
        var copy = options.Select(option => option is null || string.IsNullOrWhiteSpace(option.Key) || string.IsNullOrWhiteSpace(option.Text)
            ? throw new ArgumentException("选项键与显示名称不能为空。", nameof(options))
            : new OperatorChoiceOption(option.Key, option.Text)).ToArray();
        if (copy.Select(option => option.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != copy.Length)
            throw new ArgumentException("选项键不能重复。", nameof(options));
        var result = await RequestAsync(new WorkflowOperatorPrompt(Guid.NewGuid(), WorkflowOperatorPromptKind.Choice,
            title ?? string.Empty, message ?? string.Empty, Array.AsReadOnly(copy)), cancellationToken).ConfigureAwait(false);
        return result!;
    }

    /// <inheritdoc />
    public async ValueTask ConfirmStepAsync(string title, string message, CancellationToken cancellationToken) =>
        _ = await RequestAsync(new WorkflowOperatorPrompt(Guid.NewGuid(), WorkflowOperatorPromptKind.Confirmation,
            title ?? string.Empty, message ?? string.Empty, Array.Empty<OperatorChoiceOption>()), cancellationToken).ConfigureAwait(false);

    /// <summary>提交配置内选项；返回true仅表示本次响应被接受。</summary>
    /// <param name="promptId">窗口持有的任务身份。</param>
    /// <param name="key">工程师配置的选项键；匹配不区分大小写，返回值使用配置键。</param>
    /// <returns>任务过期、已取消、重复响应或选项无效时为false。</returns>
    public bool TryChoose(Guid promptId, string key)
    {
        bool accepted;
        lock (_sync)
        {
            if (!CanRespond(promptId, WorkflowOperatorPromptKind.Choice)) return false;
            var option = _current!.Prompt.Options.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase));
            accepted = option is not null && _current.Completion.TrySetResult(option.Key);
        }
        if (accepted) PublishChanged();
        return accepted;
    }

    /// <summary>提交当前人工步骤确认，不代替后续工位校验。</summary>
    /// <param name="promptId">当前任务身份。</param>
    /// <returns>确认首次被接受时为true，过期、类型不符或取消时为false。</returns>
    public bool TryConfirm(Guid promptId)
    {
        bool accepted;
        lock (_sync)
            accepted = CanRespond(promptId, WorkflowOperatorPromptKind.Confirmation) && _current!.Completion.TrySetResult(null);
        if (accepted) PublishChanged();
        return accepted;
    }

    /// <summary>关闭当前提示且不确认。调用任务以明确的未确认异常结束，不选择默认分支。</summary>
    /// <param name="promptId">关闭的窗口对应的任务身份。</param>
    /// <returns>关闭首次被接受时为true；旧窗口不能关闭新任务。</returns>
    public bool TryDismiss(Guid promptId)
    {
        bool accepted;
        lock (_sync)
            accepted = !_disposed && _current?.Prompt.Id == promptId && !_current.Token.IsCancellationRequested
                && _current.Completion.TrySetException(new WorkflowOperatorPromptDismissedException());
        if (accepted) PublishChanged();
        return accepted;
    }

    private bool CanRespond(Guid id, WorkflowOperatorPromptKind kind) => !_disposed
        && _current?.Prompt.Id == id && _current.Prompt.Kind == kind && !_current.Token.IsCancellationRequested;

    private async Task<string?> RequestAsync(WorkflowOperatorPrompt prompt, CancellationToken cancellationToken)
    {
        CancellationTokenSource linked;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        }
        using (linked)
        {
            await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
            PendingPrompt? pending = null;
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                pending = new PendingPrompt(prompt, linked.Token);
                lock (_sync)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    _current = pending;
                }
                using var registration = linked.Token.Register(() =>
                {
                    pending.Completion.TrySetCanceled(linked.Token);
                    PublishChanged();
                });
                PublishChanged();
                var result = await pending.Completion.Task.ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                return result;
            }
            finally
            {
                lock (_sync)
                    if (ReferenceEquals(_current, pending)) _current = null;
                PublishChanged();
                _gate.Release();
            }
        }
    }

    private void PublishChanged()
    {
        var observers = PromptChanged;
        if (observers is null) return;
        foreach (EventHandler observer in observers.GetInvocationList())
        {
            try { observer(this, EventArgs.Empty); }
            catch { /* 观察器失败不能选择默认答案或破坏当前任务。 */ }
        }
    }

    /// <summary>取消当前与排队任务；幂等。宿主应先停止运行，再释放交互适配器。</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _shutdown.Cancel();
        _shutdown.Dispose();
        PublishChanged();
        // 不在仍有finally释放门锁的等待者期间Dispose SemaphoreSlim；未创建其WaitHandle，无原生句柄需释放。
    }

    private sealed class PendingPrompt(WorkflowOperatorPrompt prompt, CancellationToken token)
    {
        internal WorkflowOperatorPrompt Prompt { get; } = prompt;
        internal CancellationToken Token { get; } = token;
        internal TaskCompletionSource<string?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
