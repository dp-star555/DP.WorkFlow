using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowStudioRuntimeBindingTests
{
    [Fact]
    public async Task RunAsync_InvokesLatestCanvasConfigurationCallback()
    {
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        var start = new StartNodeModel { Id = "Start", Title = "开始" };
        var canvasDocument = new WorkflowDocument { Name = "Run" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = start });
        canvasDocument.EntryNodeId = start.Id;
        var navigator = new WorkflowDesignerNavigator(canvasDocument, catalog);
        var handlers = new WorkflowNodeHandlerCatalog().Register(new StartNodeHandler());
        using var host = new WorkflowRuntimeHost(catalog, handlers);
        using var binding = new WorkflowStudioRuntimeBinding(host, navigator);
        var configured = 0;
        binding.ConfigureBeforeRun = () =>
        {
            configured++;
            host.Configure(navigator.RootDocument);
        };

        var result = await binding.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, configured);
    }

    [Fact]
    public async Task RunAsync_CurrentCanvas_RunsChildWithoutStartingRootWorkflow()
    {
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes();
        var childStart = new StartNodeModel { Id = "ChildStart", Title = "子开始" };
        var childDocument = new WorkflowDocument { Name = "Child" };
        var child = childDocument.CanvasProjection;
        child.Nodes.Add(new WorkflowCanvasNode { Node = childStart });
        childDocument.EntryNodeId = childStart.Id;
        var rootStart = new StartNodeModel { Id = "RootStart", Title = "根开始" };
        var block = new BlockNodeModel
        {
            Id = "Block",
            Title = "子流程",
            SubDocument = childDocument
        };
        var rootDocument = new WorkflowDocument { Name = "Root" };
        var root = rootDocument.CanvasProjection;
        root.Nodes.Add(new WorkflowCanvasNode { Node = rootStart });
        root.Nodes.Add(new WorkflowCanvasNode { Node = block });
        rootDocument.EntryNodeId = rootStart.Id;
        var navigator = new WorkflowDesignerNavigator(rootDocument, catalog);
        Assert.True(navigator.EnterSubCanvas(block.Id));
        var handlers = new WorkflowNodeHandlerCatalog().Register(new StartNodeHandler());
        using var host = new WorkflowRuntimeHost(catalog, handlers);
        using var binding = new WorkflowStudioRuntimeBinding(host, navigator)
        {
            AutoConfigureBeforeRun = true,
            RunTarget = WorkflowStudioRunTarget.CurrentCanvas
        };

        var result = await binding.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(E_NodeState.Completed, navigator.CurrentSession.GetNodeState(childStart.Id));
        Assert.Equal(E_NodeState.Idle, navigator.RootSession.GetNodeState(rootStart.Id));
    }
}
