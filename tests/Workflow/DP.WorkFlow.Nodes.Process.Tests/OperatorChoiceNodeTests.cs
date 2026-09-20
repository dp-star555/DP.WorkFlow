namespace DP.WorkFlow.Tests;

public sealed class OperatorChoiceNodeTests
{
    [Fact]
    public async Task OperatorChoice_SelectsReturnedDecisionPort()
    {
        var node = new OperatorChoiceNodeModel { Id = "Choice", Title = "选择", OptionsText = "重试=Retry,停止=Stop" };
        var canvasDocument = new WorkflowDocument { Name = "恢复选择" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var catalog = new WorkflowNodeCatalog().RegisterProcessNodes();
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler(catalog).Compile(canvasDocument);
        var services = new WorkflowServiceProvider().Add<IWorkflowOperatorService>(new RetryOperatorService());

        var result = await new WorkflowEngine(
            definition,
            new WorkflowNodeHandlerCatalog().RegisterProcessNodeHandlers(),
            new WorkflowContext(services)).RunAsync();

        Assert.True(result.Success);
    }

    [Fact]
    public void OperatorChoice_ExposesConfiguredDynamicOutputPorts()
    {
        var node = new OperatorChoiceNodeModel { OptionsText = "继续=Continue,重试=Retry,停止=Stop" };
        var descriptor = new WorkflowNodeCatalog().RegisterProcessNodes().GetOrThrow(node.NodeType);

        var outputs = descriptor.GetPorts(node).Where(port => port.Direction == WorkflowPortDirection.Output).Select(port => port.Key).ToArray();

        Assert.Equal(new[] { "Continue", "Retry", "Stop" }, outputs);
    }

    private sealed class RetryOperatorService : IWorkflowOperatorService
    {
        public ValueTask<string> AskChoiceAsync(string title, string message, IReadOnlyList<OperatorChoiceOption> options, CancellationToken cancellationToken) => ValueTask.FromResult("Retry");
        public ValueTask ConfirmStepAsync(string title, string message, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
