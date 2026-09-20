namespace DP.WorkFlow.Tests;

public sealed class SignalNodeTests
{
    [Fact]
    public async Task SetThenWait_UsesInjectedSignalServiceAndResetsMatchedSignal()
    {
        var set = new SignalSetNodeModel
        {
            Id = "Set",
            Title = "设置信号",
            SignalKey = "Ready",
            State = WorkflowInput<bool>.FromLiteral(true)
        };
        var wait = new SignalWaitNodeModel
        {
            Id = "Wait",
            Title = "等待信号",
            SignalKey = "Ready",
            ExpectedState = WorkflowInput<bool>.FromLiteral(true),
            Timeout = TimeSpan.FromSeconds(1),
            AutoResetAfterMatched = true
        };
        var canvasDocument = new WorkflowDocument { Name = "信号" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = set });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = wait });
        canvas.Connections.Add(new WorkflowConnectionModel
        {
            FromNodeId = set.Id,
            FromPort = WorkflowPorts.Success,
            ToNodeId = wait.Id
        });
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        canvasDocument.EntryNodeId = set.Id;
        var definition = new WorkflowCompiler(catalog).Compile(canvasDocument);
        var signals = new WorkflowSignalService();
        var services = new WorkflowServiceProvider().Add<IWorkflowSignalService>(signals);
        var handlers = new WorkflowNodeHandlerCatalog()
            .Register(new SignalSetNodeHandler())
            .Register(new SignalWaitNodeHandler());

        var result = await new WorkflowEngine(definition, handlers, new WorkflowContext(services)).RunAsync();

        Assert.True(result.Success);
        Assert.False(signals.Read("Ready"));
    }

    [Fact]
    public async Task SignalService_WaitDoesNotPollAndWakesWhenValueChanges()
    {
        var signals = new WorkflowSignalService();
        var waiting = signals.WaitAsync("Ready", true, TimeSpan.FromSeconds(1), CancellationToken.None).AsTask();

        signals.Write("Ready", true);

        Assert.True(await waiting);
    }
}
