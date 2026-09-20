namespace DP.WorkFlow.Tests;

public sealed class WorkflowCompositeRuntimePluginModuleTests
{
    [Fact]
    public void Register_ContributesACompleteFreezableRuntimeModule()
    {
        var nodes = new WorkflowNodeCatalog();
        var handlers = new WorkflowNodeHandlerCatalog();
        var composition = new WorkflowRuntimePluginCatalog(nodes, handlers)
            .Register(new WorkflowCompositeRuntimePluginModule())
            .Freeze();

        Assert.True(composition.IsFrozen);
        Assert.Single(nodes.Snapshot());
        Assert.All(nodes.Snapshot().Values, descriptor =>
            Assert.NotNull(handlers.Resolve(descriptor.Factory())));
    }
}
