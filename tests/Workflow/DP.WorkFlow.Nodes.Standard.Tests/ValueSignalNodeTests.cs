namespace DP.WorkFlow.Tests;

public sealed class ValueSignalNodeTests
{
    [Fact]
    public async Task ValueSignalService_WakesWithoutPolling()
    {
        var service = new WorkflowValueSignalService();
        var waiting = service.WaitAsync("Recipe", "R2", TimeSpan.FromSeconds(1), CancellationToken.None).AsTask();
        service.Write("Recipe", "R2");
        Assert.True(await waiting);
        Assert.Equal("R2", service.Read("Recipe"));
    }

    [Fact]
    public async Task SignalValueSet_UsesTypedIncrementAndStructuredResult()
    {
        var service = new WorkflowValueSignalService();
        service.WriteValue("Count", 4);
        var node = new SignalValueSetNodeModel { Id = "Set", Title = "增加", SignalKey = "Count", ValueType = E_SignalValueType.Int32, WriteMode = E_SignalValueWriteMode.Increment, LiteralValue = "3" };
        var canvasDocument = new WorkflowDocument { Name = "数据信号" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler(new WorkflowNodeCatalog().RegisterStandardNodes()).Compile(canvasDocument);
        var context = new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowValueSignalService>(service));
        var result = await new WorkflowEngine(definition, new WorkflowNodeHandlerCatalog().Register(new SignalValueSetNodeHandler()), context).RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(7, service.ReadSnapshot("Count")?.Value);
        Assert.True(context.TryGetVariable<SignalValueNodeResult>("SignalValueSetResult", out var output));
        Assert.Equal(E_SignalValueWriteMode.Increment, output!.WriteMode);
        Assert.Equal(7, output.Int32Value);
    }
}
