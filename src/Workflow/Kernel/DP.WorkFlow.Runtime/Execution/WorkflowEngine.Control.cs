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
            _externalHoldReasons.Add(normalizedReason);
            changedState = ApplyPauseStateLocked();
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
            _externalHoldReasons.Remove(normalizedReason);
            changedState = ApplyPauseStateLocked();
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
        SafeInvoke(StateChanged, changedState.Value);
        PublishSnapshot();
    }
}
