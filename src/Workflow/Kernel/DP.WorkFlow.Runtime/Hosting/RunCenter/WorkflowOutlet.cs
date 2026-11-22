using System.Globalization;
using System.Text;

namespace DP.WorkFlow;

/// <summary>出口日志级别（R1-05）。</summary>
public enum WorkflowLogLevel
{
    /// <summary>节点内部步骤、数据流转等细节。</summary>
    Debug = 0,

    /// <summary>运行起止、节点开始 / 完成。</summary>
    Info = 1,

    /// <summary>不影响结果但值得注意的情况，例如故障处理进展、提示、保持数据保存失败。</summary>
    Warning = 2,

    /// <summary>故障。</summary>
    Error = 3
}

/// <summary>出口送出的一条结构化事件。</summary>
/// <param name="FlowName">所属顶层流程名；子流程、循环块每一圈的事件都归到顶层流程名下，自己的名字仍在 <see cref="WorkflowRunEvent.WorkflowName"/>。</param>
/// <param name="Event">原始运行事件。</param>
/// <param name="Level">按类别换算的级别。</param>
/// <param name="Line">拼好的一行日志文字（不含级别和时间）。</param>
public sealed record WorkflowOutletEvent(string FlowName, WorkflowRunEvent Event, WorkflowLogLevel Level, string Line);

/// <summary>出口设置。</summary>
public sealed class WorkflowOutletOptions
{
    /// <summary>低于该级别的事件不送出。默认 <see cref="WorkflowLogLevel.Info"/>。</summary>
    public WorkflowLogLevel MinimumLevel { get; set; } = WorkflowLogLevel.Info;

    /// <summary>只送出这些类别；为空时不按类别过滤。</summary>
    public ISet<WorkflowRunEventCategory>? Categories { get; set; }

    /// <summary>待送出队列上限；满了丢最老的并计入 <see cref="WorkflowOutlet.DroppedCount"/>。</summary>
    public int Capacity { get; set; } = 4_096;

    /// <summary>复制一份设置。</summary>
    /// <returns>独立副本。</returns>
    public WorkflowOutletOptions Clone() => new()
    {
        MinimumLevel = MinimumLevel,
        Categories = Categories is null ? null : new HashSet<WorkflowRunEventCategory>(Categories),
        Capacity = Capacity
    };
}

/// <summary>
/// 对外出口（R1-05）：执行线程只投递，后台按顺序逐条交给 <see cref="OnEvent"/> / <see cref="OnLog"/>。
/// </summary>
/// <remarks>
/// 尽力送达：外部回调慢时内部排队，超过上限丢最老的并计数；回调抛出的异常被吞掉并计数，不影响流程和后续事件。
/// 适合记录和上报，不适合一条也不能丢的联锁逻辑。
/// </remarks>
public sealed class WorkflowOutlet : IDisposable
{
    private readonly object _sync = new();
    private readonly Queue<WorkflowOutletEvent> _pending = new();
    private readonly WorkflowOutletOptions _options;
    private bool _draining;
    private bool _disposed;
    private long _dropped;
    private long _callbackFailures;
    private long _delivered;

    /// <summary>初始化出口。</summary>
    /// <param name="options">设置；为空时使用默认值。</param>
    public WorkflowOutlet(WorkflowOutletOptions? options = null)
    {
        _options = options?.Clone() ?? new WorkflowOutletOptions();
        if (_options.Capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "出口队列上限必须大于 0。");
    }

    /// <summary>结构化事件回调，给数据库、MES 等使用。</summary>
    public Action<WorkflowOutletEvent>? OnEvent { get; set; }

    /// <summary>一行日志回调（级别, 文字），可直接转给 NLog、log4net 等。文字形如 <c>INFO [上料流程] 节点 拍照#3(第2圈) 完成，出口=OK，耗时 12.4ms</c>。</summary>
    public Action<WorkflowLogLevel, string>? OnLog { get; set; }

