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
    public void ShowingFailedPort_MovesAlignedWaypointsWithTheSuccessPort()
    {
        // 下边从一个端口变成两个，“成功”端口左移；原来竖直对齐它的拐点要跟过去，不能多出一个折。
        var session = new WorkflowDesignerSession(new WorkflowDocument(), new WorkflowNodeCatalog().RegisterImageNodes());
        var node = session.AddNode("Vision.FindLine", 0, 0);
        var next = session.AddNode("Vision.FindLine", 480, 480);
        var connection = session.Connect(node.Node.Id, WorkflowPorts.Success, next.Node.Id);
        double PortX()
        {
            var screen = WorkflowDesignerInteraction.FindPortPoint(session, node.Node.Id, WorkflowPorts.Success, WorkflowPortDirection.Output, connection.FromSide)!.Value;
            return WorkflowDesignerGeometry.ScreenToCanvas(session, screen.X, screen.Y).X;
        }
        var oldX = PortX();
        session.ReplaceConnectionWaypoints(connection, new[] { new WorkflowPoint(oldX, 240), new WorkflowPoint(oldX, 288), new WorkflowPoint(600, 288) });
        var snappedX = connection.Waypoints[0].X;

        Assert.True(session.SetOutputPortVisible(node.Node.Id, WorkflowPorts.Failed, true));

        var newX = PortX();
        Assert.NotEqual(oldX, newX);
        Assert.Equal(new[] { newX, newX, 600 }, connection.Waypoints.Select(point => point.X));
        Assert.True(session.Undo());
        Assert.Equal(new[] { snappedX, snappedX, 600 }, connection.Waypoints.Select(point => point.X));
    }

    [Fact]
    public void ConnectedFailedPort_StaysVisibleEvenWithoutEnabling()
    {
        // 已有失败连线的旧流程：即使没启用也显示，连线不悬空。
        var document = new WorkflowDocument();
        var canvas = document;
        var find = new FindVisionLineNodeModel { Id = "find" };
        var next = new FindVisionLineNodeModel { Id = "next" };
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = find });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = next });
        canvas.Connections.Add(new WorkflowConnectionModel { FromNodeId = "find", FromPort = WorkflowPorts.Failed, ToNodeId = "next", ToPort = WorkflowPorts.Input });
        var session = new WorkflowDesignerSession(document, new WorkflowNodeCatalog().RegisterImageNodes());

        Assert.Contains(WorkflowPorts.Failed, Outputs(session, "find"));
    }
}
