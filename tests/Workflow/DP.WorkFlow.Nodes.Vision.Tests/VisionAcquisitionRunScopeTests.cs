using DP.Vision.Acquisition;

namespace DP.WorkFlow.Tests;

/// <summary>
/// Kernel 与采集侧之间唯一的桥：<see cref="IWorkflowRunScopeOwner"/> ↔ <see cref="IVisionAcquisitionRunOwner"/>。
/// <para>
/// 桥本身没有业务逻辑，但它决定了"哪一根运行的帧"这一界定能否被两侧对上，
/// 因此身份转换、所有权透传与释放转发都必须逐条锁住，不能只靠端到端用例顺带覆盖。
/// </para>
/// </summary>
public sealed class VisionAcquisitionRunScopeTests
{
    [Fact]
    public void 采集侧入口为空时构造明确失败()
    {
        var failure = Assert.Throws<ArgumentNullException>(() => new VisionAcquisitionRunScope(null!));

        Assert.Equal("owner", failure.ParamName);
    }

    [Fact]
    public async Task 运行身份转成无连字符的采集侧身份()
    {
        var owner = new RecordingRunOwner();
        var scope = new VisionAcquisitionRunScope(owner);
        var runId = Guid.NewGuid();

        await using var lease = await scope.BeginRunAsync(runId, CancellationToken.None);

        // 采集侧只认识字符串身份；格式不一致会让运行制品与冲突报告对不上同一根运行。
        Assert.Equal(runId.ToString("N"), owner.LastRunId);
        Assert.Equal(runId, lease.RunId);
    }

    [Fact]
    public async Task 已取得所有权的资源标识原样透传()
    {
        var owner = new RecordingRunOwner { ArmedSourceIds = new[] { "Camera.Top", "Camera.Side" } };
        var scope = new VisionAcquisitionRunScope(owner);

        await using var lease = await scope.BeginRunAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(new[] { "Camera.Top", "Camera.Side" }, lease.OwnedResourceIds);
    }

    [Fact]
    public async Task 释放桥接租约转成采集侧租约释放()
    {
        var owner = new RecordingRunOwner();
        var scope = new VisionAcquisitionRunScope(owner);

        var lease = await scope.BeginRunAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(0, owner.InnerDisposeCount);

        await lease.DisposeAsync();

        Assert.Equal(1, owner.InnerDisposeCount);
    }

    [Fact]
    public async Task 采集侧取得失败时原样抛出且不产生租约()
    {
        // 例如采集运行时已经有另一根运行持有同一资源：冲突必须原样上抛给宿主，
        // 由宿主在首节点之前失败，而不是被包装成含义不明的异常。
        var conflict = new VisionResourceConflictException(new VisionResourceConflictDiagnostics(
            "Camera.Top", "camera:serial:TOP", "run-2", "run-2", "run-1", "run-1",
            EVisionSourceSharingPolicy.ExclusiveRun, "已有根运行持有所有权。"));
        var owner = new RecordingRunOwner { BeginFailure = conflict };
        var scope = new VisionAcquisitionRunScope(owner);

        var failure = await Assert.ThrowsAsync<VisionResourceConflictException>(
            async () => await scope.BeginRunAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Same(conflict, failure);
        Assert.Contains("run-1", failure.Message);
        Assert.Equal(0, owner.InnerDisposeCount);
    }

    /// <summary>可编程的采集侧根运行入口；只记录桥转发了什么，不模拟采集语义。</summary>
    private sealed class RecordingRunOwner : IVisionAcquisitionRunOwner
    {
        public string? LastRunId { get; private set; }

        public IReadOnlyList<string> ArmedSourceIds { get; set; } = Array.Empty<string>();

        public int Epoch { get; set; }

        public int InnerDisposeCount { get; private set; }

        /// <summary>取得时抛出的异常；用于验证桥不吞异常、也不包装。</summary>
        public Exception? BeginFailure { get; set; }

        public ValueTask<IVisionAcquisitionRunLease> BeginRunAsync(string runId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (BeginFailure is not null)
                throw BeginFailure;
            LastRunId = runId;
            return ValueTask.FromResult<IVisionAcquisitionRunLease>(new Lease(this, runId, Epoch, ArmedSourceIds));
        }

        private sealed class Lease : IVisionAcquisitionRunLease
        {
            private readonly RecordingRunOwner _owner;

            public Lease(RecordingRunOwner owner, string runId, int epoch, IReadOnlyList<string> armedSourceIds)
            {
                _owner = owner;
                RunId = runId;
                Epoch = epoch;
                ArmedSourceIds = armedSourceIds;
            }

            public string RunId { get; }

            public int Epoch { get; }

            public IReadOnlyList<string> ArmedSourceIds { get; }

            public ValueTask DisposeAsync()
            {
                _owner.InnerDisposeCount++;
                return default;
            }
        }
    }
}
