namespace DP.WorkFlow.Tests;

public sealed class WorkflowStandardRuntimePluginModuleTests
{
    [Fact]
    public void Register_ContributesACompleteFreezableRuntimeModule()
    {
        var nodes = new WorkflowNodeCatalog();
        var handlers = new WorkflowNodeHandlerCatalog();
        var composition = new WorkflowRuntimePluginCatalog(nodes, handlers)
            .Register(new WorkflowStandardRuntimePluginModule())
            .Freeze();

        Assert.True(composition.IsFrozen);
        Assert.Equal(26, nodes.Snapshot().Count);
        Assert.All(nodes.Snapshot().Values, descriptor =>
            Assert.NotNull(handlers.Resolve(descriptor.Factory())));
        var functionDecision = handlers.ResolveWithRequirements(new DecisionNodeModel());
        Assert.Contains(functionDecision.RequiredCapabilities,
            requirement => requirement.CapabilityType == typeof(IWorkflowConditionRegistry));
        var bindingDecision = handlers.ResolveWithRequirements(new DecisionNodeModel
        {
            ConditionSource = E_DecisionConditionSource.Binding
        });
        Assert.Empty(bindingDecision.RequiredCapabilities);
    }
}
