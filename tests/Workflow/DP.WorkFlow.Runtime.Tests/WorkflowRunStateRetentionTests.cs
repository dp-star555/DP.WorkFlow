namespace DP.WorkFlow.Tests;

/// <summary>输出历史只保留仍可能被读取的执行实例，环形流程不随循环次数累积输出。</summary>
public sealed class WorkflowRunStateRetentionTests
{
    [Fact]
    public void Commit_SameNodeTokenAndScope_SupersedesPreviousOutput()
    {
        var (state, runId) = Begin();

        var first = state.CommitNodeOutput("Capture", Identity(runId, token: 1), "frame-1", out var none);
        var second = state.CommitNodeOutput("Capture", Identity(runId, token: 1), "frame-2", out var superseded);

        Assert.Empty(none);
        Assert.Same(first, Assert.Single(superseded));
        Assert.Same(second, Assert.Single(state.NodeOutputs));
        Assert.True(state.TryGetVisibleNodeOutput("Capture", Identity(runId, token: 1), out var visible));
        Assert.Same(second, visible);
    }

    [Fact]
    public void Commit_DifferentTokenOrScope_KeepsOutputsThatRemainVisibleElsewhere()
    {
        var (state, runId) = Begin();
        state.RegisterParallelScope(1, ownerTokenId: 1);

        state.CommitNodeOutput("Node", Identity(runId, token: 2, scopes: new long[] { 1 }), "a", out _);
        state.CommitNodeOutput("Node", Identity(runId, token: 3, scopes: new long[] { 1 }), "b", out var superseded);

        Assert.Empty(superseded);
        Assert.Equal(2, state.NodeOutputs.Count);
    }

    [Fact]
    public void Commit_DoesNotRemoveInvalidatedOutputs()
    {
        var (state, runId) = Begin();
        var first = state.CommitNodeOutput("Prepare", Identity(runId, token: 1), 1, out _);
        state.InvalidateOutputsFrom(first.ExecutionSequence);

        state.CommitNodeOutput("Prepare", Identity(runId, token: 1), 2, out var superseded);

        Assert.Empty(superseded);
        Assert.Equal(2, state.NodeOutputs.Count);
        Assert.Contains(first.ExecutionSequence, state.InvalidatedOutputSequences);
    }

    [Fact]
    public void CompleteParallelScope_SupersedesBranchOutputsOfEarlierScopeWithSameOwner()
    {
        var (state, runId) = Begin();
        state.RegisterParallelScope(1, ownerTokenId: 1);
        var old = state.CommitNodeOutput("Branch", Identity(runId, token: 2, ancestors: new long[] { 1 }, scopes: new long[] { 1 }), "old", out _);
        state.CompleteParallelScope(1, out var none);
        state.RegisterParallelScope(2, ownerTokenId: 1);
        var current = state.CommitNodeOutput("Branch", Identity(runId, token: 3, ancestors: new long[] { 1 }, scopes: new long[] { 2 }), "new", out var beforeMerge);

        // 作用域汇聚前，上一轮分支输出仍可被兄弟分支读到，不能提前移除。
        Assert.Empty(none);
        Assert.Empty(beforeMerge);
        state.CompleteParallelScope(2, out var superseded);

        Assert.Same(old, Assert.Single(superseded));
        Assert.Same(current, Assert.Single(state.NodeOutputs));
        Assert.True(state.TryGetVisibleNodeOutput("Branch", Identity(runId, token: 1), out var visible));
        Assert.Same(current, visible);
    }

    private static (WorkflowRunState State, Guid RunId) Begin()
    {
        var state = new WorkflowRunState();
        var runId = Guid.NewGuid();
        state.Begin(runId);
        return (state, runId);
    }

    private static WorkflowExecutionIdentity Identity(
        Guid runId,
        long token,
        long[]? ancestors = null,
        long[]? scopes = null) =>
        new(runId, token, ancestors ?? Array.Empty<long>(), scopes ?? Array.Empty<long>(), 1);
}
