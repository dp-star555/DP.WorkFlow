namespace DP.WorkFlow.Tests;

public sealed class WorkflowProcessRuntimePluginModuleTests
{
    [Fact]
    public void Register_ContributesACompleteFreezableRuntimeModule()
    {
        var nodes = new WorkflowNodeCatalog();
        var handlers = new WorkflowNodeHandlerCatalog();
        var composition = new WorkflowRuntimePluginCatalog(nodes, handlers)
            .Register(new WorkflowProcessRuntimePluginModule())
            .Freeze();

        Assert.True(composition.IsFrozen);
        Assert.Equal(29, nodes.Snapshot().Count);
        // 去向节点只在故障处理流程里使用，故障会话由统管层提供，不声明宿主能力。
        Assert.Empty(handlers.ResolveWithRequirements(new RollBackNodeModel()).RequiredCapabilities);
        Assert.Empty(handlers.ResolveWithRequirements(new RecoverInPlaceNodeModel()).RequiredCapabilities);
        Assert.All(nodes.Snapshot().Values, descriptor =>
            Assert.NotNull(handlers.Resolve(descriptor.Factory())));
        var robot = handlers.ResolveWithRequirements(new WaferRobotHomeNodeModel());
        Assert.Contains(robot.RequiredCapabilities,
            requirement => requirement.CapabilityType == typeof(IWorkflowWaferRobotService));
    }
}
