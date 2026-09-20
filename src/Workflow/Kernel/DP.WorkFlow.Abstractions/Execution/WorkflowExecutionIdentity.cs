namespace DP.WorkFlow;

/// <summary>
/// 唯一描述一次节点执行所在的运行、Token 和并行作用域。
/// </summary>
/// <param name="RunId">本次根工作流或子工作流运行的唯一标识。</param>
/// <param name="TokenId">沿一条顺序路径推进的当前执行 Token 标识。</param>
/// <param name="AncestorTokenIds">从当前 Token 向外追溯的祖先 Token，用于判断祖先输出可见性。</param>
/// <param name="ScopeIds">从外到内排列的运行时并行作用域 ID。</param>
/// <param name="NodeExecutionCount">当前节点在本次运行中的执行次数，从 1 开始，仅用于身份和监视。</param>
/// <param name="LoopIteration">循环节点当前执行令牌的候选迭代号；非循环节点为空。</param>
public sealed record WorkflowExecutionIdentity(
    Guid RunId,
    long TokenId,
    IReadOnlyList<long> AncestorTokenIds,
    IReadOnlyList<long> ScopeIds,
    int NodeExecutionCount,
    int? LoopIteration = null);
