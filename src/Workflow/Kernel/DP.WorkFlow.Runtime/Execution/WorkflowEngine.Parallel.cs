namespace DP.WorkFlow;

public sealed partial class WorkflowEngine
{
    /// <summary>为静态并行定义创建运行时 Scope 和分支 Token，并等待全部分支到达汇聚点。</summary>
    /// <param name="scopeNode">触发派发的 ParallelAll 节点。</param>
    /// <param name="plan">编译期识别的分支入口和共同汇聚边界。</param>
    /// <param name="parentToken">拥有该并行作用域的父执行路径。</param>
    /// <param name="cancellationToken">父路径取消令牌；任一分支失败时也会取消其他分支。</param>
    private async Task ExecuteParallelScopeAsync(
        IWorkflowNodeModel scopeNode,
        WorkflowParallelScopePlan plan,
        ExecutionToken parentToken,
        CancellationToken cancellationToken)
    {
        var runtimeScopeId = Interlocked.Increment(ref _parallelScopeSequence);
        lock (_stateSync)
        {
            _parallelScopes[runtimeScopeId] = new MutableParallelScopeInfo
            {
                RuntimeScopeId = runtimeScopeId,
                ScopeNodeId = scopeNode.Id,
                MergeNodeId = plan.MergeNodeId,
                TotalBranches = plan.BranchEntryNodeIds.Count
            };
        }
        Context.RegisterParallelScope(runtimeScopeId, parentToken.TokenId);
        PublishSnapshot();

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Exception? firstException = null;
        var exceptionSync = new object();
        var tasks = plan.BranchEntryNodeIds.Select(async branchEntry =>
        {
            var ancestorTokenIds = new[] { parentToken.TokenId }
                .Concat(parentToken.AncestorTokenIds)
                .ToArray();
            var childToken = new ExecutionToken(
                Interlocked.Increment(ref _tokenSequence),
                Array.AsReadOnly(ancestorTokenIds),
                Array.AsReadOnly(parentToken.ScopeIds.Concat(new[] { runtimeScopeId }).ToArray()),
                parentToken.CopyLoopFrames());
            try
            {
                await ExecutePathAsync(
                    branchEntry,
                    plan.MergeNodeId,
                    childToken,
                    linkedCancellation.Token).ConfigureAwait(false);
                lock (_stateSync)
                    _parallelScopes[runtimeScopeId].CompletedBranches++;
                PublishSnapshot();
            }
            catch (Exception exception)
            {
                lock (exceptionSync)
                    firstException ??= exception;
                linkedCancellation.Cancel();
                throw;
            }
        }).ToArray();

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            if (firstException is not null)
                throw firstException;
            throw;
        }

        lock (_stateSync)
            _parallelScopes[runtimeScopeId].IsCompleted = true;
        Context.CompleteParallelScope(runtimeScopeId);
        var parentIdentity = new WorkflowExecutionIdentity(
            _runId,
            parentToken.TokenId,
            parentToken.AncestorTokenIds,
            parentToken.ScopeIds,
            0);
        WriteTrace(scopeNode, parentIdentity, "ParallelMerged", $"{plan.BranchEntryNodeIds.Count} 个分支全部到达汇聚节点 {plan.MergeNodeId}。", null);
        PublishSnapshot();
    }

    private sealed class ExecutionToken
    {
        private readonly Dictionary<string, int> _loopFrames;

        public ExecutionToken(
            long tokenId,
            IReadOnlyList<long> ancestorTokenIds,
            IReadOnlyList<long> scopeIds,
            IReadOnlyDictionary<string, int>? loopFrames = null)
        {
            TokenId = tokenId;
            AncestorTokenIds = ancestorTokenIds;
            ScopeIds = scopeIds;
            _loopFrames = loopFrames is null
                ? new Dictionary<string, int>(StringComparer.Ordinal)
                : new Dictionary<string, int>(loopFrames, StringComparer.Ordinal);
        }

        public long TokenId { get; }

        public IReadOnlyList<long> AncestorTokenIds { get; }

        public IReadOnlyList<long> ScopeIds { get; }

        public int PrepareLoopIteration(string nodeId) =>
            _loopFrames.TryGetValue(nodeId, out var completedIterations)
                ? completedIterations + 1
                : 1;

        public void CommitLoopIteration(string nodeId, int iteration) =>
            _loopFrames[nodeId] = iteration;

        public void CompleteLoop(string nodeId) => _loopFrames.Remove(nodeId);

        public IReadOnlyDictionary<string, int> CopyLoopFrames() =>
            new Dictionary<string, int>(_loopFrames, StringComparer.Ordinal);
    }

    private sealed class MutableParallelScopeInfo
    {
        public long RuntimeScopeId { get; init; }

        public required string ScopeNodeId { get; init; }

        public required string MergeNodeId { get; init; }

        public int TotalBranches { get; init; }

        public int CompletedBranches { get; set; }

        public bool IsCompleted { get; set; }

        public WorkflowParallelScopeInfo ToSnapshot() => new(
            RuntimeScopeId,
            ScopeNodeId,
            MergeNodeId,
            TotalBranches,
            CompletedBranches,
            IsCompleted);
    }
}
