namespace DP.WorkFlow;

public sealed partial class WorkflowEngine
{
    /// <summary>请求人工暂停；当前节点结束后不会继续调度下一节点。</summary>
    public void Pause()
    {
        E_WorkflowExecutionState? changedState;
        WorkflowEngine[] children;
        lock (_stateSync)
        {
            _manualPauseRequested = true;
            changedState = ApplyPauseStateLocked();
            // 在状态锁内写记录：与运行开始、其他控制调用的先后顺序和状态变化一致。
            RecordControl(CaptureControlRecordLocked(changedState, null, null));
            children = _activeChildEngines.Values.ToArray();
        }
        foreach (var child in children)
            child.Pause();
        PublishStateChange(changedState);
    }

    /// <summary>清除人工暂停；若不存在外部 Hold，允许继续调度。</summary>
    public void Resume()
    {
        E_WorkflowExecutionState? changedState;
        WorkflowEngine[] children;
        lock (_stateSync)
        {
            _manualPauseRequested = false;
            changedState = ApplyPauseStateLocked();
            // 在状态锁内写记录：与运行开始、其他控制调用的先后顺序和状态变化一致。
            RecordControl(CaptureControlRecordLocked(changedState, null, null));
            children = _activeChildEngines.Values.ToArray();
        }
        foreach (var child in children)
            child.Resume();
        PublishStateChange(changedState);
    }

    /// <summary>添加外部 Hold 原因，相同原因会自动去重并传播到活动子引擎。</summary>
    /// <param name="reason">设备、恢复协调器或宿主提供的非空原因；存储前裁剪首尾空白。</param>
    /// <exception cref="ArgumentException"><paramref name="reason"/> 为空或仅包含空白。</exception>
    public void AddExternalHold(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("外部 Hold 原因不能为空。", nameof(reason));

        var normalizedReason = reason.Trim();
        E_WorkflowExecutionState? changedState;
        WorkflowEngine[] children;
        lock (_stateSync)
        {
            var added = _externalHoldReasons.Add(normalizedReason);
            changedState = ApplyPauseStateLocked();
            // 在状态锁内写记录：与运行开始、其他控制调用的先后顺序和状态变化一致。
            RecordControl(CaptureControlRecordLocked(changedState, added ? "HoldAdded" : null, normalizedReason));
            children = _activeChildEngines.Values.ToArray();
        }
        foreach (var child in children)
            child.AddExternalHold(normalizedReason);
        PublishStateChange(changedState);
    }

    /// <summary>移除外部 Hold；所有 Hold 和人工暂停均清除后才会继续运行。</summary>
    /// <param name="reason">要清除并传播到子引擎的原因；空白值被忽略。</param>
    public void RemoveExternalHold(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return;

        var normalizedReason = reason.Trim();
        E_WorkflowExecutionState? changedState;
        WorkflowEngine[] children;
        lock (_stateSync)
        {
            var removed = _externalHoldReasons.Remove(normalizedReason);
            changedState = ApplyPauseStateLocked();
            // 在状态锁内写记录：与运行开始、其他控制调用的先后顺序和状态变化一致。
            RecordControl(CaptureControlRecordLocked(changedState, removed ? "HoldRemoved" : null, normalizedReason));
            children = _activeChildEngines.Values.ToArray();
        }
        foreach (var child in children)
            child.RemoveExternalHold(normalizedReason);
        PublishStateChange(changedState);
    }

    private E_WorkflowExecutionState? ApplyPauseStateLocked()
    {
        var shouldPause = _manualPauseRequested || _externalHoldReasons.Count > 0;
        if (shouldPause)
            _resumeGate.Reset();
        else
            _resumeGate.Set();

        if (_state is not (E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused))
            return null;
        var desiredState = shouldPause ? E_WorkflowExecutionState.Paused : E_WorkflowExecutionState.Running;
        if (_state == desiredState)
            return null;
        _state = desiredState;
        return desiredState;
    }

    private void PublishStateChange(E_WorkflowExecutionState? changedState)
    {
        if (!changedState.HasValue)
            return;
        Notify(StateChanged, changedState.Value);
        PublishSnapshot();
    }

    /// <summary>
    /// 在状态锁内取下要记录的控制事件（R4-a）：Hold 原因的增减，以及由此引起的暂停 / 继续。
    /// 只在运行中（运行或暂停状态）记录；运行开始前、结束后的控制调用只改变下一次运行的初始状态。
    /// </summary>
    /// <param name="changedState">本次调用引起的状态变化；没有变化时为空。</param>
    /// <param name="holdEvent">HoldAdded / HoldRemoved；原因集合没有变化时为空。</param>
    /// <param name="holdReason">增减的原因。</param>
    private ControlRecord? CaptureControlRecordLocked(E_WorkflowExecutionState? changedState, string? holdEvent, string? holdReason)
    {
        if (_state is not (E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused))
            return null;
        if (holdEvent is null && changedState is null)
            return null;
        return new ControlRecord(holdEvent, holdReason, changedState, _manualPauseRequested,
            string.Join("; ", _externalHoldReasons.OrderBy(reason => reason, StringComparer.Ordinal)));
    }

    /// <summary>把控制事件写入运行记录：先写 Hold 增减，再写它引起的暂停 / 继续。</summary>
    private void RecordControl(ControlRecord? record)
    {
        if (record is null)
            return;
        if (record.HoldEvent is not null)
            RecordRunEvent(WorkflowRunEventDraft.Lifecycle(
                record.HoldEvent,
                record.HoldEvent == "HoldAdded" ? $"外部挂起：{record.HoldReason}" : $"解除外部挂起：{record.HoldReason}",
                new Dictionary<string, object?> { ["Reason"] = record.HoldReason }));
        if (record.ChangedState is { } state)
            RecordPauseTransition(state, record.ManualPause, record.HoldReasons);
    }

    /// <summary>记录 Paused / Resumed，附带当前的人工暂停标志和全部外部挂起原因。</summary>
    private void RecordPauseTransition(E_WorkflowExecutionState state, bool manualPause, string holdReasons)
    {
        if (state is not (E_WorkflowExecutionState.Paused or E_WorkflowExecutionState.Running))
            return;
        var paused = state == E_WorkflowExecutionState.Paused;
        RecordRunEvent(WorkflowRunEventDraft.Lifecycle(
            paused ? "Paused" : "Resumed",
            paused ? "流程暂停：当前节点结束后不再调度。" : "流程继续调度。",
            new Dictionary<string, object?>
            {
                ["ManualPause"] = manualPause,
                ["HoldReasons"] = holdReasons
            }), WorkflowEventWriteMode.FlushRequested);
    }

    private sealed record ControlRecord(
        string? HoldEvent,
        string? HoldReason,
        E_WorkflowExecutionState? ChangedState,
        bool ManualPause,
        string HoldReasons);
}
