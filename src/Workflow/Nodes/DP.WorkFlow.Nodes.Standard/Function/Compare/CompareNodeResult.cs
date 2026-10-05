using System.ComponentModel;

namespace DP.WorkFlow;

/// <summary>表示比较节点提交的标准输出。</summary>
public sealed record CompareNodeResult(
    [property: DisplayName("比较结果")] bool Value,
    [property: DisplayName("左值")] string? LeftText,
    [property: DisplayName("右值")] string? RightText,
    [property: DisplayName("运算符")] string OperatorText,
    [property: DisplayName("比较类型")] string CompareTypeText);
