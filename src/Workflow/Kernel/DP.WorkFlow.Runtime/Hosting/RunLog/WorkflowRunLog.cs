using System.Globalization;

namespace DP.WorkFlow;

/// <summary>
/// 一次运行的日志树：按运行事件（<see cref="WorkflowRunEvent"/>）逐条建树。
/// 子流程按复合节点分层，循环块每圈一层，并行每个分支一层；故障、提示挂在出事的节点下，并沿途标到每一层。
/// </summary>
/// <remarks>
/// 只记流程控制本身：节点开始 / 完成 / 失败 / 取消、进出子画布、并行派发与汇聚、故障与恢复、提示、节点自己写的进度。
/// 输入解析、输出提交这类数据流事件不进树。非线程安全：由 <see cref="WorkflowRunLogBook"/> 在锁内调用。
/// </remarks>
public sealed class WorkflowRunLog
{
    private readonly Func<string, string, string?> _titleOf;
    private readonly int _maxRows;
    private readonly Dictionary<string, Canvas> _canvases = new(StringComparer.Ordinal);
    private WorkflowRunLogRow? _lastFailed;
    private long _nextId;

    /// <summary>创建一次运行的日志。</summary>
    /// <param name="flowName">流程名。</param>
    /// <param name="started">开始时间。</param>
    /// <param name="titleOf">按（画布路径，节点 ID）取节点标题；取不到返回空，用节点 ID。</param>
    /// <param name="maxRows">最多记多少行；超过后只更新已有行的结果，不再加新行。</param>
    public WorkflowRunLog(string flowName, DateTimeOffset started, Func<string, string, string?>? titleOf = null, int maxRows = 100_000)
    {
        Guard.NotNullOrWhiteSpace(flowName);
        FlowName = flowName;
        _titleOf = titleOf ?? ((_, _) => null);
        _maxRows = Math.Max(16, maxRows);
        Root = new WorkflowRunLogRow(NextId(), WorkflowRunLogRowKind.Run, null, flowName, started) { State = WorkflowRunLogState.Running };
        _canvases[string.Empty] = new Canvas(Root, "$");
        RowCount = 1;
    }

    /// <summary>流程名。</summary>
    public string FlowName { get; }

    /// <summary>运行 ID（第一条带运行 ID 的事件给出）。</summary>
    public Guid RunId { get; private set; }

    /// <summary>树根。</summary>
    public WorkflowRunLogRow Root { get; }

    /// <summary>收到的事件条数（含不进树的）。</summary>
    public long EventCount { get; private set; }

    /// <summary>节点执行次数。</summary>
    public long StepCount { get; private set; }

    /// <summary>行数。</summary>
    public int RowCount { get; private set; }

    /// <summary>是否因行数上限丢了后面的行。</summary>
    public bool Truncated { get; private set; }

    /// <summary>故障会话次数。</summary>
    public int FaultSessions { get; private set; }

    /// <summary>每收到一条事件加一；界面据此判断要不要重建。</summary>
    public long Version { get; private set; }

    /// <summary>是否已结束。</summary>
    public bool IsFinished => Root.State != WorkflowRunLogState.Running;

