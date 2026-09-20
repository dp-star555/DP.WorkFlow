namespace DP.WorkFlow;

/// <summary>根据同一节点的另一个属性值控制参数是否在设计器中显示。</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class WorkflowPropertyVisibleWhenAttribute : Attribute
{
    /// <summary>创建条件显示规则；期望值按不区分大小写的文本比较。</summary>
    /// <param name="propertyName">同一节点模型中作为显示条件来源的公开属性名称。</param>
    /// <param name="expectedValues">来源属性文本满足其中任一值时显示被标记属性；空集合表示永不匹配。</param>
    public WorkflowPropertyVisibleWhenAttribute(string propertyName, params string[] expectedValues)
    {
        PropertyName = propertyName;
        ExpectedValues = expectedValues ?? Array.Empty<string>();
    }

    /// <summary>获取条件来源属性名称。</summary>
    public string PropertyName { get; }

    /// <summary>获取允许显示被标记属性的来源值集合。</summary>
    public IReadOnlyList<string> ExpectedValues { get; }
}
