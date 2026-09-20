namespace DP.WorkFlow.Tests;

public sealed class WorkflowParallelCompilationTests
{
    [Fact]
    public void Compile_ParallelBranches_ResolvesNearestCommonMerge()
    {
        var document = BuildParallelDocument(includeSecondMergeConnection: true);
        var canvas = document.CanvasProjection;

        document.EntryNodeId = "Parallel";
        var definition = new WorkflowCompiler().Compile(document);
        var scope = definition.GetParallelScopeOrThrow("Parallel");

        Assert.Equal(new[] { "BranchA", "BranchB" }, scope.BranchEntryNodeIds.OrderBy(id => id));
        Assert.Equal("Merge", scope.MergeNodeId);
    }

    [Fact]
    public void Compile_ParallelWithoutCommonMerge_ReturnsStructuredError()
    {
        var document = BuildParallelDocument(includeSecondMergeConnection: false);
        var canvas = document.CanvasProjection;

        document.EntryNodeId = "Parallel";
        var exception = Assert.Throws<WorkflowCompilationException>(
            () => new WorkflowCompiler().Compile(document));

        Assert.Contains(exception.Errors, error => error.Code == "WF021");
    }

    [Fact]
    public void Compile_CommonReachableJoinThatDoesNotPostDominateEveryBranch_IsRejected()
    {
        var document = BuildParallelDocument(includeSecondMergeConnection: true);
        var canvas = document.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new TestNode("EarlyTerminal", "End") });
        canvas.Connections.Add(Connect("BranchB", "Alternate", "EarlyTerminal"));

        document.EntryNodeId = "Parallel";
        var exception = Assert.Throws<WorkflowCompilationException>(
            () => new WorkflowCompiler().Compile(document));

        Assert.Contains(exception.Errors, error => error.Code == "WF021");
    }

    [Fact]
    public void Compile_ParallelBranchesOverlapBeforeMerge_ReturnsStructuredError()
    {
        var document = BuildParallelDocument(includeSecondMergeConnection: false);
        var canvas = document.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new TestNode("Shared", "Action") });
        canvas.Connections.Remove(canvas.Connections.Single(connection => connection.FromNodeId == "BranchA"));
        canvas.Connections.Add(Connect("BranchA", WorkflowPorts.Success, "Shared"));
        canvas.Connections.Add(Connect("BranchB", WorkflowPorts.Success, "Shared"));
        canvas.Connections.Add(Connect("Shared", WorkflowPorts.Success, "Merge"));

        document.EntryNodeId = "Parallel";
        var exception = Assert.Throws<WorkflowCompilationException>(
            () => new WorkflowCompiler().Compile(document));

        Assert.Contains(exception.Errors, error => error.Code == "WF022");
    }

    private static WorkflowDocument BuildParallelDocument(bool includeSecondMergeConnection)
    {
        var document = new WorkflowDocument { Name = "并行编译" };
        var canvas = document.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new ParallelAllNodeModel { Id = "Parallel", Title = "Parallel" } });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new TestNode("BranchA", "Action") });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new TestNode("BranchB", "Action") });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new WaitAllInputsCompletedNodeModel { Id = "Merge", Title = "Merge" } });
        canvas.Connections.Add(Connect("Parallel", WorkflowPorts.Branch, "BranchA"));
        canvas.Connections.Add(Connect("Parallel", WorkflowPorts.Branch, "BranchB"));
        canvas.Connections.Add(Connect("BranchA", WorkflowPorts.Success, "Merge"));
        if (includeSecondMergeConnection)
            canvas.Connections.Add(Connect("BranchB", WorkflowPorts.Success, "Merge"));
        return document;
    }

    private static WorkflowConnectionModel Connect(string from, string port, string to) => new()
    {
        FromNodeId = from,
        FromPort = port,
        ToNodeId = to
    };

    private sealed class TestNode : WorkflowNodeModel
    {
        private readonly string _nodeType;

        public TestNode(string id, string nodeType)
        {
            Id = id;
            Title = id;
            _nodeType = nodeType;
        }

        public override string NodeType => _nodeType;
    }
}
