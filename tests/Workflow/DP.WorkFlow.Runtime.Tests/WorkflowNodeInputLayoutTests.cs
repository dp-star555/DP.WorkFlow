namespace DP.WorkFlow.Tests;

/// <summary>
/// 实施细节 §12.1：输入槽发现的稳定性、歧义拒绝和子计划递归。
/// 这些用例保护"普通 ResolveInput 不手写输入名"所依赖的元数据前提。
/// </summary>
public sealed class WorkflowNodeInputLayoutTests
{
    [Fact]
    public void Discover_FindsTopLevelInputSlotsInStableOrder()
    {
        var layout = WorkflowNodeInputLayout.Discover(typeof(MultiInputNode));

        // 顺序必须与属性名 Ordinal 排序一致，否则诊断和测试会随反射顺序漂移。
        Assert.Equal(new[] { "Alpha", "Mike", "Zulu" }, layout.Slots.Select(slot => slot.Key));
        Assert.Equal(typeof(int), layout.Slots[0].ValueType);
    }

    [Fact]
    public void Discover_IncludesInheritedPublicInputSlots()
    {
        var layout = WorkflowNodeInputLayout.Discover(typeof(DerivedInputNode));

        Assert.Equal(new[] { "BaseValue", "OwnValue" }, layout.Slots.Select(slot => slot.Key));
    }

    [Fact]
    public void Discover_IgnoresNonInputPropertiesIndexersNestedObjectsAndCollections()
    {
        var layout = WorkflowNodeInputLayout.Discover(typeof(NoisyNode));

        // 只认顶层 WorkflowInput<T> 属性：普通属性、索引器、嵌套配置对象和集合元素都不算输入槽。
        Assert.Equal(new[] { "Value" }, layout.Slots.Select(slot => slot.Key));
    }

