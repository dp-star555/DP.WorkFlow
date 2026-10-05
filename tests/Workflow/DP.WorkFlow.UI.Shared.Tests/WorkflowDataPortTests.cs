using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowDataPortTests
{
    [Fact]
    public void ExposingMember_IsUndoableAndGrowsNodeWithDataBand()
    {
        var (session, _, compare, _) = CreateChain();
        var height = compare.Height;

        Assert.True(session.SetOutputMemberExposed(compare.Node.Id, nameof(CompareNodeResult.Value), true));
        Assert.Contains(nameof(CompareNodeResult.Value), compare.ExposedOutputMembers);
        Assert.True(compare.Height > height);
        var port = Assert.Single(WorkflowDesignerGeometry.GetDataPortPoints(session, compare));
        var hit = WorkflowDesignerInteraction.HitDataPort(session, port.Point.X, port.Point.Y);
        Assert.NotNull(hit);
        Assert.Equal(nameof(CompareNodeResult.Value), hit.Value.Member.Name);

        Assert.True(session.Undo());
        Assert.Empty(compare.ExposedOutputMembers);
        Assert.Equal(height, compare.Height, 6);
    }

    [Fact]
    public void DraggingDataPortToDownstreamNode_BindsCompatibleParameterAndDrawsLink()
    {
        var (session, start, compare, decision) = CreateChain();
        session.SetOutputMemberExposed(compare.Node.Id, nameof(CompareNodeResult.Value), true);

        var target = Assert.Single(session.GetDataPortTargets(compare.Node.Id, nameof(CompareNodeResult.Value), decision.Node.Id));
        Assert.Equal(nameof(DecisionNodeModel.Condition), target.PropertyName);
        // 上游节点不能绑定下游节点的输出，自身也不是目标。
        Assert.Empty(session.GetDataPortTargets(compare.Node.Id, nameof(CompareNodeResult.Value), start.Node.Id));
        Assert.Empty(session.GetDataPortTargets(compare.Node.Id, nameof(CompareNodeResult.Value), compare.Node.Id));

        session.BindDataPort(compare.Node.Id, nameof(CompareNodeResult.Value), target);

        var condition = ((DecisionNodeModel)decision.Node).Condition;
        Assert.Equal(WorkflowValueSource.Binding, condition.Source);
        Assert.Equal(new WorkflowBindingKey(compare.Node.Id, nameof(CompareNodeResult.Value)), condition.Binding);
        var link = Assert.Single(session.GetDataLinks());
        Assert.Equal((compare.Node.Id, nameof(CompareNodeResult.Value), decision.Node.Id), (link.SourceNodeId, link.Member, link.ConsumerNodeId));

        Assert.True(session.Undo());
        Assert.Equal(WorkflowValueSource.Literal, ((DecisionNodeModel)decision.Node).Condition.Source);
        Assert.Empty(session.GetDataLinks());
    }

    private static (WorkflowDesignerSession Session, WorkflowCanvasNode Start, WorkflowCanvasNode Compare, WorkflowCanvasNode Decision) CreateChain()
    {
        var session = new WorkflowDesignerSession(new WorkflowDocument { Name = "DataPorts" }, new WorkflowNodeCatalog().RegisterStandardNodes());
        var start = session.AddNode("Start", 0, 0);
        var compare = session.AddNode("StringCompare", 0, 200);
        var decision = session.AddNode("Decision", 0, 400);
        session.Connect(start.Node.Id, WorkflowPorts.Success, compare.Node.Id);
        session.Connect(compare.Node.Id, WorkflowPorts.True, decision.Node.Id);
        return (session, start, compare, decision);
    }
}
