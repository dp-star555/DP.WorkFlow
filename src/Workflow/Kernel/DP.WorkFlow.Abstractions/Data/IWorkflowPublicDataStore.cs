namespace DP.WorkFlow;

/// <summary>描述一次成功节点要原子提交到公共数据仓的写入和删除集合。</summary>
public sealed class WorkflowPublicDataChangeSet
{
    /// <summary>创建不可变公共数据变更快照。</summary>
    /// <param name="writes">按稳定键索引的非空值。</param>
    /// <param name="removals">需要删除的稳定键。</param>
    public WorkflowPublicDataChangeSet(
        IReadOnlyDictionary<string, object> writes,
        IReadOnlyCollection<string> removals)
    {
        ArgumentNullException.ThrowIfNull(writes);
        ArgumentNullException.ThrowIfNull(removals);
        Writes = new Dictionary<string, object>(writes, StringComparer.Ordinal);
        Removals = removals.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>获取待发布值快照。</summary>
    public IReadOnlyDictionary<string, object> Writes { get; }

    /// <summary>获取待删除键快照。</summary>
    public IReadOnlyCollection<string> Removals { get; }
}

/// <summary>
/// 保存流程显式公开的数据。它独立于节点输出和流程局部变量；
/// 写入只通过成功节点的暂存提交或宿主显式初始化发生。
/// </summary>
public interface IWorkflowPublicDataStore
{
    /// <summary>尝试直接按运行时类型读取公开值。</summary>
    bool TryGet<T>(string key, out T? value);

    /// <summary>原子应用一次公开数据变更集合。</summary>
    void Apply(WorkflowPublicDataChangeSet changes);

    /// <summary>获取当前公开数据结构快照；值对象本身不执行深复制。</summary>
    IReadOnlyDictionary<string, object> Snapshot();
}