    [Fact]
    public void Discover_WhenInheritanceHidesSameKey_IsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => WorkflowNodeInputLayout.Discover(typeof(HidingInputNode)));

        Assert.Contains("Value", exception.Message);
    }

    [Fact]
    public void Bind_DistinguishesEqualButDistinctInputInstances()
    {
        var node = new TwoInputNode
        {
            Id = "N",
            // 两个输入内容完全相同：只有引用相等才能把它们分回各自的输入槽。
            Left = WorkflowInput<int>.FromLiteral(7),
            Right = WorkflowInput<int>.FromLiteral(7)
        };

        var map = WorkflowNodeInputLayout.Discover(typeof(TwoInputNode)).Bind(node);

        Assert.True(map.TryGetKey(node.Left, out var leftKey));
        Assert.True(map.TryGetKey(node.Right, out var rightKey));
        Assert.Equal("Left", leftKey);
        Assert.Equal("Right", rightKey);
    }

    [Fact]
    public void Bind_ReturnsFalseForUnknownInputInstance()
    {
        var node = new TwoInputNode { Id = "N" };
        var map = WorkflowNodeInputLayout.Discover(typeof(TwoInputNode)).Bind(node);

        Assert.False(map.TryGetKey(WorkflowInput<int>.FromLiteral(1), out _));
    }

    [Fact]
    public void Bind_WhenOneInstanceIsSharedByTwoSlots_FailsWithNodeIdentity()
    {
        var shared = WorkflowInput<int>.FromLiteral(5);
        var node = new TwoInputNode { Id = "N", Title = "共享实例", Left = shared, Right = shared };

        var exception = Assert.Throws<InvalidOperationException>(
            () => WorkflowNodeInputLayout.Discover(typeof(TwoInputNode)).Bind(node));

        // 错误信息必须能定位到节点、节点类型和冲突属性名。
        Assert.Contains("N", exception.Message);
        Assert.Contains("Test.TwoInput", exception.Message);
        Assert.Contains("Left", exception.Message);
        Assert.Contains("Right", exception.Message);
    }

    [Fact]
    public void Bind_WhenInputGetterThrows_FailsWithNodeIdentity()
    {
        var node = new ThrowingInputNode { Id = "N" };

        var exception = Assert.Throws<InvalidOperationException>(
            () => WorkflowNodeInputLayout.Discover(typeof(ThrowingInputNode)).Bind(node));

        Assert.Contains("N", exception.Message);
        Assert.Contains("Broken", exception.Message);
    }

    [Fact]
    public void Binder_RecursivelyBuildsChildPlanInputLayouts()
    {
        var inner = new TwoInputNode { Id = "Inner" };
        var childDocument = new WorkflowDocument { Name = "子流程", EntryNodeId = "Inner" };
        childDocument.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = inner });
        var composite = new TestSubDocumentNode { Id = "Composite", Title = "复合", SubDocument = childDocument };
        var outerDocument = new WorkflowDocument { Name = "外层", EntryNodeId = "Composite" };
        outerDocument.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = composite });

        var plan = new WorkflowCompiler().Compile(outerDocument);
        var bound = new WorkflowRuntimeBinder(new WorkflowNodeHandlerCatalog().Register(new AcceptAllHandler())).Bind(plan);

        Assert.Empty(bound.GetInputLayout("Composite").Slots);
        // 子计划必须拥有自己的布局，否则子流程内的普通 ResolveInput 会退化为 Unresolved。
        Assert.Equal(
            new[] { "Left", "Right" },
            bound.GetChildPlan("Composite").GetInputLayout("Inner").Slots.Select(slot => slot.Key));
    }

    private sealed class MultiInputNode : WorkflowNodeModel
    {
        public override string NodeType => "Test.MultiInput";

        public WorkflowInput<int> Zulu { get; set; } = WorkflowInput<int>.FromLiteral(0);

        public WorkflowInput<int> Alpha { get; set; } = WorkflowInput<int>.FromLiteral(0);

        public WorkflowInput<int> Mike { get; set; } = WorkflowInput<int>.FromLiteral(0);
    }

    private sealed class TwoInputNode : WorkflowNodeModel
    {
        public override string NodeType => "Test.TwoInput";

        public WorkflowInput<int> Left { get; set; } = WorkflowInput<int>.FromLiteral(0);

        public WorkflowInput<int> Right { get; set; } = WorkflowInput<int>.FromLiteral(0);
    }

    private class BaseInputNode : WorkflowNodeModel
    {
        public override string NodeType => "Test.BaseInput";

        public WorkflowInput<string> BaseValue { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    }

    private sealed class DerivedInputNode : BaseInputNode
    {
        public WorkflowInput<int> OwnValue { get; set; } = WorkflowInput<int>.FromLiteral(0);
    }

    private class HidingBaseNode : WorkflowNodeModel
    {
        public override string NodeType => "Test.HidingBase";

        public WorkflowInput<int> Value { get; set; } = WorkflowInput<int>.FromLiteral(0);
    }

    private sealed class HidingInputNode : HidingBaseNode
    {
        public new WorkflowInput<int> Value { get; set; } = WorkflowInput<int>.FromLiteral(0);
    }

    private sealed class ThrowingInputNode : WorkflowNodeModel
    {
        public override string NodeType => "Test.ThrowingInput";

        public WorkflowInput<int> Value { get; set; } = WorkflowInput<int>.FromLiteral(0);

        public WorkflowInput<int> Broken => throw new InvalidOperationException("配置损坏。");
    }

    private sealed class NestedConfiguration
    {
        public WorkflowInput<int> Hidden { get; set; } = WorkflowInput<int>.FromLiteral(0);
    }

    private sealed class NoisyNode : WorkflowNodeModel
    {
        public override string NodeType => "Test.Noisy";

        public WorkflowInput<int> Value { get; set; } = WorkflowInput<int>.FromLiteral(0);

        public string Plain { get; set; } = string.Empty;

        public NestedConfiguration Nested { get; set; } = new();

        public List<WorkflowInput<int>> Collection { get; set; } = new();

        public WorkflowInput<int> this[int index] => Collection[index];
    }

    private sealed class TestSubDocumentNode : WorkflowNodeModel, IWorkflowSubDocumentNode
    {
        public override string NodeType => "Test.SubDocument";

        public WorkflowDocument SubDocument { get; set; } = new();
    }

    private sealed class AcceptAllHandler : IWorkflowNodeHandler
    {
        public bool CanHandle(IWorkflowNodeModel node) => true;

        public ValueTask<NodeExecutionResult> ExecuteAsync(
            IWorkflowNodeModel node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(NodeExecutionResult.Continue(WorkflowPorts.Success));
    }
}