    /// <summary>低于该级别的事件不送出。</summary>
    public WorkflowLogLevel MinimumLevel => _options.MinimumLevel;

    /// <summary>因队列满被丢弃的事件累计数（"记录丢失"）。</summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    /// <summary>回调抛出异常的累计次数。</summary>
    public long CallbackFailureCount => Interlocked.Read(ref _callbackFailures);

    /// <summary>已交给回调的事件累计数。</summary>
    public long DeliveredCount => Interlocked.Read(ref _delivered);

    /// <summary>该事件是否会被送出（级别和类别过滤）。</summary>
    /// <param name="category">事件类别。</param>
    /// <param name="level">事件级别。</param>
    public bool Accepts(WorkflowRunEventCategory category, WorkflowLogLevel level) =>
        level >= _options.MinimumLevel && (_options.Categories is null || _options.Categories.Contains(category));

    /// <summary>投递一条事件；不等待回调。</summary>
    /// <param name="item">要送出的事件。</param>
    /// <returns>本次投递挤掉的最老事件条数（0 或 1）；被过滤或出口已释放时为 0。</returns>
    public int Post(WorkflowOutletEvent item)
    {
        Guard.NotNull(item);
        if (!Accepts(item.Event.Category, item.Level))
            return 0;
        var dropped = 0;
        var start = false;
        lock (_sync)
        {
            if (_disposed || (OnEvent is null && OnLog is null))
                return 0;
            _pending.Enqueue(item);
            while (_pending.Count > _options.Capacity)
            {
                _pending.Dequeue();
                dropped++;
            }
            if (!_draining)
                _draining = start = true;
        }
        if (dropped > 0)
            Interlocked.Add(ref _dropped, dropped);
        if (start)
            _ = Task.Run(Drain);
        return dropped;
    }

