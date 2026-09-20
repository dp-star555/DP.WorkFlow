namespace DP.WorkFlow.Tests;

public sealed class WorkflowNodeCatalogTests
{
    [Fact]
    public void Register_WhenNodeTypeIsDuplicated_ThrowsWithBothModelNames()
    {
        var catalog = new WorkflowNodeCatalog().Register<FirstNode>();

        var exception = Assert.Throws<InvalidOperationException>(() => catalog.Register<SecondNode>());

        Assert.Contains(typeof(FirstNode).FullName!, exception.Message);
        Assert.Contains(typeof(SecondNode).FullName!, exception.Message);
    }

    [Fact]
    public void CreateDescriptor_WhenAttributeAndNodeTypeDiffer_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => WorkflowNodeDescriptor.Create<InvalidKeyNode>());
    }

    [WorkflowNode("Shared")]
    private sealed class FirstNode : WorkflowNodeModel
    {
        public override string NodeType => "Shared";
    }

    [WorkflowNode("Shared")]
    private sealed class SecondNode : WorkflowNodeModel
    {
        public override string NodeType => "Shared";
    }

    [WorkflowNode("AttributeKey")]
    private sealed class InvalidKeyNode : WorkflowNodeModel
    {
        public override string NodeType => "RuntimeKey";
    }
}
