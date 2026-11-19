namespace DP.WorkFlow.Tests;

/// <summary>R7-a：节点和处理器自动发现，端口、输出类型、图标写在节点上，能力要求写在处理器里，启动自检。</summary>
public sealed class WorkflowNodeDiscoveryTests
{
    [Fact]
    public void FromType_ReadsPortsOutputTypeAndIconFromAttributes()
    {
        var loop = WorkflowNodeDescriptor.FromType(typeof(LoopNodeModel));
        Assert.Equal(new[] { "Input", "Loop", "Completed" }, loop.Ports.Select(port => port.Key));
        Assert.Equal(typeof(LoopNodeResult), loop.OutputType);
        Assert.Equal("loop", loop.Icon);

        var start = WorkflowNodeDescriptor.FromType(typeof(StartNodeModel));
        Assert.DoesNotContain(start.Ports, port => port.Direction == WorkflowPortDirection.Input);
        Assert.DoesNotContain(WorkflowNodeDescriptor.FromType(typeof(EndNodeModel)).Ports, port => port.Direction == WorkflowPortDirection.Output);

        var parallel = WorkflowNodeDescriptor.FromType(typeof(ParallelAllNodeModel));
        Assert.Equal(int.MaxValue, Assert.Single(parallel.Ports, port => port.Key == WorkflowPorts.Branch).MaxConnections);
    }

    [Fact]
    public void HandlerDeclaredCapabilities_FollowNodeConfiguration()
    {
        var handlers = new WorkflowNodeHandlerCatalog().RegisterStandardNodeHandlers();

        Assert.Empty(handlers.ResolveWithRequirements(new LoopNodeModel()).RequiredCapabilities);
        Assert.Contains(handlers.ResolveWithRequirements(new LoopNodeModel { ExitSignalKey = "Stop" }).RequiredCapabilities,
            requirement => requirement.CapabilityType == typeof(IWorkflowSignalService));
        Assert.Contains(handlers.ResolveWithRequirements(new QueueAddNodeModel()).RequiredCapabilities,
            requirement => requirement.CapabilityType == typeof(IWorkflowQueueService));
    }

    [Fact]
    public void Freeze_RejectsHandlerWithoutRegisteredModel()
    {
        var nodes = new WorkflowNodeCatalog().Register(WorkflowNodeDescriptor.FromType(typeof(EndNodeModel)));
        var handlers = new WorkflowNodeHandlerCatalog().Register(new EndNodeHandler()).Register(new DelayNodeHandler());
        var composition = new WorkflowRuntimePluginCatalog(nodes, handlers);

        var error = Assert.Throws<InvalidOperationException>(() => composition.Freeze());

        Assert.Contains(nameof(DelayNodeHandler), error.Message, StringComparison.Ordinal);
        Assert.False(nodes.IsFrozen);
    }
}
