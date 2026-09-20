namespace DP.WorkFlow.Tests;

public sealed class WorkflowBindingResolverTests
{
    [Fact]
    public async Task ResolveInput_ReadsNestedMemberAndConvertsNumericType()
    {
        var producer = new ProducerNode { Id = "Producer", Title = "生产" };
        var consumer = new ConsumerNode
        {
            Id = "Consumer",
            Title = "消费",
            Input = WorkflowInput<long>.FromBinding(new WorkflowBindingKey("Producer", "Payload.Count"))
        };
        var canvasDocument = new WorkflowDocument { Name = "绑定解析" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = producer });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = consumer });
        canvas.Connections.Add(new WorkflowConnectionModel
        {
            FromNodeId = producer.Id,
            ToNodeId = consumer.Id
        });
        canvasDocument.EntryNodeId = producer.Id;
        var definition = new WorkflowCompiler().Compile(canvasDocument);
        var handlers = new WorkflowNodeHandlerCatalog()
            .Register(new ProducerHandler())
            .Register(new ConsumerHandler());
        var context = new WorkflowContext();

        var result = await new WorkflowEngine(definition, handlers, context).RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.True(context.TryGetVariable<long>("Resolved", out var resolved));
        Assert.Equal(7L, resolved);
    }

    [Fact]
    public async Task ResolveInput_ReadsTypedPublicDataMember()
    {
        var consumer = new ConsumerNode
        {
            Id = "Consumer",
            Input = WorkflowInput<long>.FromBinding(
                WorkflowBindingKey.FromPublicData("GlobalPayload", "Count"))
        };
        var canvasDocument = new WorkflowDocument { Name = "全局数据绑定" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = consumer });
        var publicData = new WorkflowPublicDataStore();
        publicData.Apply(new WorkflowPublicDataChangeSet(
            new Dictionary<string, object> { ["GlobalPayload"] = new Payload(19) },
            Array.Empty<string>()));
        var context = new WorkflowContext(publicData: publicData);
        canvasDocument.EntryNodeId = consumer.Id;
        var definition = new WorkflowCompiler().Compile(canvasDocument);

        var result = await new WorkflowEngine(definition,
            new WorkflowNodeHandlerCatalog().Register(new ConsumerHandler()), context).RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.True(context.TryGetVariable<long>("Resolved", out var resolved));
        Assert.Equal(19, resolved);
    }

    [Fact]
    public async Task ResolveInput_WhenSourceOutputDoesNotExist_ReturnsFaultWithBindingDetails()
    {
        var consumer = new ConsumerNode
        {
            Id = "Consumer",
            Title = "消费",
            Input = WorkflowInput<long>.FromBinding(new WorkflowBindingKey("Missing", "Value"))
        };
        var canvasDocument = new WorkflowDocument { Name = "无效绑定" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = consumer });
        canvasDocument.EntryNodeId = consumer.Id;
        // 关闭编译期检查以验证 Runtime 对外部/旧定义仍有最后一道防线。
        var definition = new WorkflowCompiler(validateBindings: false).Compile(canvasDocument);
        var handlers = new WorkflowNodeHandlerCatalog().Register(new ConsumerHandler());

        var result = await new WorkflowEngine(definition, handlers).RunAsync();

        Assert.False(result.Success);
        Assert.Contains("Missing|Value", result.Message);
    }

    private sealed class ProducerNode : WorkflowNodeModel
    {
        public override string NodeType => "Producer";
    }

    private sealed class ConsumerNode : WorkflowNodeModel
    {
        public override string NodeType => "Consumer";

        public WorkflowInput<long> Input { get; set; } = WorkflowInput<long>.FromLiteral(0);
    }

    private sealed class ProducerHandler : WorkflowNodeHandler<ProducerNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            ProducerNode node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(NodeExecutionResult.Continue(output: new ProducerOutput(new Payload(7))));
    }

    private sealed class ConsumerHandler : WorkflowNodeHandler<ConsumerNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            ConsumerNode node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken)
        {
            context.SetVariable("Resolved", context.ResolveInput(node.Input));
            return ValueTask.FromResult(NodeExecutionResult.Complete());
        }
    }

    private sealed record ProducerOutput(Payload Payload);

    private sealed record Payload(int Count);
}
