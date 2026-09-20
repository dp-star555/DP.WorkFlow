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
            .Add<IWorkflowRunPreparationService>(preparation);

        var result = await new WorkflowEngine(
                new WorkflowCompiler(catalog).Compile(document), handlers, new WorkflowContext(services),
                new WorkflowExecutionOptions { MaxRecoveryAttempts = 2 })
            .RunAsync();

        Assert.True(result.Success, result.Message);
        var recorded = Assert.Single(preparation.Contexts);
        Assert.Equal(WorkflowRunScopeKind.Nested, recorded.ScopeKind);
        Assert.Equal(faulty.Id, recorded.ParentNodeId);
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

    private sealed class RecordingPreparation : IWorkflowRunPreparationService
    {
        public List<WorkflowRunPreparationContext> Contexts { get; } = new();

        public ValueTask PrepareAsync(WorkflowRunPreparationContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Contexts.Add(context);
            return ValueTask.CompletedTask;
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
