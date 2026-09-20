namespace DP.WorkFlow;

/// <summary>提供工艺流程、产品流和异常恢复节点的统一注册入口。</summary>
public static class WorkflowProcessNodes
{
    private static readonly WorkflowRuntimeCapabilityRequirement OperatorCapability =
        WorkflowRuntimeCapabilityRequirement.Require<IWorkflowOperatorService>("显示操作员提示并接收人工选择。");
    private static readonly WorkflowRuntimeCapabilityRequirement EntryGuardCapability =
        WorkflowRuntimeCapabilityRequirement.Require<IWorkflowRecoveryEntryGuard>("核实工位恢复入口、物料与副作用重执行条件。");
    private static readonly WorkflowRuntimeCapabilityRequirement ProcessCapability =
        WorkflowRuntimeCapabilityRequirement.Require<IWorkflowProcessService>("创建工艺产品记录。");
    private static readonly WorkflowRuntimeCapabilityRequirement ProductFlowCapability =
        WorkflowRuntimeCapabilityRequirement.Require<IWorkflowProductFlowService>("执行产品流转和工站状态操作。");
    private static readonly WorkflowRuntimeCapabilityRequirement WaferRobotCapability =
        WorkflowRuntimeCapabilityRequirement.Require<IWorkflowWaferRobotService>("控制并查询晶圆机器人。");
    /// <summary>注册当前已经实现的工艺流程节点模型。</summary>
    public static WorkflowNodeCatalog RegisterProcessNodes(this WorkflowNodeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog
            .Register(WorkflowNodeDescriptor.Create<RecoveryEntryNodeModel, string>(ports: Ports(WorkflowPorts.Success)))
            .Register(WorkflowNodeDescriptor.Create<ContinueOperationNodeModel, WarningResolution>(ports: Ports()))
            .Register(WorkflowNodeDescriptor.Create<RestartFromEntryNodeModel, WarningResolution>(ports: Ports()))
            .Register(WorkflowNodeDescriptor.Create<SafePointNodeModel, WorkflowSafePointContext>(
                ports: new[]
                {
                    WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
                    WorkflowPortDescriptor.Output()
                }))
            .Register(WorkflowNodeDescriptor.Create<StopCurrentStationNodeModel, WarningResolution>(ports: Ports()))
            .Register(WorkflowNodeDescriptor.Create<WarningPromptNodeModel, bool>(ports: Ports(WorkflowPorts.Success)))
            .Register(WorkflowNodeDescriptor.Create<OperatorChoiceNodeModel, string>(ports: new[] { WorkflowPortDescriptor.Input(maxConnections: int.MaxValue) }))
            .Register(WorkflowNodeDescriptor.Create<OperatorStepConfirmNodeModel, bool>(ports: Ports(WorkflowPorts.Success)))
            .Register(WorkflowNodeDescriptor.Create<RetryCurrentNodeModel, WarningResolution>(ports: Ports()))
            .Register(WorkflowNodeDescriptor.Create<JumpToNodeNodeModel, WarningResolution>(ports: Ports()))
            .Register(WorkflowNodeDescriptor.Create<WarningHandlerStartNodeModel, WorkflowInterruptContext>(ports: Ports(WorkflowPorts.Success)))
            .Register(WorkflowNodeDescriptor.Create<WarningHandlerBlockNodeModel>(ports: Array.Empty<WorkflowPortDescriptor>()))
            .Register(WorkflowNodeDescriptor.Create<ProductMoveStartNodeModel, ProductMoveStartNodeResult>(ports: ActionPorts()))
            .Register(WorkflowNodeDescriptor.Create<ProductMoveSuccessNodeModel, ProductMoveFinishNodeResult>(ports: ActionPorts()))
            .Register(WorkflowNodeDescriptor.Create<ProductMoveFailedNodeModel, ProductMoveFinishNodeResult>(ports: ActionPorts()))
            .Register(WorkflowNodeDescriptor.Create<ProductCreateNodeModel, ProductCreateNodeResult>(ports: ActionPorts()))
            .Register(WorkflowNodeDescriptor.Create<StationCanReceiveNodeModel, StationBooleanNodeResult>(ports: BooleanPorts()))
            .Register(WorkflowNodeDescriptor.Create<StationCanSendNodeModel, StationBooleanNodeResult>(ports: BooleanPorts()))
            .Register(WorkflowNodeDescriptor.Create<StationFinishedNodeModel, StationFinishedNodeResult>(ports: ActionPorts()))
            .Register(WorkflowNodeDescriptor.Create<StationSlotHasProductNodeModel, StationBooleanNodeResult>(ports: BooleanPorts()))
            .Register(WorkflowNodeDescriptor.Create<StationWaitCanReceiveNodeModel, StationWaitNodeResult>(ports: WaitPorts()))
            .Register(WorkflowNodeDescriptor.Create<StationWaitCanSendNodeModel, StationWaitNodeResult>(ports: WaitPorts()))
            .Register(WorkflowNodeDescriptor.Create<WaferRobotInitializeNodeModel, WaferRobotCommandNodeResult>(ports: ActionPorts()))
            .Register(WorkflowNodeDescriptor.Create<WaferRobotHomeNodeModel, WaferRobotCommandNodeResult>(ports: ActionPorts()))
            .Register(WorkflowNodeDescriptor.Create<WaferRobotReadSnapshotNodeModel, WaferRobotSnapshotResult>(ports: ActionPorts()))
            .Register(WorkflowNodeDescriptor.Create<WaferRobotStopNodeModel, WaferRobotCommandNodeResult>(ports: ActionPorts()))
            .Register(WorkflowNodeDescriptor.Create<WaferRobotWaitIdleNodeModel, WaferRobotWaitIdleNodeResult>(ports: WaitPorts()))
            .Register(WorkflowNodeDescriptor.Create<WaferRobotMoveNodeModel, WaferRobotCommandNodeResult>(ports: ActionPorts()))
            .Register(WorkflowNodeDescriptor.Create<WaferRobotPickNodeModel, WaferRobotCommandNodeResult>(ports: ActionPorts()))
            .Register(WorkflowNodeDescriptor.Create<WaferRobotPlaceNodeModel, WaferRobotCommandNodeResult>(ports: ActionPorts()));
    }