    /// <summary>加一条事件。</summary>
    /// <param name="runEvent">运行事件。</param>
    public void Add(WorkflowRunEvent runEvent)
    {
        Guard.NotNull(runEvent);
        EventCount++;
        Version++;
        if (RunId == Guid.Empty && runEvent.RunId != Guid.Empty && runEvent.ParentRunId is null)
            RunId = runEvent.RunId;
        if (runEvent.Category == WorkflowRunEventCategory.DataFlow)
            return;
        if (runEvent.Category == WorkflowRunEventCategory.Recovery)
        {
            AddRecovery(runEvent);
            return;
        }
        if (string.IsNullOrEmpty(runEvent.NodeId))
        {
            AddLifecycle(runEvent);
            return;
        }
        var canvas = Resolve(runEvent);
        var identity = runEvent.ExecutionIdentity;
        var token = identity?.TokenId ?? 0;
        var nodeId = runEvent.NodeId!;
        switch (runEvent.EventType)
        {
            case "NodeStarted":
            {
                StepCount++;
                var container = ContainerOf(canvas, identity, nodeId);
                var row = NewRow(container, WorkflowRunLogRowKind.Node, Title(canvas, nodeId), runEvent.Timestamp);
                if (row is null) return;
                Fill(row, runEvent, canvas);
                row.State = WorkflowRunLogState.Running;
                canvas.Nodes[Key(nodeId, token, identity?.NodeExecutionCount ?? 0)] = row;
                canvas.Latest[nodeId] = row;
                return;
            }
            case "NodeCompleted":
                Finish(Find(canvas, runEvent), runEvent, WorkflowRunLogState.Succeeded);
                return;
            case "NodeFailed":
                Finish(Find(canvas, runEvent), runEvent, WorkflowRunLogState.Failed);
                return;
            case "NodeInterrupted":
                Finish(Find(canvas, runEvent), runEvent, WorkflowRunLogState.Interrupted);
                return;
            case "NodeCanceled":
                Finish(Find(canvas, runEvent), runEvent, WorkflowRunLogState.Canceled);
                return;
            case "NodeFaultRouted":
            {
                var row = Find(canvas, runEvent);
                if (row is null) return;
                row.State = WorkflowRunLogState.FaultRouted;
                row.Detail = Message(runEvent) ?? row.Detail;
                return;
            }
            case "NodeUndoStarted":
            case "NodeUndone":
            {
                var owner = Find(canvas, runEvent) ?? canvas.Container;
                var row = NewRow(owner, WorkflowRunLogRowKind.Recovery, runEvent.EventType == "NodeUndone" ? "撤回完成" : "撤回", runEvent.Timestamp);
                if (row is null) return;
                Fill(row, runEvent, canvas);
                row.Detail = Message(runEvent);
                if (runEvent.EventType == "NodeUndone")
                    owner.State = WorkflowRunLogState.Undone;
                return;
            }
            case "ParallelStarted":
            {
                var row = LatestOf(canvas, nodeId, token);
                if (row is null)
                {
                    row = NewRow(ContainerOf(canvas, identity, nodeId), WorkflowRunLogRowKind.Parallel, Title(canvas, nodeId), runEvent.Timestamp);
                    if (row is null) return;
                    Fill(row, runEvent, canvas);
                    row.State = WorkflowRunLogState.Running;
                }
                row.Kind = WorkflowRunLogRowKind.Parallel;
                row.Detail = Message(runEvent);
                canvas.Parallels[token] = new ParallelState(row, ReadText(runEvent, "Branches")?.Split(',') ?? Array.Empty<string>());
                return;
            }
            case "ParallelMerged":
            {
                if (!canvas.Parallels.TryGetValue(token, out var parallel)) return;
                var row = parallel.Row;
                parallel.Merged = true;
                row.Detail = $"{parallel.Branches} 个分支 · 已汇聚";
                if (row.State == WorkflowRunLogState.Running && row.Kind == WorkflowRunLogRowKind.Parallel && row.NodeType is null)
                {
                    row.State = WorkflowRunLogState.Succeeded;
                    row.Ended = runEvent.Timestamp;
                }
                foreach (var branch in parallel.Rows.Values)
                    if (branch.State == WorkflowRunLogState.Running)
                    {
                        branch.State = branch.HasFailure ? WorkflowRunLogState.Failed : WorkflowRunLogState.Succeeded;
                        branch.Ended = runEvent.Timestamp;
                    }
                return;
            }
            case "NodeNoticeRaised":
            {
                var owner = Find(canvas, runEvent) ?? LatestOf(canvas, nodeId, token) ?? ContainerOf(canvas, identity, nodeId);
                var row = NewRow(owner, WorkflowRunLogRowKind.Notice, "提示", runEvent.Timestamp);
                if (row is null) return;
                Fill(row, runEvent, canvas);
                row.Detail = Message(runEvent);
                row.MarkNotice();
                return;
            }
            default:
            {
                // 节点自己写的进度、说明和其他故障类事件：挂在这次执行下面。
                var owner = Find(canvas, runEvent) ?? LatestOf(canvas, nodeId, token) ?? ContainerOf(canvas, identity, nodeId);
                var fault = runEvent.Category == WorkflowRunEventCategory.Fault;
                var row = NewRow(owner, fault ? WorkflowRunLogRowKind.Fault : WorkflowRunLogRowKind.Message, runEvent.EventType, runEvent.Timestamp);
                if (row is null) return;
                Fill(row, runEvent, canvas);
                row.Detail = Message(runEvent);
                if (fault)
                {
                    row.Title = "故障";
                    row.MarkFailure();
                    _lastFailed = row;
                }
                return;
            }
        }
    }

