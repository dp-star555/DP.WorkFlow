namespace DP.WorkFlow;

/// <summary>提供运控与设备节点的统一注册入口。</summary>
public static class WorkflowMotionNodes
{
    private static readonly WorkflowRuntimeCapabilityRequirement IoCapability =
        WorkflowRuntimeCapabilityRequirement.Require<IWorkflowIoService>("访问宿主 IO 点位。");
    private static readonly WorkflowRuntimeCapabilityRequirement AxisCapability =
        WorkflowRuntimeCapabilityRequirement.Require<IWorkflowAxisService>("控制并查询宿主运动轴。");
    private static readonly WorkflowRuntimeCapabilityRequirement PneumaticCapability =
        WorkflowRuntimeCapabilityRequirement.Require<IWorkflowPneumaticService>("控制并查询宿主气动设备。");
    private static readonly WorkflowRuntimeCapabilityRequirement CodeReaderCapability =
        WorkflowRuntimeCapabilityRequirement.Require<IWorkflowCodeReaderService>("控制并查询宿主读码器。");
    /// <summary>注册当前已经实现的运控节点模型。</summary>
    public static WorkflowNodeCatalog RegisterMotionNodes(this WorkflowNodeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog
            .Register(WorkflowNodeDescriptor.Create<IoReadNodeModel, WorkflowIoNodeResult>(
                ports: new[]
                {
                    WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
                    WorkflowPortDescriptor.Output()
                }))
            .Register(WorkflowNodeDescriptor.Create<IoWriteNodeModel, WorkflowIoWriteResult>(
                ports: new[]
                {
                    WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
                    WorkflowPortDescriptor.Output()
                }))
            .Register(WorkflowNodeDescriptor.Create<IoWaitNodeModel, WorkflowIoNodeResult>(
                ports: new[]
                {
                    WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
                    WorkflowPortDescriptor.Output(WorkflowPorts.Success),
                    WorkflowPortDescriptor.Output(WorkflowPorts.Timeout)
                }))
            .Register(WorkflowNodeDescriptor.Create<IoMultiCheckNodeModel, IoMultiNodeResult>(
                ports: new[]
                {
                    WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
                    WorkflowPortDescriptor.Output(WorkflowPorts.True),
                    WorkflowPortDescriptor.Output(WorkflowPorts.False)
                }))
            .Register(WorkflowNodeDescriptor.Create<IoMultiWaitNodeModel, IoMultiNodeResult>(
                ports: new[]
                {
                    WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
                    WorkflowPortDescriptor.Output(WorkflowPorts.Success),
                    WorkflowPortDescriptor.Output(WorkflowPorts.Timeout)
                }))
            .Register(WorkflowNodeDescriptor.Create<AxisServoNodeModel, AxisServoNodeResult>(ports: StandardPorts()))
            .Register(WorkflowNodeDescriptor.Create<AxisStopNodeModel, AxisStopNodeResult>(ports: StandardPorts()))
            .Register(WorkflowNodeDescriptor.Create<AxisActionNodeModel, WorkflowAxisStepResult>(ports: StandardPorts()))
            .Register(WorkflowNodeDescriptor.Create<AxisWaitNodeModel, AxisWaitNodeResult>(ports: WaitPorts()))
            .Register(WorkflowNodeDescriptor.Create<CylinderControlNodeModel, PneumaticNodeResult>(ports: StandardPorts()))
            .Register(WorkflowNodeDescriptor.Create<CylinderWaitNodeModel, PneumaticNodeResult>(ports: WaitPorts()))
            .Register(WorkflowNodeDescriptor.Create<VacuumControlNodeModel, PneumaticNodeResult>(ports: StandardPorts()))
            .Register(WorkflowNodeDescriptor.Create<VacuumWaitNodeModel, PneumaticNodeResult>(ports: WaitPorts()))
            .Register(WorkflowNodeDescriptor.Create<CodeReaderOpenNodeModel, string>(ports: StandardPorts()))
            .Register(WorkflowNodeDescriptor.Create<CodeReaderCloseNodeModel, string>(ports: StandardPorts()))
            .Register(WorkflowNodeDescriptor.Create<CodeReaderTriggerNodeModel, string>(ports: StandardPorts()))
            .Register(WorkflowNodeDescriptor.Create<CodeReaderWaitScanNodeModel, CodeReaderScanResult>(ports: WaitPorts()));
    }

    /// <summary>注册当前已经实现的运控节点处理器。</summary>
    public static WorkflowNodeHandlerCatalog RegisterMotionNodeHandlers(
        this WorkflowNodeHandlerCatalog handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        return handlers
            .Register(new IoReadNodeHandler(), IoCapability)
            .Register(new IoWriteNodeHandler(), IoCapability)
            .Register(new IoWaitNodeHandler(), IoCapability)
            .Register(new IoMultiCheckNodeHandler(), IoCapability)
            .Register(new IoMultiWaitNodeHandler(), IoCapability)
            .Register(new AxisServoNodeHandler(), AxisCapability)
            .Register(new AxisStopNodeHandler(), AxisCapability)
            .Register(new AxisActionNodeHandler(), AxisCapability)
            .Register(new AxisWaitNodeHandler(), AxisCapability)
            .Register(new CylinderControlNodeHandler(), PneumaticCapability)
            .Register(new CylinderWaitNodeHandler(), PneumaticCapability)
            .Register(new VacuumControlNodeHandler(), PneumaticCapability)
            .Register(new VacuumWaitNodeHandler(), PneumaticCapability)
            .Register(new CodeReaderOpenNodeHandler(), CodeReaderCapability)
            .Register(new CodeReaderCloseNodeHandler(), CodeReaderCapability)
            .Register(new CodeReaderTriggerNodeHandler(), CodeReaderCapability)
            .Register(new CodeReaderWaitScanNodeHandler(), CodeReaderCapability);
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
