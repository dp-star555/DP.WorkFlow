namespace DP.WorkFlow;

/// <summary>运行事件的粗粒度类别，用于查询、筛选和保留策略。</summary>
public enum WorkflowRunEventCategory
{
    /// <summary>Run 起止、取消、故障和中断检测。</summary>
    Lifecycle = 0,

    /// <summary>节点开始、完成和取消。</summary>
    NodeExecution = 1,

    /// <summary>输入解析、输出提交以及变量和公共数据提交。</summary>
    DataFlow = 2,

    /// <summary>节点内部步骤和引擎内部进度。</summary>
    Trace = 3,

    /// <summary>节点故障和操作清理失败。</summary>
    Fault = 4,

    /// <summary>故障处理会话的进展。</summary>
    Recovery = 5
}

/// <summary>
/// 表示运行过程中已经发生的一条不可变事实。
/// </summary>
/// <param name="RunId">产生事件的运行实例身份；子 Run 使用自己的 RunId。</param>
/// <param name="Sequence">同一 Run 内严格唯一且单调递增的事件序号。</param>
/// <param name="Timestamp">Recorder 观察到事件的 UTC 时间。</param>
/// <param name="Category">事件类别，用于查询和保留策略。</param>
/// <param name="EventType">稳定英文事件键；显示文本由 UI 本地化或存放在 Data 中。</param>
/// <param name="WriteMode">产生该事件时请求的推送优先级。</param>
/// <param name="WorkflowName">产生事件的流程名称。</param>
/// <param name="NodeId">产生事件的节点实例 ID；Run 级事件为空。</param>
/// <param name="NodeType">产生事件的稳定节点类型键；非节点事件为空。</param>
/// <param name="ExecutionIdentity">产生事件的完整执行身份，保留 Token、Scope、执行次数和循环迭代号。</param>
/// <param name="OperationId">与该事件关联的逻辑操作身份。</param>
/// <param name="RecoveryCaseId">与该事件关联的故障处理会话身份。</param>
/// <param name="SchemaVersion">事件 Payload 结构版本，不是节点版本。</param>
/// <param name="Data">经过容量、标识化和脱敏处理的 Payload；不包含任意对象图。</param>
/// <param name="ParentRunId">子 Run 的父 Run 身份；根 Run 为空。</param>
/// <param name="ParentExecution">触发子 Run 的父节点执行身份；根 Run 为空。</param>
public sealed record WorkflowRunEvent(
    Guid RunId,
    long Sequence,
    DateTimeOffset Timestamp,
    WorkflowRunEventCategory Category,
    string EventType,
    WorkflowEventWriteMode WriteMode,
    string? WorkflowName,
    string? NodeId,
    string? NodeType,
    WorkflowExecutionIdentity? ExecutionIdentity,
    Guid? OperationId,
    Guid? RecoveryCaseId,
    int SchemaVersion,
    IReadOnlyDictionary<string, WorkflowTraceValue>? Data,
    Guid? ParentRunId = null,
    WorkflowExecutionIdentity? ParentExecution = null);

