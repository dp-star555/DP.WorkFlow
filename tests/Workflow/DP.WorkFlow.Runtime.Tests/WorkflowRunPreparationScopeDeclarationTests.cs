namespace DP.WorkFlow.Tests;

/// <summary>
/// AR-01：锁定各处准备调用点**声明的作用域**，而不是帧仓在给定作用域下的行为。
///
/// 分工：
/// - 本文件覆盖"调用点传对了没有"——走真实引擎的恢复路径（prepare: true 的嵌套运行）。
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
        subflow.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = treat });

        var document = new WorkflowDocument { Name = "主流程", EntryNodeId = faulty.Id };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = faulty });

        var services = new WorkflowServiceProvider()
            .Add<IWorkflowFaultRecoveryCoordinator>(new SubflowRecoveryCoordinator(subflow, catalog, handlers))
            .Add<IWorkflowRunPreparationService>(preparation)
            // 故意把资源所有者一并注册：即使它在容器里可达，嵌套调用点也不得解析并调用它。
            .Add<IWorkflowRunResourceOwner>(preparation)
            // 运行作用域所有者同理：嵌套运行不得重新取得所有权（否则会重置采集代次，
            // 让父运行的未领取帧变成"上一代"而被清退）。
            .Add<IWorkflowRunScopeOwner>(scope);

        var result = await new WorkflowEngine(
                new WorkflowCompiler(catalog).Compile(document), handlers, new WorkflowContext(services),
                new WorkflowExecutionOptions { MaxRecoveryAttempts = 2 })
            .RunAsync();

        Assert.True(result.Success, result.Message);
        var recorded = Assert.Single(preparation.Contexts);
        Assert.Equal(WorkflowRunScopeKind.Nested, recorded.ScopeKind);
        Assert.Equal(faulty.Id, recorded.ParentNodeId);
        // AR-01 阶段2：退役上一轮资源是根运行宿主的专属动作。引擎的嵌套路径不解析
        // IWorkflowRunResourceOwner，因此这里必须一次都没有发生——这是"父资源只由父释放"的类型级保证。
        Assert.Equal(0, preparation.ReleaseCount);
        // V1-C：引擎侧只有根宿主解析 IWorkflowRunScopeOwner；引擎自身（含嵌套路径）一次都不解析。
        Assert.Equal(0, scope.BeginCount);
    }

    /// <summary>
    /// 走与生产一致的受监管入口：<c>IWorkflowRecoverySubflowContext</c> → <c>RunTrackedChildAsync(prepare: true)</c>。
    /// 这正是 AR-01 的破坏点所在的那条路径。
    /// </summary>
    private sealed class SubflowRecoveryCoordinator : IWorkflowFaultRecoveryCoordinator
    {
        private readonly WorkflowBoundExecutionPlan _plan;

        public SubflowRecoveryCoordinator(WorkflowDocument subflow, WorkflowNodeCatalog catalog, WorkflowNodeHandlerCatalog handlers) =>
            _plan = new WorkflowRuntimeBinder(handlers).Bind(new WorkflowCompiler(catalog).Compile(subflow));

        public async ValueTask<WorkflowFaultRecoveryDecision> RecoverAsync(
            WorkflowFaultRecoveryRequest request, IWorkflowFaultRecoveryContext context, CancellationToken cancellationToken)
        {
            var childContext = new WorkflowContext(context.Services);
            if (context is IWorkflowRecoverySubflowContext supervised)
                await supervised.RunRecoverySubflowAsync(_plan, childContext,
                    new WorkflowExecutionOptions { MaxRecoveryAttempts = 1 }, cancellationToken).ConfigureAwait(false);
            return WorkflowFaultRecoveryDecision.Retry("已处置");
        }
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
    private sealed class FlakyNode : WorkflowNodeModel, IWorkflowRetrySafetyNode
    {
        public override string NodeType => "TestScopeFlaky";
        public WorkflowRetrySafety RetrySafety => WorkflowRetrySafety.Idempotent;
    }

    private sealed class FlakyNodeHandler : WorkflowNodeHandler<FlakyNode>
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
            return ValueTask.FromResult(NodeExecutionResult.Continue());
        }
    }
}
