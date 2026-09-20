namespace DP.WorkFlow.Tests;

public sealed class WorkflowRuntimeCapabilityTests
{
    [Fact]
    public async Task RunAsync_RejectsMissingCapabilityBeforeHandlerExecution()
    {
        var handler = new CapabilityNodeHandler();
        var nodes = new WorkflowNodeCatalog()
            .Register(WorkflowNodeDescriptor.Create<CapabilityNode>());
        var handlers = new WorkflowNodeHandlerCatalog()
            .Register(
                handler,
                WorkflowRuntimeCapabilityRequirement.Require<ITestCapability>("测试运行能力。"));
        new WorkflowRuntimePluginCatalog(nodes, handlers).Freeze();
        var document = CreateDocument(new CapabilityNode { Id = "Capability", Title = "Capability" });
        using var host = new WorkflowRuntimeHost(nodes, handlers);
        host.Configure(document, new WorkflowContext(new WorkflowServiceProvider()));

        var error = await Assert.ThrowsAsync<WorkflowRuntimeCapabilityException>(() => host.RunAsync());

        var issue = Assert.Single(error.Issues);
        Assert.Equal("Capability", issue.NodeId);
        Assert.Equal(typeof(ITestCapability), issue.CapabilityType);
        Assert.False(handler.Executed);
    }

    [Fact]
    public async Task RunAsync_ExecutesAfterRequiredCapabilityIsAvailable()
    {
        var handler = new CapabilityNodeHandler();
        var nodes = new WorkflowNodeCatalog()
            .Register(WorkflowNodeDescriptor.Create<CapabilityNode>());
        var handlers = new WorkflowNodeHandlerCatalog()
            .Register(handler, WorkflowRuntimeCapabilityRequirement.Require<ITestCapability>());
        new WorkflowRuntimePluginCatalog(nodes, handlers).Freeze();
        var services = new WorkflowServiceProvider().Add<ITestCapability>(new TestCapability());
        using var host = new WorkflowRuntimeHost(nodes, handlers);
        host.Configure(
            CreateDocument(new CapabilityNode { Id = "Capability", Title = "Capability" }),
            new WorkflowContext(services));

        var result = await host.RunAsync();

        Assert.True(result.Success);
        Assert.True(handler.Executed);
    }

    [Fact]
    public void Binder_FreezesConfigurationDependentCapabilityRequirements()
    {
        var nodes = new WorkflowNodeCatalog()
            .Register(WorkflowNodeDescriptor.Create<CapabilityNode>());
        var handlers = new WorkflowNodeHandlerCatalog()
            .Register(new CapabilityNodeHandler(), node =>
                ((CapabilityNode)node).RequiresCapability
                    ? new[] { WorkflowRuntimeCapabilityRequirement.Require<ITestCapability>() }
                    : Array.Empty<WorkflowRuntimeCapabilityRequirement>());
        var sourceNode = new CapabilityNode
        {
            Id = "Capability",
            Title = "Capability",
            RequiresCapability = true
        };
        var plan = new WorkflowCompiler(nodes).Compile(CreateDocument(sourceNode));

        var boundPlan = new WorkflowRuntimeBinder(handlers).Bind(plan);
        sourceNode.RequiresCapability = false;

        var requirement = Assert.Single(boundPlan.GetRequiredCapabilities("Capability"));
        Assert.Equal(typeof(ITestCapability), requirement.CapabilityType);
    }

    private static WorkflowDocument CreateDocument(IWorkflowNodeModel node)
    {
        var document = new WorkflowDocument { EntryNodeId = node.Id };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        return document;
    }

    private interface ITestCapability
    {
    }

    private sealed class TestCapability : ITestCapability
    {
    }

    private sealed class CapabilityNode : WorkflowNodeModel
    {
        public CapabilityNode()
        {
        }

        public override string NodeType => "Test.Capability";

        public bool RequiresCapability { get; set; }
    }

    private sealed class CapabilityNodeHandler : WorkflowNodeHandler<CapabilityNode>
    {
        public bool Executed { get; private set; }

        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            CapabilityNode node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken)
        {
            _ = context.GetRequiredCapability<ITestCapability>();
            Executed = true;
            return ValueTask.FromResult(NodeExecutionResult.Complete());
        }
    }
}