/// <summary>
/// 引擎提交给 Recorder 的事件草稿；序号、时间戳和 Payload 编码由 Recorder 补全。
/// </summary>
/// <param name="Category">事件类别。</param>
/// <param name="EventType">稳定英文事件键。</param>
/// <param name="Message">面向使用者的可选说明；编码后作为 <c>Message</c> 键进入 Payload。</param>
/// <param name="NodeId">节点实例 ID；Run 级事件为空。</param>
/// <param name="NodeType">稳定节点类型键；非节点事件为空。</param>
/// <param name="ExecutionIdentity">完整执行身份。</param>
/// <param name="OperationId">关联的逻辑操作身份。</param>
/// <param name="RecoveryCaseId">关联的故障处理会话身份。</param>
/// <param name="Data">未编码的结构化数据；由 Recorder 统一编码，调用方不得在提交后修改。</param>
public sealed record WorkflowRunEventDraft(
    WorkflowRunEventCategory Category,
    string EventType,
    string? Message = null,
    string? NodeId = null,
    string? NodeType = null,
    WorkflowExecutionIdentity? ExecutionIdentity = null,
    Guid? OperationId = null,
    Guid? RecoveryCaseId = null,
    IReadOnlyDictionary<string, object?>? Data = null)
{
    /// <summary>创建 Run 生命周期事件草稿。</summary>
    /// <param name="eventType">稳定事件键，例如 RunStarted。</param>
    /// <param name="message">面向使用者的可选说明。</param>
    /// <param name="data">可选结构化数据。</param>
    /// <returns>类别为 <see cref="WorkflowRunEventCategory.Lifecycle"/> 的草稿。</returns>
    public static WorkflowRunEventDraft Lifecycle(
        string eventType,
        string? message = null,
        IReadOnlyDictionary<string, object?>? data = null) =>
        new(WorkflowRunEventCategory.Lifecycle, eventType, message, Data: data);

    /// <summary>创建节点执行事件草稿。</summary>
    /// <param name="eventType">稳定事件键，例如 NodeStarted。</param>
    /// <param name="node">产生事件的节点配置。</param>
    /// <param name="identity">该次执行的完整身份。</param>
    /// <param name="message">面向使用者的可选说明。</param>
    /// <param name="data">可选结构化数据。</param>
    /// <returns>类别为 <see cref="WorkflowRunEventCategory.NodeExecution"/> 的草稿。</returns>
    public static WorkflowRunEventDraft Node(
        string eventType,
        IWorkflowNodeModel node,
        WorkflowExecutionIdentity identity,
        string? message = null,
        IReadOnlyDictionary<string, object?>? data = null) =>
        new(WorkflowRunEventCategory.NodeExecution, eventType, message, node.Id, node.NodeType, identity, Data: data);

    /// <summary>创建节点内部 Trace 事件草稿。</summary>
    /// <param name="step">稳定步骤键。</param>
    /// <param name="node">产生步骤的节点配置。</param>
    /// <param name="identity">该次执行的完整身份。</param>
    /// <param name="message">面向使用者的可选说明。</param>
    /// <param name="data">可选结构化数据。</param>
    /// <returns>类别为 <see cref="WorkflowRunEventCategory.Trace"/> 的草稿。</returns>
    public static WorkflowRunEventDraft Trace(
        string step,
        IWorkflowNodeModel node,
        WorkflowExecutionIdentity identity,
        string? message = null,
        IReadOnlyDictionary<string, object?>? data = null) =>
        new(WorkflowRunEventCategory.Trace, step, message, node.Id, node.NodeType, identity, Data: data);

    /// <summary>创建节点故障事件草稿。</summary>
    /// <param name="eventType">稳定事件键，例如 NodeFailed。</param>
    /// <param name="node">发生故障的节点配置。</param>
    /// <param name="identity">故障发生时的执行身份。</param>
    /// <param name="message">诊断消息。</param>
    /// <param name="operationId">关联的逻辑操作身份。</param>
    /// <param name="data">可选结构化数据。</param>
    /// <returns>类别为 <see cref="WorkflowRunEventCategory.Fault"/> 的草稿。</returns>
    public static WorkflowRunEventDraft Fault(
        string eventType,
        IWorkflowNodeModel node,
        WorkflowExecutionIdentity identity,
        string? message = null,
        Guid? operationId = null,
        IReadOnlyDictionary<string, object?>? data = null) =>
        new(WorkflowRunEventCategory.Fault, eventType, message, node.Id, node.NodeType, identity, operationId, Data: data);
}

/// <summary>表示 Recorder 接受一条事件草稿后的结果。</summary>
/// <param name="Accepted">事件是否已进入有界队列或内存窗口。</param>
/// <param name="Sequence">分配给该事件的 Run 内序号；未接受时为零。</param>
/// <param name="FlushRequested">是否因 Durable 优先级请求了立即推送。</param>
/// <param name="Error">未接受时的原因；接受时为空。</param>
public sealed record WorkflowRunEventReceipt(
    bool Accepted,
    long Sequence,
    bool FlushRequested,
    string? Error = null);

/// <summary>表示按 Run 内序号排列的一段运行事件。</summary>
/// <param name="RunId">事件所属的运行实例身份。</param>
/// <param name="Events">按序号升序排列的事件。</param>
public sealed record WorkflowRunEventBatch(Guid RunId, IReadOnlyList<WorkflowRunEvent> Events)
{
    /// <summary>获取批次中最后一条事件的序号；空批次为零。</summary>
    public long LastSequence => Events.Count == 0 ? 0 : Events[^1].Sequence;
}

/// <summary>表示一次 Run 的正常收尾信息。</summary>
/// <param name="State">已经确定的 Run 终态。</param>
/// <param name="Message">终态说明。</param>
/// <param name="Elapsed">Run 总耗时。</param>
public sealed record WorkflowRunCompletion(
    E_WorkflowExecutionState State,
    string? Message,
    TimeSpan Elapsed);

/// <summary>表示顶层 Sink 对一次写入的结果。</summary>
/// <param name="Succeeded">Sink 是否确认接收。</param>
/// <param name="Error">失败原因；成功时为空。</param>
public sealed record WorkflowRunEventWriteResult(bool Succeeded, string? Error = null)
{
    /// <summary>获取表示成功的共享结果。</summary>
    public static WorkflowRunEventWriteResult Success { get; } = new(true);

    /// <summary>创建表示失败的结果。</summary>
    /// <param name="error">失败原因。</param>
    /// <returns>携带失败原因的结果。</returns>
    public static WorkflowRunEventWriteResult Failed(string error) => new(false, error);
}
