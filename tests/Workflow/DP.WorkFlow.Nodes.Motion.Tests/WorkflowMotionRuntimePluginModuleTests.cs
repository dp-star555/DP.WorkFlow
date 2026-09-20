namespace DP.WorkFlow.Tests;

public sealed class WorkflowMotionRuntimePluginModuleTests
{
    [Fact]
    public void Register_ContributesACompleteFreezableRuntimeModule()
    {
        var nodes = new WorkflowNodeCatalog();
        var handlers = new WorkflowNodeHandlerCatalog();
        var composition = new WorkflowRuntimePluginCatalog(nodes, handlers)
            .Register(new WorkflowMotionRuntimePluginModule())
            .Freeze();

        Assert.True(composition.IsFrozen);
        Assert.Equal(17, nodes.Snapshot().Count);
        Assert.All(nodes.Snapshot().Values, descriptor =>
            Assert.NotNull(handlers.Resolve(descriptor.Factory())));
        var axis = handlers.ResolveWithRequirements(new AxisActionNodeModel());
        Assert.Contains(axis.RequiredCapabilities,
            requirement => requirement.CapabilityType == typeof(IWorkflowAxisService));
    }
}
