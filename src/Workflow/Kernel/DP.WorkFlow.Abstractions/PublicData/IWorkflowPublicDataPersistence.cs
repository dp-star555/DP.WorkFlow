namespace DP.WorkFlow;

/// <summary>
/// 一项"保持"公共数据的保存内容（R2-d）：值按不变区域性写成文本，bool 信号和数值各一项，队列按顺序每个元素一项；
/// 数值尚未写入时为空列表。
/// </summary>
/// <param name="Key">键。</param>
/// <param name="Kind">形态。</param>
/// <param name="ValueType">值类型。</param>
/// <param name="Values">值文本。</param>
public sealed record WorkflowPersistedPublicData(
    string Key,
    WorkflowPublicDataKind Kind,
    E_SignalValueType ValueType,
    IReadOnlyList<string> Values);

/// <summary>
/// "保持"公共数据的存取（断电保存）。公共数据中心在登记保持项时读出上次保存的值，每次写入后保存当前值。
/// 实现需要线程安全；保存失败抛出的异常由中心吞掉并通过 <c>PersistenceFailed</c> 报告，不影响流程。
/// </summary>
public interface IWorkflowPublicDataPersistence
{
    /// <summary>读出全部已保存的项。</summary>
    IReadOnlyList<WorkflowPersistedPublicData> Load();

    /// <summary>保存一项的当前值（替换同键的旧值）。</summary>
    /// <param name="item">当前值。</param>
    void Save(WorkflowPersistedPublicData item);
}
