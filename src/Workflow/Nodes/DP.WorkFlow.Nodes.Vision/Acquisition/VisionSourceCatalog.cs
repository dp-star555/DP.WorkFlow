using System;
using System.Collections.Generic;
using System.Linq;
using DP.Vision.Acquisition;

namespace DP.WorkFlow;

/// <summary>
/// 宿主发布的逻辑源条目。只暴露Workflow侧做运行前检查与候选编辑所需的字段，
/// 不解释Provider私有配置、设备地址或打开方式；Provider失败时不会自动换源。
/// </summary>
public sealed record WorkflowVisionSourceInfo
{
    /// <summary>创建并校验源条目。</summary>
    /// <param name="sourceId">逻辑视觉源标识。</param>
    /// <param name="providerId">唯一允许服务该源的Provider稳定身份。</param>
    /// <param name="sharingPolicy">该源在物理资源上的并发协调策略。</param>
    /// <param name="isAvailable">Provider是否已就绪且该源当前可采集。</param>
    /// <param name="diagnostic">不可用原因；可用时为空。</param>
    /// <param name="acquisitionMode">
    /// 该源的采集时序；决定采集节点能否做节点级参数覆盖。
    /// 追加在末尾，使既有的五参位置调用行为不变。
    /// </param>
    /// <exception cref="ArgumentException">标识为空或仅包含空白字符。</exception>
    public WorkflowVisionSourceInfo(
        string sourceId,
        string providerId,
        EVisionSourceSharingPolicy sharingPolicy,
        bool isAvailable = true,
        string? diagnostic = null,
        EVisionAcquisitionMode acquisitionMode = EVisionAcquisitionMode.OnDemand)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new ArgumentException("逻辑源标识不能为空。", nameof(sourceId));
        if (string.IsNullOrWhiteSpace(providerId))
            throw new ArgumentException("Provider身份不能为空。", nameof(providerId));
        SourceId = sourceId.Trim();
        ProviderId = providerId.Trim();
        SharingPolicy = sharingPolicy;
        AcquisitionMode = acquisitionMode;
        IsAvailable = isAvailable;
        Diagnostic = string.IsNullOrWhiteSpace(diagnostic) ? null : diagnostic.Trim();
    }

    /// <summary>逻辑视觉源标识。</summary>
    public string SourceId { get; }

    /// <summary>唯一允许服务该源的Provider稳定身份。</summary>
    public string ProviderId { get; }

    /// <summary>该源在物理资源上的并发协调策略。</summary>
    public EVisionSourceSharingPolicy SharingPolicy { get; }

    /// <summary>
    /// 该源的采集时序：主动采集（节点到达后采集）或外部回调缓冲（相机长期布防，节点稍后领取）。
    /// 缓冲源需要根运行作用域所有权，且不接受节点级曝光/增益覆盖。
    /// </summary>
    public EVisionAcquisitionMode AcquisitionMode { get; }

    /// <summary>Provider是否已就绪且该源当前可采集。</summary>
    public bool IsAvailable { get; }

    /// <summary>不可用原因；可用时为空。</summary>
    public string? Diagnostic { get; }

    /// <inheritdoc/>
    public override string ToString() => IsAvailable ? SourceId : $"{SourceId}（不可用）";
}

/// <summary>
/// 已发布逻辑源的只读目录；由宿主装配，供运行准备校验和属性编辑器候选使用。
/// 工作流文档只保存SourceId，本目录负责把它映射到当前机器上的Provider。
/// </summary>
public interface IWorkflowVisionSourceCatalog
{
    /// <summary>全部已发布的逻辑源，按SourceId排序。</summary>
    IReadOnlyList<WorkflowVisionSourceInfo> Sources { get; }

    /// <summary>按逻辑源标识查找。</summary>
    /// <param name="sourceId">逻辑源标识。</param>
    /// <param name="source">找到的源条目。</param>
    /// <returns>已发布时返回 <see langword="true"/>。</returns>
    bool TryGet(string sourceId, out WorkflowVisionSourceInfo? source);
}

/// <summary>不可变逻辑源目录；发布后不再接受变更。</summary>
public sealed class WorkflowVisionSourceCatalog : IWorkflowVisionSourceCatalog
{
    private readonly Dictionary<string, WorkflowVisionSourceInfo> _sources;

    /// <summary>创建目录。</summary>
    /// <param name="sources">已发布的源条目；同一SourceId重复会明确失败。</param>
    /// <exception cref="ArgumentNullException">集合为空。</exception>
    /// <exception cref="ArgumentException">存在空条目或重复SourceId。</exception>
    public WorkflowVisionSourceCatalog(IEnumerable<WorkflowVisionSourceInfo> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = new Dictionary<string, WorkflowVisionSourceInfo>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (source is null) throw new ArgumentException("源条目不能为空。", nameof(sources));
            if (!_sources.TryAdd(source.SourceId, source))
                throw new ArgumentException($"逻辑源 {source.SourceId} 重复发布。", nameof(sources));
        }
    }

    /// <summary>
    /// 从采集侧一次发布的Composition直接投影Workflow源目录（V2-2需求4）。
    /// 条目字段来自公共层可解释的机器配置部分与Plugin解析后的验证结果；
    /// 未安装Type的Source被保真保留并标记不可用，由宿主诊断与显示。
    /// </summary>
    /// <param name="acquisition">已发布的不可变Composition。</param>
    /// <exception cref="ArgumentNullException">组合为空。</exception>
    /// <exception cref="ArgumentException">组合投影含重复SourceId。</exception>
    public static WorkflowVisionSourceCatalog FromAcquisition(IVisionAcquisitionSourceCatalog acquisition)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        return new WorkflowVisionSourceCatalog(
            acquisition.SourceCatalog.Select(entry => new WorkflowVisionSourceInfo(
                entry.SourceId,
                entry.ProviderId,
                entry.SharingPolicy,
                entry.IsAvailable,
                entry.Diagnostic,
                entry.AcquisitionMode)));
    }

    /// <inheritdoc/>
    public IReadOnlyList<WorkflowVisionSourceInfo> Sources =>
        _sources.Values.OrderBy(source => source.SourceId, StringComparer.Ordinal).ToArray();

    /// <inheritdoc/>
    public bool TryGet(string sourceId, out WorkflowVisionSourceInfo? source)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            source = null;
            return false;
        }

        return _sources.TryGetValue(sourceId.Trim(), out source);
    }
}
