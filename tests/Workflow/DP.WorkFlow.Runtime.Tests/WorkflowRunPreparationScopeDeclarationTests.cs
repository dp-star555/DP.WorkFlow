namespace DP.WorkFlow.Tests;

/// <summary>
/// AR-01：锁定各处准备调用点**声明的作用域**，而不是帧仓在给定作用域下的行为。
///
/// 分工：
/// - 本文件覆盖"调用点传对了没有"——走统管层故障会话运行处理流程的路径（嵌套运行）。
/// - <c>WorkflowVisionFrameScopeRunScopeTests</c> 覆盖"拿到作用域之后做对了没有"。
/// 两者缺一不可：只测后者时，把 <c>WorkflowEngine</c> 改回 <c>Root</c> 仍然全绿。
/// </summary>
public sealed class WorkflowRunPreparationScopeDeclarationTests
{
    [Fact]
    public async Task 恢复子流程的准备请求声明嵌套作用域并携带父节点ID()
    {
        var preparation = new RecordingPreparation();
        var scope = new CountingRunScopeOwner();
        var faulty = new FlakyNode { Id = "faulty", Title = "故障一次" };
        var treat = new TreatNode { Id = "treat", Title = "处置" };

        var catalog = new WorkflowNodeCatalog()
            .Register(WorkflowNodeDescriptor.Create<FlakyNode>(ports: new[] { WorkflowPortDescriptor.Output() }))
            .Register(WorkflowNodeDescriptor.Create<TreatNode>(ports: new[] { WorkflowPortDescriptor.Output() }));
        var handlers = new WorkflowNodeHandlerCatalog()
            .Register(new FlakyNodeHandler())
            .Register(new TreatNodeHandler());

        // 处置子流程：单节点即可，本测试只要求它被真的跑起来。
        var subflow = new WorkflowDocument { Name = "处置子流程", EntryNodeId = treat.Id };
        subflow.Nodes.Add(new WorkflowCanvasNode { Node = treat });

        var document = new WorkflowDocument { Name = "主流程", EntryNodeId = faulty.Id };
        document.Nodes.Add(new WorkflowCanvasNode { Node = faulty });

        var services = new WorkflowServiceProvider()
            .Add<IWorkflowRunPreparationService>(preparation)
            // 故意把资源所有者一并注册：根运行宿主会用它一次，处理流程（嵌套）不得再解析并调用它。
            .Add<IWorkflowRunResourceOwner>(preparation)
            // 运行作用域所有者同理：处理流程不得重新取得所有权（否则会重置采集代次，
            // 让父运行的未领取帧变成"上一代"而被清退）。
            .Add<IWorkflowRunScopeOwner>(scope);

        using var center = new WorkflowRunCenter(catalog, handlers,
            new WorkflowRunCenterOptions { DefaultFaultHandler = subflow, FaultSettleMilliseconds = 0 }, services);
        var flow = center.AddFlow(document);
        var result = await flow.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.Success, result.Message);
        var nested = Assert.Single(preparation.Contexts, context => context.ScopeKind == WorkflowRunScopeKind.Nested);
        Assert.Equal(faulty.Id, nested.ParentNodeId);
        Assert.Single(preparation.Contexts, context => context.ScopeKind == WorkflowRunScopeKind.Root);
        // AR-01 阶段2：退役上一轮资源是根运行宿主的专属动作；处理流程嵌套运行，不解析 IWorkflowRunResourceOwner。
        Assert.Equal(1, preparation.ReleaseCount);
        // V1-C：只有根宿主解析 IWorkflowRunScopeOwner；处理流程一次都不解析。
        Assert.Equal(1, scope.BeginCount);
        Assert.Equal(1, handlers.Resolve(treat) is TreatNodeHandler treatHandler ? treatHandler.ExecutionCount : -1);
    }

    private sealed class RecordingPreparation : IWorkflowRunPreparationService, IWorkflowRunResourceOwner
    {
        public List<WorkflowRunPreparationContext> Contexts { get; } = new();

        /// <summary>被调用过的退役次数；嵌套运行必须保持为 0。</summary>
        public int ReleaseCount { get; private set; }

        public ValueTask PrepareAsync(WorkflowRunPreparationContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Contexts.Add(context);
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleasePreviousRunAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseCount++;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>只计数的运行作用域所有者；引擎路径必须保持 0 次取得。</summary>
    private sealed class CountingRunScopeOwner : IWorkflowRunScopeOwner
    {
        public int BeginCount { get; private set; }

        public ValueTask<IWorkflowRunScopeLease> BeginRunAsync(Guid runId, CancellationToken cancellationToken)
        {
            BeginCount++;
            return ValueTask.FromResult<IWorkflowRunScopeLease>(new Lease(runId));
        }

        private sealed class Lease(Guid runId) : IWorkflowRunScopeLease
        {
            public Guid RunId { get; } = runId;

            public IReadOnlyList<string> OwnedResourceIds => Array.Empty<string>();

            public ValueTask DisposeAsync() => default;
        }
    }

    [WorkflowNode("TestScopeFlaky")]
    private sealed class FlakyNode : WorkflowNodeModel
    {
        public override string NodeType => "TestScopeFlaky";
    }

    private sealed class FlakyNodeHandler : WorkflowSideEffectFreeNodeHandler<FlakyNode>
    {
        public int ExecutionCount;

        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            FlakyNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return ValueTask.FromResult(ExecutionCount == 1
                ? NodeExecutionResult.Fail("首次失败")
                : NodeExecutionResult.Continue(output: ExecutionCount));
        }
    }

    [WorkflowNode("TestScopeTreat")]
    private sealed class TreatNode : WorkflowNodeModel
    {
        public override string NodeType => "TestScopeTreat";
    }

    private sealed class TreatNodeHandler : WorkflowNodeHandler<TreatNode>
    {
        public int ExecutionCount;

        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            TreatNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            // 处理流程给出去向：就地恢复出故障的节点。
            ((WorkflowFaultSession)context.Services.GetService(typeof(WorkflowFaultSession))!)
                .SetDirective(null, WorkflowResumeDirective.RecoverInPlace("已处置"));
            return ValueTask.FromResult(NodeExecutionResult.Continue());
        }
    }
}
