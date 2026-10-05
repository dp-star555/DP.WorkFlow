namespace DP.WorkFlow.Tests;

public sealed class WorkflowTransactionalPreparationTests
{
    [Fact]
    public async Task RootRun_CommitsCandidateAfterScopeAcquisition_AndRetiresAfterExecution()
    {
        var events = new List<string>();
        var preparation = new Preparation(events);
        var services = new WorkflowServiceProvider().Add<IWorkflowRunPreparationService>(preparation)
            .Add<IWorkflowRunScopeOwner>(new Scope(events));
        using var host = Host(services, events);

        Assert.True((await host.RunAsync()).Success);

        Assert.Equal(new[] { "prepare", "begin", "commit", "execute", "scope.release", "candidate.release" }, events);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RootRun_FailureBeforeCommit_RollsBackCandidateWithoutExecuting(bool scopeFailure)
    {
        var events = new List<string>();
        var services = new WorkflowServiceProvider().Add<IWorkflowRunPreparationService>(new Preparation(events));
        if (scopeFailure) services.Add<IWorkflowRunScopeOwner>(new Scope(events, fail: true));
        else services.Add<IWorkflowRunResourceOwner>(new RejectRetirement());
        using var host = Host(services, events);

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync());

        Assert.Contains("candidate.release", events);
        Assert.DoesNotContain("commit", events);
        Assert.DoesNotContain("execute", events);
    }

    private static WorkflowRuntimeHost Host(WorkflowServiceProvider services, List<string> events)
    {
        var document = new WorkflowDocument { EntryNodeId = "node" }; document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new Node { Id = "node" } });
        var host = new WorkflowRuntimeHost(new WorkflowNodeCatalog().Register<Node>(), new WorkflowNodeHandlerCatalog().Register(new Handler(events)));
        host.Configure(document, new WorkflowContext(services)); return host;
    }
    private sealed class Node : WorkflowNodeModel { public override string NodeType => "Test.Transaction"; }
    private sealed class Handler(List<string> events) : WorkflowNodeHandler<Node>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(Node node, IWorkflowNodeExecutionContext context, CancellationToken token)
        { events.Add("execute"); return ValueTask.FromResult(NodeExecutionResult.Complete()); }
    }
    private sealed class Preparation(List<string> events) : IWorkflowTransactionalRunPreparationService
    {
        public ValueTask PrepareAsync(WorkflowRunPreparationContext context, CancellationToken token) => throw new InvalidOperationException("不能使用旧准备入口。");
        public ValueTask<IWorkflowPreparedRun> PrepareRunAsync(WorkflowRunPreparationContext context, CancellationToken token)
        {
            Assert.NotEqual(Guid.Empty, context.BindingScopeId); Assert.NotNull(context.PositionedNodes);
            events.Add("prepare"); return ValueTask.FromResult<IWorkflowPreparedRun>(new Candidate(events));
        }
    }
    private sealed class Candidate(List<string> events) : IWorkflowPreparedRun
    {
        public void Commit() => events.Add("commit");
        public ValueTask DisposeAsync() { events.Add("candidate.release"); return ValueTask.CompletedTask; }
    }
    private sealed class Scope(List<string> events, bool fail = false) : IWorkflowRunScopeOwner
    {
        public ValueTask<IWorkflowRunScopeLease> BeginRunAsync(Guid runId, CancellationToken token)
        {
            events.Add("begin"); if (fail) throw new InvalidOperationException("无法取得运行作用域。");
            return ValueTask.FromResult<IWorkflowRunScopeLease>(new Lease(runId, events));
        }
    }
    private sealed class Lease(Guid id, List<string> events) : IWorkflowRunScopeLease
    {
        public Guid RunId => id;
        public IReadOnlyList<string> OwnedResourceIds => Array.Empty<string>();
        public ValueTask DisposeAsync() { events.Add("scope.release"); return ValueTask.CompletedTask; }
    }
    private sealed class RejectRetirement : IWorkflowRunResourceOwner
    {
        public ValueTask ReleasePreviousRunAsync(CancellationToken token) => throw new InvalidOperationException("旧资源退役失败。");
    }
}
