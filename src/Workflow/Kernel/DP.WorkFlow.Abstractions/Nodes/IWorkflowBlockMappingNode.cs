namespace DP.WorkFlow;

/// <summary>指定 Block 输入映射的来源。</summary>
public enum E_BlockInputSource
{
    Literal = 0,
    ParentVariable = 1,
    ParentNodeBinding = 2
}

/// <summary>将父作用域中的一个值写入子流程变量。</summary>
public sealed class BlockInputMapping
{
    public string TargetVariableName { get; set; } = string.Empty;
    public E_BlockInputSource Source { get; set; }
    public object? LiteralValue { get; set; }
    public string ParentVariableName { get; set; } = string.Empty;
    public WorkflowBindingKey? ParentBinding { get; set; }
}

/// <summary>指定 Block 输出映射的来源。</summary>
public enum E_BlockOutputSource
{
    ChildVariable = 0,
    ChildNodeBinding = 1
}

/// <summary>将子流程中的一个值显式写回父流程变量。</summary>
public sealed class BlockOutputMapping
{
    public string TargetVariableName { get; set; } = string.Empty;
    public E_BlockOutputSource Source { get; set; }
    public string ChildVariableName { get; set; } = string.Empty;
    public WorkflowBindingKey? ChildBinding { get; set; }
}
