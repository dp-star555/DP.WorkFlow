using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowFailedPortVisibilityTests
{
    private static string[] Outputs(WorkflowDesignerSession session, string nodeId) =>
        session.GetPorts(nodeId, WorkflowPortDirection.Output).Select(p => p.Key).ToArray();

    [Fact]
    public void FailedPort_HiddenByDefault_ShownAfterEnabling_AndUndoable()
    {
        var session = new WorkflowDesignerSession(new WorkflowDocument(), new WorkflowNodeCatalog().RegisterImageNodes());
        var node = session.AddNode("Vision.FindLine", 0, 0);

        // 默认只显示“成功”，属性里仍能看到两个可启用的输出。
        Assert.Equal(new[] { WorkflowPorts.Success }, Outputs(session, node.Node.Id));
        Assert.Equal(2, session.GetDeclaredPorts(node.Node.Id, WorkflowPortDirection.Output).Count);

        Assert.True(session.SetOutputPortVisible(node.Node.Id, WorkflowPorts.Failed, true));
        Assert.Equal(new[] { WorkflowPorts.Success, WorkflowPorts.Failed }, Outputs(session, node.Node.Id));
        Assert.Contains(WorkflowCanvasNode.ShownPortMarker + WorkflowPorts.Failed, node.HiddenOutputPorts);

        Assert.True(session.Undo());
        Assert.Equal(new[] { WorkflowPorts.Success }, Outputs(session, node.Node.Id));
        Assert.True(session.Redo());
        Assert.True(session.SetOutputPortVisible(node.Node.Id, WorkflowPorts.Failed, false));
        Assert.Empty(node.HiddenOutputPorts);
    }

    [Fact]
    public void ConnectedFailedPort_StaysVisibleEvenWithoutEnabling()
    {
        // 已有失败连线的旧流程：即使没启用也显示，连线不悬空。
        var document = new WorkflowDocument();
        var canvas = document.CanvasProjection;
        var find = new FindVisionLineNodeModel { Id = "find" };
        var next = new FindVisionLineNodeModel { Id = "next" };
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = find });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = next });
        canvas.Connections.Add(new WorkflowConnectionModel { FromNodeId = "find", FromPort = WorkflowPorts.Failed, ToNodeId = "next", ToPort = WorkflowPorts.Input });
        var session = new WorkflowDesignerSession(document, new WorkflowNodeCatalog().RegisterImageNodes());

        Assert.Contains(WorkflowPorts.Failed, Outputs(session, "find"));
    }
}
