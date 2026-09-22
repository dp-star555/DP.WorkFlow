namespace DP.WorkFlow.Tests;

/// <summary>
/// 实施细节 §12.4：真实节点集成。Handler 内部只调用普通 <c>ResolveInput(input)</c>，
/// 输入键与来源血缘必须由绑定阶段冻结的输入槽元数据自动产生，测试不得依赖任何手写输入名。
/// </summary>
public sealed class WorkflowRunRecordingNodeIntegrationTests
{
    [Fact]
    public async Task StringCompare_RealNode_RecordsAutomaticKeysForBothSlots()
    {
        var sink = new CollectingSink();
        var document = new WorkflowDocument { Name = "字符串比较血缘", EntryNodeId = "Producer" };
        var canvas = document.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode
        {
            Node = new ActionNodeModel { Id = "Producer", Title = "生产", FunctionKey = "Produce" }
        });
        canvas.Nodes.Add(new WorkflowCanvasNode
        {
            Node = new StringCompareNodeModel
            {
                Id = "Compare",
                Title = "比较",
                Operator = E_StringCompareOperator.Equals,
                // 左值绑定真实上游输出成员，右值是字面量：两个槽必须分别得到自己的稳定键。
                Left = WorkflowInput<string?>.FromBinding(new WorkflowBindingKey("Producer", "Text")),
                Right = WorkflowInput<string?>.FromLiteral("hello")
            }
        });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new EndNodeModel { Id = "End", Title = "结束" } });
        canvas.Connections.Add(Connect("Producer", WorkflowPorts.Success, "Compare"));
        canvas.Connections.Add(Connect("Compare", WorkflowPorts.True, "End"));

        var services = new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(
            new WorkflowActionRegistry().Register(
                "Produce",
                (_, _) => ValueTask.FromResult<object?>(new StringPayload("hello"))));
        var engine = new WorkflowEngine(
            new WorkflowCompiler(new WorkflowNodeCatalog().RegisterStandardNodes()).Compile(document),
            new WorkflowNodeHandlerCatalog().RegisterStandardNodeHandlers(),
            new WorkflowContext(services),
            new WorkflowExecutionOptions { Recording = new WorkflowRunRecordingOptions { Sink = sink } });

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        var producer = Assert.Single(engine.Context.ExportNodeOutputs(), item => item.NodeId == "Producer");
        var resolved = sink.Events.Where(item => item.EventType == "InputResolved").ToArray();
        // StringCompareNodeHandler 只写 context.ResolveInput(node.Left/node.Right)：
        // 两个输入键都必须自动来自属性名，且顺序稳定。
        Assert.Equal(new[] { "Left", "Right" }, resolved.Select(item => item.Data!["InputKey"].Text));
        Assert.All(resolved, item => Assert.Equal("Automatic", item.Data!["InputMetadataStatus"].Text));
        var left = resolved[0].Data!;
        var right = resolved[1].Data!;
        // 左值记录到真实上游输出的成员血缘。
        Assert.Equal("NodeOutput", left["SourceKind"].Text);
        Assert.Equal("Producer", left["SourceNodeId"].Text);
        Assert.Equal("Text", left["SourceOutputKey"].Text);
        Assert.Equal(producer.ExecutionSequence, Convert.ToInt64(left["SourceOutputSequence"].Scalar));
        // 右值是字面量，不携带来源节点身份。
        Assert.Equal("Literal", right["SourceKind"].Text);
        Assert.Null(right["SourceOutputKey"].Scalar);
        // 真实节点的正式输出同样自动提取稳定键。
        var committed = Assert.Single(
            sink.Events,
            item => item.EventType == "OutputCommitted" && item.NodeId == "Compare");
        Assert.Contains("Value", committed.Data!["OutputKeys"].Text);
        Assert.Equal(typeof(CompareNodeResult).FullName, committed.Data["OutputType"].Text);
    }

    private static WorkflowConnectionModel Connect(string from, string port, string to) => new()
    {
        FromNodeId = from,
        FromPort = port,
        ToNodeId = to
    };

    private sealed record StringPayload(string Text);

    private sealed class CollectingSink : IWorkflowRunEventSink
    {
        private readonly object _sync = new();
        private readonly List<WorkflowRunEvent> _events = new();

        public IReadOnlyList<WorkflowRunEvent> Events
        {
            get
            {
                lock (_sync)
                    return _events.OrderBy(item => item.Sequence).ToArray();
            }
        }

        public ValueTask<WorkflowRunEventWriteResult> WriteAsync(
            IReadOnlyList<WorkflowRunEvent> events,
            bool requestImmediateFlush,
            CancellationToken cancellationToken)
        {
            lock (_sync)
                _events.AddRange(events);
            return ValueTask.FromResult(WorkflowRunEventWriteResult.Success);
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