    private void AddLifecycle(WorkflowRunEvent runEvent)
    {
        switch (runEvent.EventType)
        {
            case "RunStarted" when runEvent.Parents is not { Count: > 0 }:
                Root.Started = runEvent.Timestamp;
                return;
            case "RunCompleted" when runEvent.Parents is not { Count: > 0 }:
                EndRun(runEvent, WorkflowRunLogState.Succeeded);
                return;
            case "RunCanceled" when runEvent.Parents is not { Count: > 0 }:
                EndRun(runEvent, WorkflowRunLogState.Canceled);
                return;
            case "RunFaulted" when runEvent.Parents is not { Count: > 0 }:
                EndRun(runEvent, WorkflowRunLogState.Failed);
                if (!Root.HasFailure) Root.MarkFailure();
                return;
            case "CanvasEntered":
            {
                var canvas = Resolve(runEvent);
                if (canvas.Container.Kind == WorkflowRunLogRowKind.Cycle)
                    canvas.Container.Started = runEvent.Timestamp;
                return;
            }
            case "CanvasExited":
            {
                var canvas = Resolve(runEvent);
                var row = canvas.Container;
                if (row.Kind != WorkflowRunLogRowKind.Cycle || row.State != WorkflowRunLogState.Running) return;
                row.Ended = runEvent.Timestamp;
                row.State = ReadText(runEvent, "State") switch
                {
                    nameof(E_WorkflowExecutionState.Canceled) => WorkflowRunLogState.Canceled,
                    nameof(E_WorkflowExecutionState.Faulted) => WorkflowRunLogState.Failed,
                    _ => WorkflowRunLogState.Succeeded
                };
                return;
            }
            case "RunStarted":
            case "RunCompleted":
            case "RunCanceled":
            case "RunFaulted":
                return;
            default:
            {
                var canvas = Resolve(runEvent);
                var row = NewRow(canvas.Container, WorkflowRunLogRowKind.Lifecycle, LifecycleTitle(runEvent.EventType), runEvent.Timestamp);
                if (row is null) return;
                row.Detail = Message(runEvent);
                row.Data = runEvent.Data;
                row.CanvasPath = runEvent.CanvasPath;
                return;
            }
        }
    }

    private void AddRecovery(WorkflowRunEvent runEvent)
    {
        // 故障处理事件来自流程管理，没有节点和画布：挂在最近出故障的那一层，没有时挂在树根。
        var container = _lastFailed?.Parent ?? Root;
        if (runEvent.EventType == "FaultSessionStarted")
            FaultSessions++;
        var row = NewRow(container, WorkflowRunLogRowKind.Recovery, runEvent.EventType switch
        {
            "FaultRaised" => "故障上报",
            "FaultSessionStarted" => "故障处理开始",
            "FaultSessionEnded" => "故障处理结束",
            "FaultHandlingFailed" => "故障处理失败",
            "StopPointReached" => "停下等处理",
            "FlowParked" => "流程停住",
            "ResumeApplied" => "处理去向",
            "ResumeRejected" => "去向被拒",
            "Rejected" => "被拒绝",
            "Stopped" => "已停止",
            "PublicDataReset" => "公共数据复位",
            _ => runEvent.EventType
        }, runEvent.Timestamp);
        if (row is null) return;
        row.Detail = Message(runEvent);
        row.Data = runEvent.Data;
        row.RecoveryCaseId = runEvent.RecoveryCaseId;
        if (runEvent.EventType == "FaultHandlingFailed")
            row.MarkFailure();
    }

