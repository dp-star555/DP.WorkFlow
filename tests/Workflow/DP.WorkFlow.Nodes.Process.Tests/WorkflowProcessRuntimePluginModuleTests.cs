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
        Assert.Equal(30, nodes.Snapshot().Count);
        Assert.Contains(handlers.ResolveWithRequirements(new RestartFromEntryNodeModel()).RequiredCapabilities,
            requirement => requirement.CapabilityType == typeof(IWorkflowRecoveryEntryGuard));
        Assert.Empty(handlers.ResolveWithRequirements(new ContinueOperationNodeModel()).RequiredCapabilities);
        Assert.All(nodes.Snapshot().Values, descriptor =>
            Assert.NotNull(handlers.Resolve(descriptor.Factory())));
        var robot = handlers.ResolveWithRequirements(new WaferRobotHomeNodeModel());
        Assert.Contains(robot.RequiredCapabilities,
            requirement => requirement.CapabilityType == typeof(IWorkflowWaferRobotService));
    }
}
