using DP.Vision.Acquisition;

namespace DP.WorkFlow;

/// <summary>
/// 把采集运行时的根运行所有权接到Workflow的中立运行作用域契约上。
/// <para>
/// Kernel不得引用DP.Vision，因此两侧各有一个"根运行作用域所有者"：Kernel只认识
/// <see cref="IWorkflowRunScopeOwner"/>，采集侧只认识 <see cref="IVisionAcquisitionRunOwner"/>，
/// 本类型是唯一的桥。
/// </para>
/// <para>
/// 注册方式与 <see cref="IVisionAcquisition"/> 相同：宿主把它注册进服务容器，
/// 嵌套调用点不解析 <see cref="IWorkflowRunScopeOwner"/>，因此结构上无法重新取得所有权。
/// </para>
/// </summary>
public sealed class VisionAcquisitionRunScope : IWorkflowRunScopeOwner
{
    private readonly IVisionAcquisitionRunOwner _owner;

    /// <summary>创建桥接。</summary>
    /// <param name="owner">采集运行时的根运行所有权入口。</param>
    /// <exception cref="ArgumentNullException">入口为空。</exception>
    public VisionAcquisitionRunScope(IVisionAcquisitionRunOwner owner) =>
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    /// <inheritdoc/>
    public async ValueTask<IWorkflowRunScopeLease> BeginRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var lease = await _owner.BeginRunAsync(runId.ToString("N"), cancellationToken).ConfigureAwait(false);
        return new Lease(runId, lease);
    }

    private sealed class Lease : IWorkflowRunScopeLease
    {
        private readonly IVisionAcquisitionRunLease _lease;

        public Lease(Guid runId, IVisionAcquisitionRunLease lease)
        {
            RunId = runId;
            _lease = lease;
        }

        public Guid RunId { get; }

        public IReadOnlyList<string> OwnedResourceIds => _lease.ArmedSourceIds;

        /// <summary>本轮采集代次；用于诊断与运行制品。</summary>
        public int Epoch => _lease.Epoch;

        public ValueTask DisposeAsync() => _lease.DisposeAsync();
    }
}
