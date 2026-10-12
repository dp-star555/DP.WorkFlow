namespace DP.WorkFlow.Tests;

/// <summary>成员路径经过按键寻址的输出：先找公开属性，找不到时按键读取；缺键时给出输出自己的说明。</summary>
public sealed class WorkflowKeyedOutputTests
{
    [Theory]
    [InlineData("Items.a.Score", 0.75)]       // 信封没有 Score，落到测量项的值上。
    [InlineData("Items.a.Detail.Score", 0.5)] // 键之后继续按属性读取。
    [InlineData("Items.Count", 2.0)]          // 集合本身的属性仍然可读。
    [InlineData("Total", 2.0)]                // 普通静态路径不受影响。
    public async Task ResolveInput_ReadsThroughKeyedOutputs(string path, double expected)
    {
        var (result, context) = await RunAsync(path);
        Assert.True(result.Success, result.Message);
        Assert.True(context.TryGetVariable<double>("Resolved", out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("Items.zz.Score", "测量项 zz 不存在")]
    [InlineData("Items.b.Score", "测量项 乙 未找到：边缘不足")]
    [InlineData("Items.a.Missing", "测量项 甲 的结果没有成员 Missing")]
    public async Task ResolveInput_MissingKeyUsesOutputsOwnMessage(string path, string message)
    {
        var (result, _) = await RunAsync(path);
        Assert.False(result.Success);
        Assert.Contains(message, result.Message);
    }

    private static async Task<(WorkflowRunResult, WorkflowContext)> RunAsync(string path)
    {
        var producer = new ProducerNode { Id = "Producer" };
        var consumer = new ConsumerNode { Id = "Consumer", Input = WorkflowInput<double>.FromBinding(new WorkflowBindingKey("Producer", path)) };
        var document = new WorkflowDocument { Name = "按键寻址" };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = producer });
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = consumer });
        document.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = producer.Id, ToNodeId = consumer.Id });
        document.EntryNodeId = producer.Id;
        var context = new WorkflowContext();
        var handlers = new WorkflowNodeHandlerCatalog().Register(new ProducerHandler()).Register(new ConsumerHandler());
        var result = await new WorkflowEngine(new WorkflowCompiler(validateBindings: false).Compile(document), handlers, context).RunAsync();
        return (result, context);
    }

    private sealed class ProducerNode : WorkflowNodeModel { public override string NodeType => "Producer"; }

    private sealed class ConsumerNode : WorkflowNodeModel
    {
        public override string NodeType => "Consumer";
        public WorkflowInput<double> Input { get; set; } = WorkflowInput<double>.FromLiteral(0);
    }

    private sealed record Measure(double Score, Measure? Detail = null);

    private sealed class Item(string id, string name, Measure? value, string? error) : IWorkflowKeyedOutput
    {
        public string Id => id;
        public string Name => name;
        public bool Found => value is not null;
        public object? Value => value;
        public bool TryGetKeyedValue(string key, out object? result)
        {
            result = null;
            var property = value?.GetType().GetProperty(key);
            if (property is null) return false;
            result = property.GetValue(value);
            return true;
        }
        public string DescribeMissingKey(string key) => Found ? $"测量项 {name} 的结果没有成员 {key}。" : $"测量项 {name} 未找到：{error}。";
    }

    private sealed class Items(IReadOnlyList<Item> items) : IWorkflowKeyedOutput
    {
        public int Count => items.Count;
        public bool TryGetKeyedValue(string key, out object? value)
        {
            value = items.FirstOrDefault(i => i.Id == key);
            return value is not null;
        }
        public string DescribeMissingKey(string key) => $"测量项 {key} 不存在（已删除或未启用）。";
    }

    private sealed record Group(double Total, Items Items);

    private sealed class ProducerHandler : WorkflowNodeHandler<ProducerNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(ProducerNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(NodeExecutionResult.Continue(output: new Group(2, new Items(new[]
            {
                new Item("a", "甲", new Measure(.75, new Measure(.5)), null),
                new Item("b", "乙", null, "边缘不足")
            }))));
    }

    private sealed class ConsumerHandler : WorkflowNodeHandler<ConsumerNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(ConsumerNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            context.SetVariable("Resolved", context.ResolveInput(node.Input));
            return ValueTask.FromResult(NodeExecutionResult.Continue());
        }
    }
}
