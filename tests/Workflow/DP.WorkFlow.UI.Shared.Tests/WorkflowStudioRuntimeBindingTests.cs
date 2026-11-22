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
        var canvas = canvasDocument;
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
        var child = childDocument;
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
        var root = rootDocument;
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

    [Fact]
    public async Task Frames_PullSnapshotWhileRunning_WithoutEnginePush()
    {
        // R1-04：引擎不推送快照；绑定按帧比较变化计数，变了才取快照并更新画布。
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        var start = new StartNodeModel { Id = "Start", Title = "开始" };
        var action = new ActionNodeModel { Id = "Wait", Title = "等待", FunctionKey = "Wait" };
        var document = new WorkflowDocument { Name = "Frames", EntryNodeId = start.Id };
        document.Nodes.Add(new WorkflowCanvasNode { Node = start });
        document.Nodes.Add(new WorkflowCanvasNode { Node = action });
        document.Connections.Add(new WorkflowConnectionModel { FromNodeId = start.Id, ToNodeId = action.Id });
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var actions = new WorkflowActionRegistry().Register("Wait", async (_, token) =>
        {
            await release.Task.WaitAsync(token);
            return null;
        });
        var navigator = new WorkflowDesignerNavigator(document, catalog);
        var handlers = new WorkflowNodeHandlerCatalog().RegisterStandardNodeHandlers();
        using var host = new WorkflowRuntimeHost(catalog, handlers);
        host.Configure(document, new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions)));
        using var binding = new WorkflowStudioRuntimeBinding(host, navigator) { FrameIntervalMilliseconds = 20 };

        var run = host.RunAsync();
        await WaitUntilAsync(() => navigator.RootSession.GetNodeState(action.Id) == E_NodeState.Running);
        release.TrySetResult(true);
        var result = await run;
        // 完成属于停止类状态，立即推一帧，不用等帧检查。
        await WaitUntilAsync(() => navigator.RootSession.GetNodeState(action.Id) == E_NodeState.Completed);

        Assert.True(result.Success, result.Message);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(5, timeout.Token);
    }
}