    private void EndRun(WorkflowRunEvent runEvent, WorkflowRunLogState state)
    {
        Root.State = state;
        Root.Ended = runEvent.Timestamp;
        Root.Detail = Message(runEvent) ?? Root.Detail;
        // 运行结束时还标着“进行中”的层（被取消、没收到结束事件）跟着结束。
        Close(Root, runEvent.Timestamp, state == WorkflowRunLogState.Succeeded ? WorkflowRunLogState.Succeeded : WorkflowRunLogState.Canceled);
    }

    private static void Close(WorkflowRunLogRow row, DateTimeOffset at, WorkflowRunLogState state)
    {
        foreach (var child in row.Children)
        {
            if (child.State == WorkflowRunLogState.Running)
            {
                child.State = child.HasFailure ? WorkflowRunLogState.Failed : state;
                child.Ended = at;
            }
            if (child.Children.Count > 0)
                Close(child, at, state);
        }
    }

    private void Finish(WorkflowRunLogRow? row, WorkflowRunEvent runEvent, WorkflowRunLogState state)
    {
        if (row is null) return;
        row.Ended = runEvent.Timestamp;
        row.Data = runEvent.Data ?? row.Data;
        if (ReadDouble(runEvent, "Elapsed") is { } elapsed)
            row.Elapsed = TimeSpan.FromMilliseconds(elapsed);
        var message = Message(runEvent);
        if (message is "节点执行完成。") message = null;
        if (state == WorkflowRunLogState.Succeeded)
        {
            // 走了故障出口的节点保持“故障出口”，不改成成功。
            if (row.State != WorkflowRunLogState.FaultRouted)
                row.State = WorkflowRunLogState.Succeeded;
            if (ReadText(runEvent, "SelectedPort") is { Length: > 0 } port && !string.Equals(port, WorkflowPorts.Success, StringComparison.OrdinalIgnoreCase))
                row.Detail = string.IsNullOrWhiteSpace(message) ? "出口 " + port : message + " · 出口 " + port;
            else if (!string.IsNullOrWhiteSpace(message))
                row.Detail = message;
            return;
        }
        row.State = state;
        row.Detail = message ?? row.Detail;
        if (state is WorkflowRunLogState.Failed or WorkflowRunLogState.Interrupted)
        {
            row.Kind = row.IsContainer ? row.Kind : WorkflowRunLogRowKind.Fault;
            row.MarkFailure();
            _lastFailed = row;
        }
    }

