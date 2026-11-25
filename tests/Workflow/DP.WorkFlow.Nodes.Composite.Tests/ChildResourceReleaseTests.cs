namespace DP.WorkFlow.Tests;

/// <summary>R2-e：子流程结束时只把输出映射引用的资源交给父流程，其余立即归还。</summary>
public sealed class ChildResourceReleaseTests
{
    [Fact]
    public async Task Block_KeepsOnlyResourcesReferencedByOutputs()
    {
        var handler = new ProjectingNodeHandler();
        var block = new BlockNodeModel { Id = "Block", Title = "子流程", SubDocument = Child() };
        block.Outputs.Add(new WorkflowSubflowOutput { Name = "Image", Source = new WorkflowBindingKey("Kept", "$") });
        var engine = CreateEngine(block, handler);

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.True(Assert.Single(handler.Projections["Internal"]).Retired);
        Assert.False(Assert.Single(handler.Projections["Kept"]).Retired);
    }

    [Fact]
    public async Task LoopBlock_KeepsOnlyResourcesReferencedByOutputs()
    {
        var handler = new ProjectingNodeHandler();
        var loop = new LoopBlockNodeModel { Id = "Block", Title = "循环块", SubDocument = Child(), Iterations = 3 };
        loop.Outputs.Add(new WorkflowSubflowOutput { Name = "Image", Source = new WorkflowBindingKey("Kept", "$") });
        var engine = CreateEngine(loop, handler);

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.All(handler.Projections["Internal"], projection => Assert.True(projection.Retired));
        Assert.All(handler.Projections["Kept"].Take(2), projection => Assert.True(projection.Retired));
        Assert.False(handler.Projections["Kept"][^1].Retired);
    }

    private static WorkflowDocument Child()
    {
        var start = new StartNodeModel { Id = "ChildStart", Title = "开始" };
        var child = new WorkflowDocument { Name = "Child", EntryNodeId = start.Id };
        foreach (var node in new IWorkflowNodeModel[] { start, new ProjectingNode { Id = "Internal", Title = "内部取图" }, new ProjectingNode { Id = "Kept", Title = "交出取图" } })
            child.Nodes.Add(new WorkflowCanvasNode { Node = node });
        child.Connections.Add(Connect("ChildStart", "Internal"));
        child.Connections.Add(Connect("Internal", "Kept"));
        return child;
    }

    private static WorkflowEngine CreateEngine(IWorkflowNodeModel block, ProjectingNodeHandler handler)
    {
        var start = new StartNodeModel { Id = "Start", Title = "开始" };
        var parent = new WorkflowDocument { Name = "Parent", EntryNodeId = start.Id };
        parent.Nodes.Add(new WorkflowCanvasNode { Node = start });
        parent.Nodes.Add(new WorkflowCanvasNode { Node = block });
        parent.Connections.Add(Connect("Start", block.Id));
        var definition = new WorkflowCompiler(new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes()).Compile(parent);
        var handlers = new WorkflowNodeHandlerCatalog().RegisterCompositeNodeHandlers().Register(new StartNodeHandler()).Register(handler);
        return new WorkflowEngine(definition, handlers, new WorkflowContext());
    }

    private static WorkflowConnectionModel Connect(string from, string to) => new()
    {
        FromNodeId = from, FromPort = WorkflowPorts.Success, ToNodeId = to, ToPort = WorkflowPorts.Input
    };

    private sealed class ProjectingNode : WorkflowNodeModel
    {
        public override string NodeType => "Test.Projecting";
    }

    private sealed class ProjectingNodeHandler : WorkflowNodeHandler<ProjectingNode>
    {
        private readonly object _sync = new();

        public Dictionary<string, List<RecordingProjection>> Projections { get; } = new(StringComparer.Ordinal);

        protected override ValueTask<NodeExecutionResult> ExecuteAsync(ProjectingNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
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
        public bool Retired { get; private set; }
        public void Commit(long executionSequence) { }
        public void Retire() => Retired = true;
    }
}
