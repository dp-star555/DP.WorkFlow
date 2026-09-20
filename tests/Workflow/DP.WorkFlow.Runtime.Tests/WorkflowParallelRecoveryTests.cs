namespace DP.WorkFlow.Tests;

public sealed class WorkflowParallelRecoveryTests
{
    [Fact]
    public async Task RunAsync_RecoverableFailureInsideParallelScope_IsRejectedBeforeCoordinator()
    {
        var fork = new ForkNode { Id = "Fork", Title = "并行" };
        var failed = new RecoverableNode { Id = "Failed", Title = "失败分支" };
        var passed = new PassNode { Id = "Passed", Title = "正常分支" };
        var join = new JoinNode { Id = "Join", Title = "汇聚" };
        var canvasDocument = new WorkflowDocument { Name = "并行恢复身份" };
        var canvas = canvasDocument.CanvasProjection;
        foreach (var node in new IWorkflowNodeModel[] { fork, failed, passed, join })
            canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvas.Connections.Add(Connect(fork.Id, WorkflowPorts.Branch, failed.Id));
        canvas.Connections.Add(Connect(fork.Id, WorkflowPorts.Branch, passed.Id));
        canvas.Connections.Add(Connect(failed.Id, WorkflowPorts.Success, join.Id));
        canvas.Connections.Add(Connect(passed.Id, WorkflowPorts.Success, join.Id));

        var coordinator = new CountingRecoveryCoordinator();
        var context = new WorkflowContext(
            new WorkflowServiceProvider().Add<IWorkflowFaultRecoveryCoordinator>(coordinator));
        var handlers = new WorkflowNodeHandlerCatalog()
            .Register(new ForkNodeHandler())
            .Register(new RecoverableNodeHandler())
            .Register(new PassNodeHandler())
            .Register(new JoinNodeHandler());
        canvasDocument.EntryNodeId = fork.Id;
        var definition = new WorkflowCompiler().Compile(canvasDocument);

        var result = await new WorkflowEngine(definition, handlers, context).RunAsync();

        Assert.False(result.Success);
        Assert.Contains("并行作用域", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, coordinator.CallCount);
    }

    private static WorkflowConnectionModel Connect(string fromNodeId, string fromPort, string toNodeId) => new()
    {
        FromNodeId = fromNodeId,
        FromPort = fromPort,
        ToNodeId = toNodeId
    };

    private sealed class ForkNode : WorkflowNodeModel, IWorkflowParallelForkNode
    {
        public override string NodeType => "Test.Fork";
    }

    private sealed class JoinNode : WorkflowNodeModel, IWorkflowParallelJoinNode
    {
        public override string NodeType => "Test.Join";
    }

    private sealed class RecoverableNode : WorkflowNodeModel
    {
        public override string NodeType => "Test.Recoverable";
    }

    private sealed class PassNode : WorkflowNodeModel
    {
        public override string NodeType => "Test.Pass";
    }

    private sealed class ForkNodeHandler : WorkflowNodeHandler<ForkNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            ForkNode node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(NodeExecutionResult.Continue(WorkflowPorts.Branch));
    }

    private sealed class JoinNodeHandler : WorkflowNodeHandler<JoinNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            JoinNode node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(NodeExecutionResult.Complete());
    }

    private sealed class RecoverableNodeHandler : WorkflowNodeHandler<RecoverableNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            RecoverableNode node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(NodeExecutionResult.Fail(
                "recoverable",
                WorkflowFaultDisposition.RequestRecovery,
                9001));
    }

    private sealed class PassNodeHandler : WorkflowNodeHandler<PassNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            PassNode node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(NodeExecutionResult.Continue());
    }

    private sealed class CountingRecoveryCoordinator : IWorkflowFaultRecoveryCoordinator
    {
        public int CallCount { get; private set; }

        public ValueTask<WorkflowFaultRecoveryDecision> RecoverAsync(
            WorkflowFaultRecoveryRequest request,
            IWorkflowFaultRecoveryContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(WorkflowFaultRecoveryDecision.Retry());
        }
    }
}