    private Canvas Resolve(WorkflowRunEvent runEvent)
    {
        var canvas = _canvases[string.Empty];
        if (runEvent.Parents is not { Count: > 0 } parents)
            return canvas;
        foreach (var parent in parents)
        {
            if (!canvas.Latest.TryGetValue(parent.NodeId, out var owner))
            {
                // 没收到复合节点自己的开始事件（例如记录丢了）：补一行占位。
                owner = NewRow(canvas.Container, WorkflowRunLogRowKind.Node, Title(canvas, parent.NodeId), runEvent.Timestamp) ?? canvas.Container;
                if (!ReferenceEquals(owner, canvas.Container))
                {
                    owner.NodeId = parent.NodeId;
                    owner.CanvasPath = canvas.Path;
                    owner.State = WorkflowRunLogState.Running;
                    canvas.Latest[parent.NodeId] = owner;
                }
            }
            if (owner.Kind is WorkflowRunLogRowKind.Node or WorkflowRunLogRowKind.Fault)
                owner.Kind = parent.Cycle > 0 ? WorkflowRunLogRowKind.LoopBlock : WorkflowRunLogRowKind.SubFlow;
            var path = canvas.Path + "/" + parent.NodeId;
            var key = canvas.Key + "/" + owner.Id.ToString(CultureInfo.InvariantCulture) + "@" + parent.Cycle.ToString(CultureInfo.InvariantCulture);
            if (!_canvases.TryGetValue(key, out var child))
            {
                var container = owner;
                if (parent.Cycle > 0)
                {
                    container = NewRow(owner, WorkflowRunLogRowKind.Cycle, $"第 {parent.Cycle.ToString(CultureInfo.InvariantCulture)} 圈", runEvent.Timestamp) ?? owner;
                    container.Cycle = parent.Cycle;
                    container.CanvasPath = path;
                    container.State = WorkflowRunLogState.Running;
                    // 上一圈还没收到结束事件时，新的一圈开始就算它结束了。
                    foreach (var previous in owner.Children)
                        if (!ReferenceEquals(previous, container) && previous.Kind == WorkflowRunLogRowKind.Cycle && previous.State == WorkflowRunLogState.Running)
                        {
                            previous.State = previous.HasFailure ? WorkflowRunLogState.Failed : WorkflowRunLogState.Succeeded;
                            previous.Ended ??= runEvent.Timestamp;
                        }
                }
                child = new Canvas(container, path) { Key = key };
                _canvases[key] = child;
            }
            canvas = child;
        }
        return canvas;
    }

    private WorkflowRunLogRow ContainerOf(Canvas canvas, WorkflowExecutionIdentity? identity, string nodeId)
    {
        if (identity is null || identity.TokenId == 0)
            return canvas.Container;
        if (canvas.Tokens.TryGetValue(identity.TokenId, out var known))
            return known;
        var container = canvas.Container;
        if (identity.AncestorTokenIds is { Count: > 0 } ancestors)
        {
            var parentToken = ancestors[0];
            if (canvas.Parallels.TryGetValue(parentToken, out var parallel) && !parallel.Merged)
            {
                var index = parallel.Rows.Count + 1;
                var position = Array.IndexOf(parallel.Entries, nodeId);
                if (position >= 0) index = position + 1;
                var title = $"分支 {index.ToString(CultureInfo.InvariantCulture)} {Title(canvas, nodeId)}";
                var branch = NewRow(parallel.Row, WorkflowRunLogRowKind.Branch, title, DateTimeOffset.MinValue);
                if (branch is not null)
                {
                    branch.TokenId = identity.TokenId;
                    branch.ParentTokenId = parentToken;
                    branch.CanvasPath = canvas.Path;
                    branch.State = WorkflowRunLogState.Running;
                    parallel.Rows[identity.TokenId] = branch;
                    container = branch;
                }
            }
            else if (canvas.Tokens.TryGetValue(parentToken, out var parentContainer))
                container = parentContainer;
        }
        canvas.Tokens[identity.TokenId] = container;
        return container;
    }

    private WorkflowRunLogRow? NewRow(WorkflowRunLogRow parent, WorkflowRunLogRowKind kind, string title, DateTimeOffset started)
    {
        if (RowCount >= _maxRows)
        {
            Truncated = true;
            return null;
        }
        var row = new WorkflowRunLogRow(NextId(), kind, parent, title, started == DateTimeOffset.MinValue ? parent.Started : started);
        parent.Add(row);
        RowCount++;
        // 分支行的开始时间取第一个节点的开始时间。
        if (parent.Kind == WorkflowRunLogRowKind.Branch && parent.Children.Count == 1 && started != DateTimeOffset.MinValue)
            parent.Started = started;
        return row;
    }

