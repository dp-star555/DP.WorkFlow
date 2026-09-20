namespace DP.WorkFlow;

/// <summary>
/// 提供标准节点模型和处理器的统一注册入口。
/// </summary>
public static class WorkflowStandardNodes
{
    private static readonly WorkflowRuntimeCapabilityRequirement ActionRegistryCapability =
        WorkflowRuntimeCapabilityRequirement.Require<IWorkflowActionRegistry>("执行宿主动作函数。");
    private static readonly WorkflowRuntimeCapabilityRequirement ConditionRegistryCapability =
        WorkflowRuntimeCapabilityRequirement.Require<IWorkflowConditionRegistry>("执行宿主条件函数。");
    private static readonly WorkflowRuntimeCapabilityRequirement SignalCapability =
        WorkflowRuntimeCapabilityRequirement.Require<IWorkflowSignalService>("读写和等待布尔流程信号。");
    private static readonly WorkflowRuntimeCapabilityRequirement ValueSignalCapability =
        WorkflowRuntimeCapabilityRequirement.Require<IWorkflowValueSignalService>("读写和等待强类型流程信号。");
    private static readonly WorkflowRuntimeCapabilityRequirement QueueCapability =
        WorkflowRuntimeCapabilityRequirement.Require<IWorkflowQueueService>("访问流程队列。");
    /// <summary>向节点目录注册 Phase 1 已实现的标准节点模型。</summary>
    public static WorkflowNodeCatalog RegisterStandardNodes(this WorkflowNodeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog
            .Register(WorkflowNodeDescriptor.Create<StartNodeModel>(
                ports: new[] { WorkflowPortDescriptor.Output() }))
            .Register(WorkflowNodeDescriptor.Create<ActionNodeModel>(
                ports: new[] { WorkflowPortDescriptor.Input(maxConnections: int.MaxValue), WorkflowPortDescriptor.Output() }))
            .Register(WorkflowNodeDescriptor.Create<CSharpScriptNodeModel, CSharpScriptNodeResult>(
                ports: new[]
                {
                    WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
                    WorkflowPortDescriptor.Output(),
                    WorkflowPortDescriptor.Output("Error")
                }))
            .Register(WorkflowNodeDescriptor.Create<DecisionNodeModel, DecisionNodeResult>(
                ports: new[]
                {
                    WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
                    WorkflowPortDescriptor.Output(WorkflowPorts.True),
                    WorkflowPortDescriptor.Output(WorkflowPorts.False)
                }))
            .Register(WorkflowNodeDescriptor.Create<ValueCompareNodeModel, CompareNodeResult>(
                ports: new[]
                {
                    WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
                    WorkflowPortDescriptor.Output(WorkflowPorts.True),
                    WorkflowPortDescriptor.Output(WorkflowPorts.False)
                }))
            .Register(WorkflowNodeDescriptor.Create<StringCompareNodeModel, CompareNodeResult>(
                ports: new[]
                {
                    WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
                    WorkflowPortDescriptor.Output(WorkflowPorts.True),
                    WorkflowPortDescriptor.Output(WorkflowPorts.False)
                }))
            .Register(WorkflowNodeDescriptor.Create<WaitFunctionNodeModel, WaitFunctionNodeResult>(
                ports: new[]
                {
                    WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
                    WorkflowPortDescriptor.Output(WorkflowPorts.Success),
                    WorkflowPortDescriptor.Output(WorkflowPorts.Timeout)
                }))
            .Register(WorkflowNodeDescriptor.Create<DelayNodeModel, DelayNodeResult>(
                ports: new[] { WorkflowPortDescriptor.Input(maxConnections: int.MaxValue), WorkflowPortDescriptor.Output() }))
            .Register(WorkflowNodeDescriptor.Create<LoopNodeModel, LoopNodeResult>(
                ports: new[]
                {
                    WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
                    WorkflowPortDescriptor.Output(WorkflowPorts.Loop),
                    WorkflowPortDescriptor.Output(WorkflowPorts.Completed)
                }))
            .Register(WorkflowNodeDescriptor.Create<JumpNodeModel>(
                ports: new[] { WorkflowPortDescriptor.Input(maxConnections: int.MaxValue), WorkflowPortDescriptor.Output() }))
            .Register(WorkflowNodeDescriptor.Create<ParallelAllNodeModel>(
                ports: new[]
                {
                    WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
                    WorkflowPortDescriptor.Output(WorkflowPorts.Branch, int.MaxValue)
                }))
            .Register(WorkflowNodeDescriptor.Create<WaitAllInputsCompletedNodeModel>(
                ports: new[]
                {
                    WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
                    WorkflowPortDescriptor.Output()
                }))
            .Register(WorkflowNodeDescriptor.Create<SignalSetNodeModel, SignalSetNodeResult>(
                ports: new[]
                {
                    WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
                    WorkflowPortDescriptor.Output()
                }))
            .Register(WorkflowNodeDescriptor.Create<SignalWaitNodeModel, SignalWaitNodeResult>(
                ports: new[]
                {
                    WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
                    WorkflowPortDescriptor.Output(WorkflowPorts.Success),
                    WorkflowPortDescriptor.Output(WorkflowPorts.Timeout)
                }))
            .Register(WorkflowNodeDescriptor.Create<SignalValueSetNodeModel, SignalValueNodeResult>(ports: StandardPorts()))
            .Register(WorkflowNodeDescriptor.Create<SignalValueWaitNodeModel, SignalValueNodeResult>(ports: WaitPorts()))
            .Register(WorkflowNodeDescriptor.Create<WaitSignalsNodeModel, bool>(ports: WaitPorts()))
            .Register(WorkflowNodeDescriptor.Create<SignalStateBatchInitializeNodeModel, SignalStateBatchInitializeResult>(ports: StandardPorts()))
            .Register(WorkflowNodeDescriptor.Create<SignalValueBatchInitializeNodeModel, SignalValueBatchInitializeResult>(ports: StandardPorts()))
            .Register(WorkflowNodeDescriptor.Create<ConvertValueNodeModel, ConvertValueNodeResult>(ports: StandardPorts()))
            .Register(WorkflowNodeDescriptor.Create<QueueInitializeNodeModel, QueueInitializeResult>(ports: StandardPorts()))
            .Register(WorkflowNodeDescriptor.Create<QueueAddNodeModel, WorkflowQueueNodeResult>(ports: StandardPorts()))
            .Register(WorkflowNodeDescriptor.Create<QueueRemoveNodeModel, WorkflowQueueNodeResult>(ports: StandardPorts()))
            .Register(WorkflowNodeDescriptor.Create<QueueReadNodeModel, WorkflowQueueNodeResult>(ports: StandardPorts()))
            .Register(WorkflowNodeDescriptor.Create<QueueWaitNodeModel, QueueWaitNodeResult>(
                ports: new[]
                {
                    WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
                    WorkflowPortDescriptor.Output(WorkflowPorts.Success),
                    WorkflowPortDescriptor.Output(WorkflowPorts.Timeout)
                }))
            .Register(WorkflowNodeDescriptor.Create<EndNodeModel>(
                ports: new[] { WorkflowPortDescriptor.Input(maxConnections: int.MaxValue) }));
    }