    /// <summary>等待已投递的事件全部送完。</summary>
    /// <param name="timeout">最长等待时间。</param>
    /// <returns>在时限内送完返回 true。</returns>
    public bool WaitIdle(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        lock (_sync)
        {
            while (_draining || _pending.Count > 0)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero || !Monitor.Wait(_sync, remaining))
                    return !_draining && _pending.Count == 0;
            }
            return true;
        }
    }

    /// <summary>停止接收新事件，尽量送完已投递的事件（最多 2 秒）。</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        WaitIdle(TimeSpan.FromSeconds(2));
    }

    private void Drain()
    {
        while (true)
        {
            WorkflowOutletEvent item;
            lock (_sync)
            {
                if (_pending.Count == 0)
                {
                    _draining = false;
                    Monitor.PulseAll(_sync);
                    return;
                }
                item = _pending.Dequeue();
            }
            Deliver(item);
        }
    }

    private void Deliver(WorkflowOutletEvent item)
    {
        var onEvent = OnEvent;
        var onLog = OnLog;
        try
        {
            onEvent?.Invoke(item);
        }
        catch
        {
            Interlocked.Increment(ref _callbackFailures);
        }
        try
        {
            onLog?.Invoke(item.Level, FormatLevel(item.Level) + " " + item.Line);
        }
        catch
        {
            Interlocked.Increment(ref _callbackFailures);
        }
        Interlocked.Increment(ref _delivered);
    }

    /// <summary>级别的日志前缀。</summary>
    /// <param name="level">级别。</param>
    public static string FormatLevel(WorkflowLogLevel level) => level switch
    {
        WorkflowLogLevel.Debug => "DEBUG",
        WorkflowLogLevel.Info => "INFO",
        WorkflowLogLevel.Warning => "WARN",
        _ => "ERROR"
    };

    /// <summary>按类别换算级别：故障→Error，运行起止和节点开始 / 完成→Info，故障处理和提示→Warning，其余→Debug。</summary>
    /// <param name="runEvent">运行事件。</param>
    public static WorkflowLogLevel GetLevel(WorkflowRunEvent runEvent)
    {
        Guard.NotNull(runEvent);
        return runEvent.Category switch
        {
            WorkflowRunEventCategory.Fault => WorkflowLogLevel.Error,
            WorkflowRunEventCategory.Lifecycle when runEvent.EventType == "RunFaulted" => WorkflowLogLevel.Error,
            WorkflowRunEventCategory.Lifecycle or WorkflowRunEventCategory.NodeExecution => WorkflowLogLevel.Info,
            WorkflowRunEventCategory.Recovery => WorkflowLogLevel.Warning,
            _ when runEvent.EventType == "NodeNoticeRaised" => WorkflowLogLevel.Warning,
            _ => WorkflowLogLevel.Debug
        };
    }

    /// <summary>拼一行日志文字（不含级别）。</summary>
    /// <param name="flowName">所属顶层流程名。</param>
    /// <param name="runEvent">运行事件。</param>
    /// <param name="nodeTitle">节点显示名；为空时用节点 ID。</param>
    public static string FormatLine(string flowName, WorkflowRunEvent runEvent, string? nodeTitle = null)
    {
        Guard.NotNull(runEvent);
        var text = new StringBuilder();
        text.Append('[').Append(flowName);
        if (!string.IsNullOrEmpty(runEvent.WorkflowName) && !string.Equals(runEvent.WorkflowName, flowName, StringComparison.Ordinal))
            text.Append('/').Append(runEvent.WorkflowName);
        // 子画布（R3-a）：标出经过的复合节点和圈序号，例如 [上料流程/循环块@3]。
        if (runEvent.Parents is { Count: > 0 } parents)
            foreach (var parent in parents)
            {
                text.Append('/').Append(parent.NodeId);
                if (parent.Cycle > 0)
                    text.Append('@').Append(parent.Cycle.ToString(CultureInfo.InvariantCulture));
            }
        text.Append("] ");
        var message = ReadText(runEvent, "Message");
        if (runEvent.NodeId is { Length: > 0 } nodeId)
        {
            text.Append("节点 ").Append(string.IsNullOrWhiteSpace(nodeTitle) ? nodeId : nodeTitle);
            if (runEvent.ExecutionIdentity is { } identity)
            {
                text.Append('#').Append(identity.NodeExecutionCount.ToString(CultureInfo.InvariantCulture));
                if (identity.LoopIteration is { } loop)
                    text.Append("(第").Append(loop.ToString(CultureInfo.InvariantCulture)).Append("圈)");
            }
            switch (runEvent.EventType)
            {
                case "NodeStarted":
                    text.Append(" 开始");
                    return text.ToString();
                case "NodeCompleted":
                    text.Append(" 完成");
                    if (ReadText(runEvent, "SelectedPort") is { } port)
                        text.Append("，出口=").Append(port);
                    if (ReadDouble(runEvent, "Elapsed") is { } elapsed)
                        text.Append("，耗时 ").Append(elapsed.ToString("0.0", CultureInfo.InvariantCulture)).Append("ms");
                    return text.ToString();
                default:
                    text.Append(' ').Append(runEvent.EventType);
                    break;
            }
        }
        else
        {
            text.Append(runEvent.EventType switch
            {
                "RunStarted" => "运行开始",
                "RunCompleted" => "运行完成",
                "RunCanceled" => "运行取消",
                "RunFaulted" => "运行故障",
                "CanvasEntered" => "进入子画布",
                "CanvasExited" => "离开子画布",
                _ => runEvent.EventType
            });
        }
        if (!string.IsNullOrWhiteSpace(message))
            text.Append("：").Append(message);
        return text.ToString();
    }

    private static string? ReadText(WorkflowRunEvent runEvent, string key) =>
        runEvent.Data is not null && runEvent.Data.TryGetValue(key, out var value) && value is not null
            ? Convert.ToString(value.ToObject(), CultureInfo.InvariantCulture)
            : null;

    private static double? ReadDouble(WorkflowRunEvent runEvent, string key)
    {
        if (runEvent.Data is null || !runEvent.Data.TryGetValue(key, out var value) || value?.ToObject() is not { } raw)
            return null;
        try
        {
            return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }
}
