using DP.Vision;

namespace DP.WorkFlow.Tests;

/// <summary>AR-01 回归：只有根运行可以释放上一轮资源，嵌套运行必须保持既有资源不变。</summary>
public sealed class WorkflowVisionFrameScopeRunScopeTests
{
    private static WorkflowRunPreparationContext Context(WorkflowRunScopeKind kind) =>
        new(Array.Empty<IWorkflowNodeModel>(), kind);

    /// <summary>
    /// 模拟一次采图/载图节点输出：帧进入运行帧仓，节点输出持有的是同一个仓内句柄。
    /// 局部临时句柄全部释放后，仓内句柄成为唯一持有者。
    /// </summary>
    private static ImageFrame PublishNodeOutput(WorkflowVisionFrameScope scope)
    {
        using var source = VisionImage.CopyFrom(new ImageInfo(1, 1, EPixelLayout.Gray8), new byte[] { 7 });
        using var local = new ImageFrame("f1", source);
        return scope.Retain(local);
    }

    [Fact]
    public async Task 嵌套运行准备不得释放根运行已保留的帧()
    {
        using var scope = new WorkflowVisionFrameScope();
        await scope.PrepareAsync(Context(WorkflowRunScopeKind.Root), CancellationToken.None);

        var nodeOutput = PublishNodeOutput(scope);

        // 本轮内部起嵌套运行（故障处置子流程）
        await scope.PrepareAsync(Context(WorkflowRunScopeKind.Nested), CancellationToken.None);

        // 修复前：仓内句柄已被无条件清空，父运行的节点输出失效并抛 ObjectDisposedException。
        using var lease = nodeOutput.Retain();
        Assert.Equal("f1", lease.FrameId);
    }

    [Fact]
    public async Task 准备阶段本身不得释放既有资源即使声明根作用域()
    {
        using var scope = new WorkflowVisionFrameScope();
        var previous = PublishNodeOutput(scope);

        // AR-01 阶段2：破坏性清理已移出 PrepareAsync。准备只做校验与绑定，
        // 释放上一轮资源是运行所有者的独立动作（IWorkflowRunResourceOwner）。
        await scope.PrepareAsync(Context(WorkflowRunScopeKind.Root), CancellationToken.None);

        using var lease = previous.Retain();
        Assert.Equal("f1", lease.FrameId);
    }

    [Fact]
    public async Task 运行所有者释放上一轮保留的帧()
    {
        using var scope = new WorkflowVisionFrameScope();
        var previous = PublishNodeOutput(scope);

        await scope.PrepareAsync(Context(WorkflowRunScopeKind.Root), CancellationToken.None);
        await scope.ReleasePreviousRunAsync(CancellationToken.None);

        // 清理语义不能被削弱：开新一轮仍要回收上一轮资源。
        Assert.Throws<ObjectDisposedException>(() => previous.Retain());
    }

    [Fact]
    public async Task 释放沿准备链转发给后续所有者()
    {
        var next = new RecordingOwner();
        using var scope = new WorkflowVisionFrameScope(next);

        await scope.ReleasePreviousRunAsync(CancellationToken.None);

        Assert.Equal(1, next.ReleaseCount);
    }

    [Fact]
    public async Task 嵌套准备仍然执行跨子文档节点ID重复校验()
    {
        using var scope = new WorkflowVisionFrameScope();
        var nodes = new IWorkflowNodeModel[]
        {
            new LoadVisionFileNodeModel { Id = "same" },
            new LoadVisionFileNodeModel { Id = "same" }
        };

        // 嵌套作用域只是"不清资源"，不是"跳过准备"：校验必须照常执行。
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.PrepareAsync(
                new WorkflowRunPreparationContext(nodes, WorkflowRunScopeKind.Nested),
                CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task 嵌套准备同样链式调用后续准备服务()
    {
        var next = new RecordingPreparation();
        using var scope = new WorkflowVisionFrameScope(next);

        await scope.PrepareAsync(Context(WorkflowRunScopeKind.Nested), CancellationToken.None);

        var recorded = Assert.Single(next.Contexts);
        Assert.Equal(WorkflowRunScopeKind.Nested, recorded.ScopeKind);
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

    /// <summary>同时是准备服务与资源所有者，用来验证释放会沿准备链转发。</summary>
    private sealed class RecordingOwner : IWorkflowRunPreparationService, IWorkflowRunResourceOwner
    {
        public int ReleaseCount;

        public ValueTask PrepareAsync(WorkflowRunPreparationContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleasePreviousRunAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseCount++;
            return ValueTask.CompletedTask;
        }
    }
}