    /// <summary>向处理器目录注册全部标准节点处理器。</summary>
    /// <param name="handlers">目标处理器目录。</param>
    /// <returns>目标目录，便于链式组合。</returns>
    public static WorkflowNodeHandlerCatalog RegisterStandardNodeHandlers(
        this WorkflowNodeHandlerCatalog handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        return handlers
            .Register(new StartNodeHandler())
            .Register(new ActionNodeHandler(), ActionRegistryCapability)
            .Register(new CSharpScriptNodeHandler())
            .Register(new DecisionNodeHandler(), node =>
                ((DecisionNodeModel)node).ConditionSource == E_DecisionConditionSource.Function
                    ? new[] { ConditionRegistryCapability }
                    : Array.Empty<WorkflowRuntimeCapabilityRequirement>())
            .Register(new ValueCompareNodeHandler())
            .Register(new StringCompareNodeHandler())
            .Register(new WaitFunctionNodeHandler(), ConditionRegistryCapability)
            .Register(new DelayNodeHandler())
            .Register(new LoopNodeHandler())
            .Register(new JumpNodeHandler())
            .Register(new ParallelAllNodeHandler())
            .Register(new WaitAllInputsCompletedNodeHandler())
            .Register(new SignalSetNodeHandler(), SignalCapability)
            .Register(new SignalWaitNodeHandler(), SignalCapability)
            .Register(new SignalValueSetNodeHandler(), ValueSignalCapability)
            .Register(new SignalValueWaitNodeHandler(), ValueSignalCapability)
            .Register(new WaitSignalsNodeHandler(), SignalCapability)
            .Register(new SignalStateBatchInitializeNodeHandler(), SignalCapability)
            .Register(new SignalValueBatchInitializeNodeHandler(), ValueSignalCapability)
            .Register(new ConvertValueNodeHandler())
            .Register(new QueueInitializeNodeHandler(), QueueCapability)
            .Register(new QueueAddNodeHandler(), QueueCapability)
            .Register(new QueueRemoveNodeHandler(), QueueCapability)
            .Register(new QueueReadNodeHandler(), QueueCapability)
            .Register(new QueueWaitNodeHandler(), QueueCapability)
            .Register(new EndNodeHandler());
    }

    private static WorkflowPortDescriptor[] StandardPorts() =>
        new[]
        {
            WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
            WorkflowPortDescriptor.Output()
        };

    private static WorkflowPortDescriptor[] WaitPorts() =>
        new[]
        {
            WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
            WorkflowPortDescriptor.Output(WorkflowPorts.Success),
            WorkflowPortDescriptor.Output(WorkflowPorts.Timeout)
        };
}