    private void Fill(WorkflowRunLogRow row, WorkflowRunEvent runEvent, Canvas canvas)
    {
        row.NodeId = runEvent.NodeId;
        row.NodeType = runEvent.NodeType;
        row.CanvasPath = canvas.Path;
        row.Data = runEvent.Data;
        row.RecoveryCaseId = runEvent.RecoveryCaseId;
        if (runEvent.ExecutionIdentity is { } identity)
        {
            row.TokenId = identity.TokenId;
            row.ParentTokenId = identity.AncestorTokenIds is { Count: > 0 } ancestors ? ancestors[0] : 0;
        }
        if (runEvent.Parents is { Count: > 0 } parents)
            row.Cycle = parents[parents.Count - 1].Cycle;
    }

    private static WorkflowRunLogRow? Find(Canvas canvas, WorkflowRunEvent runEvent)
    {
        var identity = runEvent.ExecutionIdentity;
        if (identity is not null && canvas.Nodes.TryGetValue(Key(runEvent.NodeId!, identity.TokenId, identity.NodeExecutionCount), out var row))
            return row;
        return canvas.Latest.TryGetValue(runEvent.NodeId!, out var latest) && (identity is null || latest.TokenId == identity.TokenId) ? latest : null;
    }

    private static WorkflowRunLogRow? LatestOf(Canvas canvas, string nodeId, long token) =>
        canvas.Latest.TryGetValue(nodeId, out var row) && (token == 0 || row.TokenId == token) ? row : null;

    private string Title(Canvas canvas, string nodeId) => _titleOf(canvas.Path, nodeId) is { Length: > 0 } title ? title : nodeId;

    private long NextId() => ++_nextId;

    private static string Key(string nodeId, long token, int execution) =>
        nodeId + "#" + token.ToString(CultureInfo.InvariantCulture) + "#" + execution.ToString(CultureInfo.InvariantCulture);

    private static string LifecycleTitle(string eventType) => eventType switch
    {
        "StopRequested" => "要求停下",
        "InterruptRequested" => "要求中断",
        "RunContinued" => "继续运行",
        "OutputsInvalidated" => "输出作废",
        "HoldAdded" => "挂起",
        "HoldRemoved" => "解除挂起",
        "Paused" => "暂停",
        "Resumed" => "继续",
        _ => eventType
    };

    internal static string? Message(WorkflowRunEvent runEvent) => ReadText(runEvent, "Message");

    internal static string? ReadText(WorkflowRunEvent runEvent, string key) =>
        runEvent.Data is not null && runEvent.Data.TryGetValue(key, out var value) && value is not null
            ? Convert.ToString(value.ToObject(), CultureInfo.InvariantCulture)
            : null;

    private static double? ReadDouble(WorkflowRunEvent runEvent, string key)
    {
        if (runEvent.Data is null || !runEvent.Data.TryGetValue(key, out var value) || value is null) return null;
        return value.ToObject() switch
        {
            double number => number,
            float number => number,
            int number => number,
            long number => number,
            decimal number => (double)number,
            TimeSpan span => span.TotalMilliseconds,
            string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private sealed class Canvas(WorkflowRunLogRow container, string path)
    {
        public WorkflowRunLogRow Container { get; } = container;
        public string Path { get; } = path;
        public string Key { get; init; } = string.Empty;
        public Dictionary<string, WorkflowRunLogRow> Nodes { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, WorkflowRunLogRow> Latest { get; } = new(StringComparer.Ordinal);
        public Dictionary<long, WorkflowRunLogRow> Tokens { get; } = new();
        public Dictionary<long, ParallelState> Parallels { get; } = new();
    }

    private sealed class ParallelState(WorkflowRunLogRow row, string[] entries)
    {
        public WorkflowRunLogRow Row { get; } = row;
        public string[] Entries { get; } = entries.Select(entry => entry.Trim()).ToArray();
        public int Branches => Math.Max(Entries.Length, Rows.Count);
        public bool Merged { get; set; }
        public Dictionary<long, WorkflowRunLogRow> Rows { get; } = new();
    }
}