    /// <summary>注册当前已经实现的工艺流程节点处理器。</summary>
    public static WorkflowNodeHandlerCatalog RegisterProcessNodeHandlers(
        this WorkflowNodeHandlerCatalog handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        return handlers
            .Register(new RecoveryEntryNodeHandler())
            .Register(new ContinueOperationNodeHandler())
            .Register(new RestartFromEntryNodeHandler(), EntryGuardCapability)
            .Register(new SafePointNodeHandler())
            .Register(new StopCurrentStationNodeHandler())
            .Register(new WarningPromptNodeHandler(), OperatorCapability)
            .Register(new OperatorChoiceNodeHandler(), OperatorCapability)
            .Register(new OperatorStepConfirmNodeHandler(), OperatorCapability)
            .Register(new RetryCurrentNodeHandler())
            .Register(new JumpToNodeNodeHandler())
            .Register(new WarningHandlerStartNodeHandler())
            .Register(new WarningHandlerBlockNodeHandler())
            .Register(new ProductMoveStartNodeHandler(), ProductFlowCapability)
            .Register(new ProductMoveSuccessNodeHandler(), ProductFlowCapability)
            .Register(new ProductMoveFailedNodeHandler(), ProductFlowCapability)
            .Register(new ProductCreateNodeHandler(), ProcessCapability)
            .Register(new StationCanReceiveNodeHandler(), ProductFlowCapability)
            .Register(new StationCanSendNodeHandler(), ProductFlowCapability)
            .Register(new StationFinishedNodeHandler(), ProductFlowCapability)
            .Register(new StationSlotHasProductNodeHandler(), ProductFlowCapability)
            .Register(new StationWaitCanReceiveNodeHandler(), ProductFlowCapability)
            .Register(new StationWaitCanSendNodeHandler(), ProductFlowCapability)
            .Register(new WaferRobotInitializeNodeHandler(), WaferRobotCapability)
            .Register(new WaferRobotHomeNodeHandler(), WaferRobotCapability)
            .Register(new WaferRobotReadSnapshotNodeHandler(), WaferRobotCapability)
            .Register(new WaferRobotStopNodeHandler(), WaferRobotCapability)
            .Register(new WaferRobotWaitIdleNodeHandler(), WaferRobotCapability)
            .Register(new WaferRobotMoveNodeHandler(), WaferRobotCapability)
            .Register(new WaferRobotPickNodeHandler(), WaferRobotCapability)
            .Register(new WaferRobotPlaceNodeHandler(), WaferRobotCapability);
    }

    private static WorkflowPortDescriptor[] Ports(params string[] outputKeys) =>
        new[] { WorkflowPortDescriptor.Input(maxConnections: int.MaxValue) }
            .Concat(outputKeys.Select(key => WorkflowPortDescriptor.Output(key)))
            .ToArray();

    private static WorkflowPortDescriptor[] ActionPorts() => Ports(WorkflowPorts.Success, "Failed");
    private static WorkflowPortDescriptor[] BooleanPorts() => Ports(WorkflowPorts.True, WorkflowPorts.False, "Failed");
    private static WorkflowPortDescriptor[] WaitPorts() => Ports(WorkflowPorts.Success, WorkflowPorts.Timeout, "Failed");
}
