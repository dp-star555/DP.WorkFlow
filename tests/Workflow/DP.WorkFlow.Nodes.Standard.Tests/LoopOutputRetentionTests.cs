namespace DP.WorkFlow.Tests;

/// <summary>环形流程中节点再次执行时，上一次的输出及其资源随即释放，内存不随循环次数增长。</summary>
public sealed class LoopOutputRetentionTests
{
    [Fact]
    public async Task RunAsync_SerialLoop_RetainsOnlyLatestOutputAndRetiresSupersededProjections()
    {
        var loop = new LoopNodeModel { Id = "Loop", Title = "循环", Iterations = 20 };
        var body = new ProjectingNode { Id = "Body", Title = "取图" };
        var end = new EndNodeModel { Id = "End", Title = "结束" };
        var document = Document("环形取图", new IWorkflowNodeModel[] { loop, body, end },
            Connect("Loop", WorkflowPorts.Loop, "Body"),
            Connect("Body", WorkflowPorts.Success, "Loop"),
            Connect("Loop", WorkflowPorts.Completed, "End"));
        var handler = new ProjectingNodeHandler();
        var engine = CreateEngine(document, "Loop", handler);

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Single(engine.RunState.NodeOutputs, output => output.NodeId == "Body");
        Assert.Single(engine.RunState.NodeOutputs, output => output.NodeId == "Loop");
        var projections = handler.Projections["Body"];
        Assert.Equal(20, projections.Count);
        Assert.All(projections, projection => Assert.True(projection.Committed));
        // 前 19 次输出已被取代并释放；最后一次留给结果查看。
        Assert.All(projections.Take(19), projection => Assert.True(projection.Retired));
        Assert.False(projections[^1].Retired);
    }

    [Fact]
    public async Task RunAsync_ParallelInsideLoop_RetiresPreviousIterationBranchOutputsAfterMerge()
    {
        var loop = new LoopNodeModel { Id = "Loop", Title = "循环", Iterations = 5 };
        var parallel = new ParallelAllNodeModel { Id = "Parallel", Title = "并行" };
        var branchA = new ProjectingNode { Id = "A", Title = "分支A" };
        var branchB = new ProjectingNode { Id = "B", Title = "分支B" };
        var merge = new WaitAllInputsCompletedNodeModel { Id = "Merge", Title = "汇聚" };
        var end = new EndNodeModel { Id = "End", Title = "结束" };
        var document = Document("环形并行", new IWorkflowNodeModel[] { loop, parallel, branchA, branchB, merge, end },
            Connect("Loop", WorkflowPorts.Loop, "Parallel"),
            Connect("Parallel", WorkflowPorts.Branch, "A"),
            Connect("Parallel", WorkflowPorts.Branch, "B"),
            Connect("A", WorkflowPorts.Success, "Merge"),
            Connect("B", WorkflowPorts.Success, "Merge"),
            Connect("Merge", WorkflowPorts.Success, "Loop"),
            Connect("Loop", WorkflowPorts.Completed, "End"));
        var handler = new ProjectingNodeHandler();
        var engine = CreateEngine(document, "Loop", handler);

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        foreach (var nodeId in new[] { "A", "B" })
        {
            Assert.Single(engine.RunState.NodeOutputs, output => output.NodeId == nodeId);
            var projections = handler.Projections[nodeId];
            Assert.Equal(5, projections.Count);
            Assert.All(projections.Take(4), projection => Assert.True(projection.Retired));
            Assert.False(projections[^1].Retired);
        }
        Assert.Single(engine.RunState.NodeOutputs, output => output.NodeId == "Merge");
    }

    private static WorkflowEngine CreateEngine(WorkflowDocument document, string entryNodeId, ProjectingNodeHandler handler)
    {
        document.EntryNodeId = entryNodeId;
        var definition = new WorkflowCompiler(new WorkflowNodeCatalog().RegisterStandardNodes()).Compile(document);
        var handlers = new WorkflowNodeHandlerCatalog()
            .Register(new LoopNodeHandler())
            .Register(new ParallelAllNodeHandler())
            .Register(new WaitAllInputsCompletedNodeHandler())
            .Register(new EndNodeHandler())
            .Register(handler);
        return new WorkflowEngine(definition, handlers, new WorkflowContext());
    }

    private static WorkflowDocument Document(string name, IEnumerable<IWorkflowNodeModel> nodes, params WorkflowConnectionModel[] connections)
    {
        var document = new WorkflowDocument { Name = name };
        var canvas = document.CanvasProjection;
        foreach (var node in nodes)
            canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        foreach (var connection in connections)
            canvas.Connections.Add(connection);
        return document;
    }

    private static WorkflowConnectionModel Connect(string from, string port, string to) => new()
    {
        FromNodeId = from,
        FromPort = port,
        ToNodeId = to
    };

    private sealed class ProjectingNode : WorkflowNodeModel
    {
        public override string NodeType => "Test.Projecting";
    }

    /// <summary>每次执行返回新输出及一个记录发布与释放的投影，模拟持有图像帧的节点。</summary>
    private sealed class ProjectingNodeHandler : WorkflowNodeHandler<ProjectingNode>
    {
        private readonly object _sync = new();

        public Dictionary<string, List<RecordingProjection>> Projections { get; } = new(StringComparer.Ordinal);

        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            ProjectingNode node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken)
        {
            var projection = new RecordingProjection();
            lock (_sync)
            {
                if (!Projections.TryGetValue(node.Id, out var list))
                    Projections[node.Id] = list = new List<RecordingProjection>();
                list.Add(projection);
            }
            return ValueTask.FromResult(NodeExecutionResult.Continue(output: new object(), projection: projection));
        }
    }

    private sealed class RecordingProjection : IWorkflowNodeOutputProjection
    {
        public bool Committed { get; private set; }
        public bool Retired { get; private set; }

        public void Commit(long executionSequence) => Committed = true;

        public void Retire() => Retired = true;
    }
}
