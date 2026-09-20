namespace DP.WorkFlow;

/// <summary>表示比较节点提交的标准输出。</summary>
public sealed record CompareNodeResult(
    bool Value,
    string? LeftText,
    string? RightText,
    string OperatorText,
    string CompareTypeText);
