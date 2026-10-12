namespace DP.WorkFlow.Tests;

/// <summary>编辑器单节点试运行：读最近一次运行的上游输出，可替换输入，写入不回到流程。</summary>
public sealed class WorkflowNodeTrialRunnerTests
{
    [Fact]
    public async Task RunAsync_UsesLatestUpstreamOutputAndDiscardsWrites()
    {
        var (context, handlers, consumer) = await RunFlowAsync();
        consumer.Input = WorkflowInput<long>.FromBinding(new WorkflowBindingKey("Producer", "Count"));

        var result = await new WorkflowNodeTrialRunner(handlers, context).RunAsync(consumer);

        Assert.True(result.Success, result.Message);
        Assert.Equal(14L, result.Output);
    }

    [Fact]
    public async Task RunAsync_ReplacesInputByReference()
    {
        var (context, handlers, consumer) = await RunFlowAsync();
        consumer.Input = WorkflowInput<long>.FromBinding(new WorkflowBindingKey("Producer", "Count"));

        var result = await new WorkflowNodeTrialRunner(handlers, context)
            .RunAsync(consumer, new Dictionary<object, object?>(ReferenceEqualityComparer.Instance) { [consumer.Input] = 21L });

        Assert.Equal(42L, result.Output);
        // 流程运行写下的变量、公共数据和输出保持不变；试运行的写入被丢弃。
        Assert.True(context.TryGetVariable<long>("Seen", out var seen)); Assert.Equal(7L, seen);
        Assert.True(context.PublicData.TryGet<long>("Trial", out var published)); Assert.Equal(7L, published);
        Assert.True(context.TryGetNodeOutput("Consumer", out var output)); Assert.Equal(14L, output!.Value);
    }

    [Fact]
    public async Task RunAsync_WithoutUpstreamOutputThrowsBindingError()
    {
        var consumer = new ConsumerNode { Id = "Consumer", Input = WorkflowInput<long>.FromBinding(new WorkflowBindingKey("Producer", "Count")) };
        var runner = new WorkflowNodeTrialRunner(new WorkflowNodeHandlerCatalog().Register(new ConsumerHandler()), new WorkflowContext());

        var error = await Assert.ThrowsAsync<WorkflowBindingException>(() => runner.RunAsync(consumer));
        Assert.Contains("尚未产生输出", error.Message);
    }

    private static async Task<(WorkflowContext, WorkflowNodeHandlerCatalog, ConsumerNode)> RunFlowAsync()
    {
        var producer = new ProducerNode { Id = "Producer" };
        var consumer = new ConsumerNode { Id = "Consumer", Input = WorkflowInput<long>.FromBinding(new WorkflowBindingKey("Producer", "Count")) };
        var document = new WorkflowDocument { Name = "试运行" };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = producer });
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = consumer });
        document.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = producer.Id, ToNodeId = consumer.Id });
        document.EntryNodeId = producer.Id;
        var handlers = new WorkflowNodeHandlerCatalog().Register(new ProducerHandler()).Register(new ConsumerHandler());
        var context = new WorkflowContext();
        var run = await new WorkflowEngine(new WorkflowCompiler().Compile(document), handlers, context).RunAsync();
        Assert.True(run.Success, run.Message);
        return (context, handlers, consumer);
    }

    private sealed class ProducerNode : WorkflowNodeModel { public override string NodeType => "Producer"; }

    private sealed class ConsumerNode : WorkflowNodeModel
    {
        public override string NodeType => "Consumer";
        public WorkflowInput<long> Input { get; set; } = WorkflowInput<long>.FromLiteral(0);
    }

    private sealed record ProducerOutput(long Count);

    private sealed class ProducerHandler : WorkflowNodeHandler<ProducerNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(ProducerNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(NodeExecutionResult.Continue(output: new ProducerOutput(7)));
    }

    private sealed class ConsumerHandler : WorkflowNodeHandler<ConsumerNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(ConsumerNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            var value = context.ResolveInput(node.Input);
            context.SetVariable("Seen", value);
            context.PublishData("Trial", value);
            return ValueTask.FromResult(NodeExecutionResult.Continue(output: value * 2));
        }
    }
}
