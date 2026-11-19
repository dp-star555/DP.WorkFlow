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
        var engine = new WorkflowEngine(definition, new WorkflowNodeHandlerCatalog().Register(new SignalValueSetNodeHandler()), context);
        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(7, service.ReadSnapshot("Count")?.Value);
        // R2-d：结果只通过节点输出提供，不再写入流程变量。
        var output = Assert.IsType<SignalValueNodeResult>(engine.FindLatestNodeOutput(engine.RunState.RunId, node.Id)!.Value);
        Assert.False(context.TryGetVariable<SignalValueNodeResult>("SignalValueSetResult", out _));
        Assert.Equal(E_SignalValueWriteMode.Increment, output!.WriteMode);
        Assert.Equal(7, output.Int32Value);
    }
}
