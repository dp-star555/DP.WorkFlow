using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowDesignerSessionTests
{
    [Fact]
    public void AddAndMoveNode_SnapToVisibleGrid()
    {
        var session = new WorkflowDesignerSession(
            new WorkflowDocument(),
            new WorkflowNodeCatalog().RegisterStandardNodes());

        var node = session.AddNode("Delay", 37, 61);
        session.MoveNode(node.Node.Id, 103, 107);

        Assert.Equal(0, (node.X + node.Width / 2) % 24, 6);
        Assert.Equal(0, (node.Y + node.Height / 2) % 24, 6);
    }

    [Fact]
    public void SessionConstruction_DoesNotNormalizeLayoutUntilExplicitCommand()
    {
        var canvasDocument = new WorkflowDocument();
        var canvas = canvasDocument.CanvasProjection;
        var node = new DelayNodeModel { Id = "Delay", Title = "等待" };
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node, Height = 140 });

        var session = new WorkflowDesignerSession(canvasDocument, new WorkflowNodeCatalog().RegisterStandardNodes());

        Assert.Equal(140, canvas.Nodes[0].Height);
        Assert.True(session.NormalizeLayout());
        Assert.Equal(WorkflowDesignerGeometry.MinimumNodeHeight, canvas.Nodes[0].Height);
        Assert.True(session.Undo());
        Assert.Equal(140, canvas.Nodes[0].Height);
    }

    [Fact]
    public void LongNodeTitle_ExpandsNodeInsteadOfEllipsisLayout()
    {
        var model = new ActionNodeModel { Id = "Action", Title = "这是一个很长很长的节点名称" };
        var canvasDocument = new WorkflowDocument();
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = model, Width = 180 });

        var session = new WorkflowDesignerSession(canvasDocument, new WorkflowNodeCatalog().RegisterStandardNodes());

        Assert.Equal(180, canvas.Nodes[0].Width);
        Assert.True(session.NormalizeLayout());
        Assert.True(canvas.Nodes[0].Width > 260);
    }

    [Fact]
    public void NodeHeaderLayout_ReservesRuntimeRegionAndUsesCompactTitleFont()
    {
        var layout = WorkflowDesignerGeometry.CalculateNodeHeaderLayout(
            new WorkflowDesignerRect(100, 50, 180, 64),
            zoom: 1,
            measuredRuntimeWidth: 96);

        Assert.True(layout.TitleBounds.X + layout.TitleBounds.Width <= layout.RuntimeBounds.X);
        Assert.True(layout.RuntimeBounds.Width <= (180 - 32) * 0.45 + 0.01);
        Assert.Equal(9, layout.TitleFontSize);
        Assert.Equal(7.5, layout.RuntimeFontSize);
    }

    [Fact]
    public void NodeTitleBecomesShorter_ShrinksWidthAndKeepsCenter()
    {
        var model = new ActionNodeModel { Id = "Action", Title = "短名称" };
        var canvasNode = new WorkflowCanvasNode { Node = model, X = 48, Width = 180 };
        var canvasDocument = new WorkflowDocument();
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(canvasNode);
        var session = new WorkflowDesignerSession(canvasDocument, new WorkflowNodeCatalog().RegisterStandardNodes());
        session.NormalizeLayout();
        var shortWidth = canvasNode.Width;
        var center = canvasNode.X + canvasNode.Width / 2;

        model.Title = "这是一个非常非常长的节点名称用于测试动态尺寸";
        session.NotifyNodeConfigurationChanged();
        var longWidth = canvasNode.Width;
        model.Title = "短名称";
        session.NotifyNodeConfigurationChanged();

        Assert.True(longWidth > shortWidth);
        Assert.Equal(shortWidth, canvasNode.Width);
        Assert.Equal(center, canvasNode.X + canvasNode.Width / 2);
    }

    [Fact]
    public void MovePortSide_UpdatesConnectionsUsingTheOldSide()
    {
        var session = CreateSession();
        var start = session.AddNode("Start", 0, 0);
        var end = session.AddNode("End", 300, 0);
        var connection = session.Connect(
            start.Node.Id,
            WorkflowPorts.Success,
            end.Node.Id,
            fromSide: WorkflowPortSide.Bottom,
            toSide: WorkflowPortSide.Top);

        session.SetPortSide(start.Node.Id, WorkflowPortDirection.Output, WorkflowPorts.Success, WorkflowPortSide.Right);

        Assert.Equal(WorkflowPortSide.Right, connection.FromSide);
        Assert.True(session.Undo());
        Assert.Equal(WorkflowPortSide.Bottom, connection.FromSide);
    }

    [Fact]
    public void CopyAndPasteSelection_ClonesNodesAndInternalConnectionsAsSingleUndo()
    {
        var session = CreateSession();
        var start = session.AddNode("Start", 0, 0);
        var end = session.AddNode("End", 300, 0);
        session.Connect(start.Node.Id, WorkflowPorts.Success, end.Node.Id);
        session.SelectNodes(new[] { start.Node.Id, end.Node.Id });

        Assert.True(session.CopySelection());
        var pasted = session.PasteSelection();

        Assert.Equal(2, pasted.Count);
        Assert.Equal(4, session.Canvas.Nodes.Count);
        Assert.Equal(2, session.Canvas.Connections.Count);
        Assert.DoesNotContain(pasted, item => item.Node.Id is "Start1" or "End1");
        Assert.True(session.Undo());
        Assert.Equal(2, session.Canvas.Nodes.Count);
        Assert.Single(session.Canvas.Connections);
    }

    [Fact]
    public void CopyAndPasteScriptNode_GeneratesIndependentScriptIdentity()
    {
        var session = CreateSession();
        var originalCanvasNode = session.AddNode("CSharpScript", 0, 0);
        var original = Assert.IsType<CSharpScriptNodeModel>(originalCanvasNode.Node);
        session.SelectedNodeId = original.Id;

        Assert.True(session.CopySelection());
        var pasted = Assert.Single(session.PasteSelection());
        var copy = Assert.IsType<CSharpScriptNodeModel>(pasted.Node);

        Assert.NotEqual(original.ScriptId, copy.ScriptId);
        Assert.Equal(original.Script, copy.Script);
    }

    [Fact]
    public void MultiOutputVisibility_IsLayoutOnlyAndPreservesSemanticConnections()
    {
        var session = CreateSession();
        var decision = session.AddNode("Decision", 0, 0);
        var end = session.AddNode("End", 300, 0);
        session.Connect(decision.Node.Id, "False", end.Node.Id);

        Assert.True(session.SetOutputPortVisible(decision.Node.Id, "False", false));
        Assert.DoesNotContain(session.GetPorts(decision.Node.Id, WorkflowPortDirection.Output), port => port.Key == "False");
        Assert.Single(session.Canvas.Connections);
        Assert.Equal(WorkflowConnectionState.Active, session.Canvas.Connections[0].State);

        Assert.True(session.Undo());
        Assert.Contains(session.GetPorts(decision.Node.Id, WorkflowPortDirection.Output), port => port.Key == "False");
        Assert.Single(session.Canvas.Connections);
    }

    [Fact]
    public void SingleSidePort_IsCenteredOnTheWholeNodeEdge()
    {
        var session = CreateSession();
        var node = session.AddNode("End", 100, 100);
        var ports = session.GetPorts(node.Node.Id, WorkflowPortDirection.Input);
        var point = WorkflowDesignerGeometry.GetPortScreenPoint(session, node, ports[0], ports, WorkflowPortSide.Left);
        var bounds = WorkflowDesignerGeometry.GetNodeScreenRect(session, node);

        Assert.Equal(bounds.Y + bounds.Height / 2, point.Y, 6);
    }

    [Fact]
    public void MultipleOutputPortTracks_SnapTheirRoutingAxisToGrid()
    {
        var session = CreateSession();
        var node = session.AddNode("Decision", 100, 100);
        var ports = session.GetPorts(node.Node.Id, WorkflowPortDirection.Output);

        foreach (var port in ports)
        {
            var point = WorkflowDesignerGeometry.GetPortScreenPoint(session, node, port, ports);
            var canvasPoint = WorkflowDesignerGeometry.ScreenToCanvas(session, point.X, point.Y);
            if (node.GetPortSide(port) is WorkflowPortSide.Left or WorkflowPortSide.Right)
                Assert.Equal(0, canvasPoint.Y % 24, 6);
            else
                Assert.Equal(0, canvasPoint.X % 24, 6);
        }
    }

    [Fact]
    public void ConnectionLabelPosition_IsUndoableAndClampedToPath()
    {
        var session = CreateSession();
        var start = session.AddNode("Start", 0, 0);
        var end = session.AddNode("End", 300, 0);
        var connection = session.Connect(start.Node.Id, WorkflowPorts.Success, end.Node.Id);

        Assert.True(session.SetConnectionLabelPosition(connection, 0.8));
        Assert.Equal(0.8, connection.LabelPosition);
        Assert.True(session.Undo());
        Assert.Equal(0.5, connection.LabelPosition);
    }

    [Fact]
    public void MultipleIncomingConnections_CanUseDifferentTargetSides()
    {
        var session = CreateSession();
        var first = session.AddNode("Action", 0, 0);
        var second = session.AddNode("Action", 0, 200);
        var target = session.AddNode("End", 400, 100);

        var top = session.Connect(
            first.Node.Id,
            WorkflowPorts.Success,
            target.Node.Id,
            WorkflowPorts.Input,
            toSide: WorkflowPortSide.Top);
        var left = session.Connect(
            second.Node.Id,
            WorkflowPorts.Success,
            target.Node.Id,
            WorkflowPorts.Input,
            toSide: WorkflowPortSide.Left);

        Assert.Equal(WorkflowPortSide.Top, top.ToSide);
        Assert.Equal(WorkflowPortSide.Left, left.ToSide);
        Assert.Equal(2, session.Canvas.Connections.Count);
    }

    [Fact]
    public void AddNodeOnConnection_ReplacesConnectionAsSingleUndoOperation()
    {
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        var canvasDocument = new WorkflowDocument();
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new StartNodeModel { Id = "Start", Title = "开始" } });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new EndNodeModel { Id = "End", Title = "结束" }, X = 300 });
        var connection = new WorkflowConnectionModel
        {
            FromNodeId = "Start",
            FromPort = WorkflowPorts.Success,
            ToNodeId = "End",
            ToPort = WorkflowPorts.Input
        };
        canvas.Connections.Add(connection);
        var session = new WorkflowDesignerSession(canvasDocument, catalog);

        var inserted = session.AddNodeOnConnection("Delay", 120, 0, connection);

        Assert.NotNull(inserted);
        Assert.Equal(3, canvas.Nodes.Count);
        Assert.Equal(2, canvas.Connections.Count);
        Assert.Contains(canvas.Connections, item => item.FromNodeId == "Start" && item.ToNodeId == inserted!.Node.Id);
        Assert.Contains(canvas.Connections, item => item.FromNodeId == inserted!.Node.Id && item.ToNodeId == "End");
        Assert.True(session.Undo());
        Assert.Equal(2, canvas.Nodes.Count);
        Assert.Same(connection, Assert.Single(canvas.Connections));
    }

    [Fact]
    public void MoveSingleEndpoint_ClearsStaleWaypointsAndUndoRestoresThem()
    {
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        var canvasDocument = new WorkflowDocument();
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new StartNodeModel { Id = "Start", Title = "开始" } });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new EndNodeModel { Id = "End", Title = "结束" }, X = 200 });
        var connection = new WorkflowConnectionModel { FromNodeId = "Start", ToNodeId = "End" };
        connection.Waypoints.Add(new WorkflowPoint(100, 150));
        canvas.Connections.Add(connection);
        var session = new WorkflowDesignerSession(canvasDocument, catalog);

        session.MoveNodes(new Dictionary<string, WorkflowPoint> { ["End"] = new(200, 80) });

        Assert.Empty(connection.Waypoints);
        Assert.True(session.Undo());
        Assert.Equal(new WorkflowPoint(100, 150), Assert.Single(connection.Waypoints));
    }

    [Fact]
    public void MoveSelectedEndpointPair_TranslatesConnectionWaypoints()
    {
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        var canvasDocument = new WorkflowDocument();
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new StartNodeModel { Id = "Start", Title = "开始" } });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new EndNodeModel { Id = "End", Title = "结束" }, X = 200 });
        var connection = new WorkflowConnectionModel { FromNodeId = "Start", ToNodeId = "End" };
        connection.Waypoints.Add(new WorkflowPoint(100, 50));
        canvas.Connections.Add(connection);
        var session = new WorkflowDesignerSession(canvasDocument, catalog);
        var startItem = canvas.Nodes.Single(item => item.Node.Id == "Start");
        var endItem = canvas.Nodes.Single(item => item.Node.Id == "End");

        session.MoveNodes(new Dictionary<string, WorkflowPoint>
        {
            ["Start"] = new(startItem.X + 20, startItem.Y + 30),
            ["End"] = new(endItem.X + 20, endItem.Y + 30)
        });

        Assert.Equal(new WorkflowPoint(120, 80), Assert.Single(connection.Waypoints));
        Assert.True(session.Undo());
        Assert.Equal(new WorkflowPoint(100, 50), Assert.Single(connection.Waypoints));
    }

    [Fact]
    public void ReplaceConnectionWaypoints_MergesNearbyAndCollinearPoints()
    {
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        var canvasDocument = new WorkflowDocument();
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new StartNodeModel { Id = "Start", Title = "开始" } });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new EndNodeModel { Id = "End", Title = "结束" } });
        var connection = new WorkflowConnectionModel { FromNodeId = "Start", ToNodeId = "End" };
        canvas.Connections.Add(connection);
        var session = new WorkflowDesignerSession(canvasDocument, catalog);

        session.ReplaceConnectionWaypoints(connection, new[]
        {
            new WorkflowPoint(50, 20),
            new WorkflowPoint(53, 22),
            new WorkflowPoint(100, 20),
            new WorkflowPoint(150, 20)
        });

        Assert.Equal(new[] { new WorkflowPoint(48, 24), new WorkflowPoint(144, 24) }, connection.Waypoints);
    }

    [Fact]
    public void ReplaceConnectionWaypoints_IsUndoable()
    {
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        var canvasDocument = new WorkflowDocument();
        var canvas = canvasDocument.CanvasProjection;
        var start = new WorkflowCanvasNode { Node = new StartNodeModel { Id = "Start", Title = "开始" } };
        var end = new WorkflowCanvasNode { Node = new EndNodeModel { Id = "End", Title = "结束" } };
        canvas.Nodes.Add(start);
        canvas.Nodes.Add(end);
        var connection = new WorkflowConnectionModel { FromNodeId = "Start", ToNodeId = "End" };
        canvas.Connections.Add(connection);
        var session = new WorkflowDesignerSession(canvasDocument, catalog);

        session.ReplaceConnectionWaypoints(connection, new[]
        {
            new WorkflowPoint(100, 20),
            new WorkflowPoint(100, 80)
        });

        Assert.Equal(2, connection.Waypoints.Count);
        Assert.True(session.Undo());
        Assert.Empty(connection.Waypoints);
    }

    [Fact]
    public void SetPortSide_IsUndoablePerNodeInstance()
    {
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        var canvasDocument = new WorkflowDocument();
        var canvas = canvasDocument.CanvasProjection;
        var item = new WorkflowCanvasNode { Node = new StartNodeModel { Id = "Start", Title = "开始" } };
        canvas.Nodes.Add(item);
        var session = new WorkflowDesignerSession(canvasDocument, catalog);
        var port = Assert.Single(session.GetPorts("Start", WorkflowPortDirection.Output));

        session.SetPortSide("Start", WorkflowPortDirection.Output, port.Key, WorkflowPortSide.Right);
        Assert.Equal(WorkflowPortSide.Right, item.GetPortSide(port));
        Assert.True(session.Undo());
        Assert.Equal(WorkflowPortSide.Bottom, item.GetPortSide(port));
    }

    [Fact]
    public void AddConnectMoveAndUndoRedo_ModifySingleSharedDocument()
    {
        var session = CreateSession();
        var start = session.AddNode("Start", 10, 20);
        var end = session.AddNode("End", 300, 20);
        var connection = session.Connect(start.Node.Id, WorkflowPorts.Success, end.Node.Id);
        var initialEndX = end.X;

        Assert.Equal(2, session.Canvas.Nodes.Count);
        Assert.Single(session.Canvas.Connections);
        Assert.True(session.MoveNode(end.Node.Id, 350, 80));
        Assert.Equal(0, (end.X + end.Width / 2) % 24, 6);

        Assert.True(session.Undo());
        Assert.Equal(initialEndX, end.X);
        Assert.True(session.Undo());
        Assert.Empty(session.Canvas.Connections);
        Assert.True(session.Redo());
        Assert.Same(connection, Assert.Single(session.Canvas.Connections));
    }

    [Fact]
    public void RemoveNode_UndoRestoresNodeAndIncidentConnections()
    {
        var session = CreateSession();
        var start = session.AddNode("Start", 0, 0);
        var action = session.AddNode("Action", 200, 0);
        var end = session.AddNode("End", 400, 0);
        session.Connect(start.Node.Id, WorkflowPorts.Success, action.Node.Id);
        session.Connect(action.Node.Id, WorkflowPorts.Success, end.Node.Id);

        Assert.True(session.RemoveNode(action.Node.Id));
        Assert.Equal(2, session.Canvas.Nodes.Count);
        Assert.Empty(session.Canvas.Connections);
        Assert.True(session.Undo());
        Assert.Equal(3, session.Canvas.Nodes.Count);
        Assert.Equal(2, session.Canvas.Connections.Count);
    }

    [Fact]
    public void SelectAndRemoveConnection_ClearsSelectionAndSupportsUndo()
    {
        var session = CreateSession();
        var start = session.AddNode("Start", 0, 0);
        var end = session.AddNode("End", 200, 0);
        var connection = session.Connect(start.Node.Id, WorkflowPorts.Success, end.Node.Id);

        Assert.True(session.SelectConnection(connection));
        Assert.Same(connection, session.SelectedConnection);
        Assert.Null(session.SelectedNodeId);
        Assert.True(session.RemoveConnection(connection));
        Assert.Null(session.SelectedConnection);
        Assert.Empty(session.Canvas.Connections);
        Assert.True(session.Undo());
        Assert.Same(connection, Assert.Single(session.Canvas.Connections));
    }

    [Fact]
    public void WaypointOperations_AreUndoable()
    {
        var session = CreateSession();
        var start = session.AddNode("Start", 0, 0);
        var end = session.AddNode("End", 200, 0);
        var connection = session.Connect(start.Node.Id, WorkflowPorts.Success, end.Node.Id);

        session.AddConnectionWaypoint(connection, new WorkflowPoint(100, 40));
        Assert.True(session.MoveConnectionWaypoint(connection, 0, new WorkflowPoint(120, 60)));
        Assert.Equal(new WorkflowPoint(120, 48), Assert.Single(connection.Waypoints));
        Assert.True(session.RemoveConnectionWaypoint(connection, 0));
        Assert.Empty(connection.Waypoints);

        Assert.True(session.Undo());
        Assert.Equal(new WorkflowPoint(120, 48), Assert.Single(connection.Waypoints));
        Assert.True(session.Undo());
        Assert.Equal(new WorkflowPoint(96, 48), Assert.Single(connection.Waypoints));
    }

    [Fact]
    public void Connect_EnforcesDeclaredPortCardinality()
    {
        var session = CreateSession();
        var start = session.AddNode("Start", 0, 0);
        var first = session.AddNode("End", 200, 0);
        var second = session.AddNode("End", 200, 100);
        session.Connect(start.Node.Id, WorkflowPorts.Success, first.Node.Id);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            session.Connect(start.Node.Id, WorkflowPorts.Success, second.Node.Id));

        Assert.Contains("连接上限", exception.Message);
    }

    [Fact]
    public void FitToView_CentersAllNodesInsideViewport()
    {
        var session = CreateSession();
        session.AddNode("Start", 100, 100);
        session.AddNode("End", 500, 300);

        session.FitToView(1000, 600);

        foreach (var node in session.Canvas.Nodes)
        {
            var rect = WorkflowDesignerGeometry.GetNodeScreenRect(session, node);
            Assert.InRange(rect.X, 0, 1000);
            Assert.InRange(rect.Y, 0, 600);
            Assert.InRange(rect.X + rect.Width, 0, 1000);
            Assert.InRange(rect.Y + rect.Height, 0, 600);
        }
    }

    [Fact]
    public void ViewportAndGeometry_RoundTripAndClampZoom()
    {
        var session = CreateSession();
        session.SetViewport(99, 120, -30);
        var screen = WorkflowDesignerGeometry.CanvasToScreen(session, 40, 80);
        var canvas = WorkflowDesignerGeometry.ScreenToCanvas(session, screen.X, screen.Y);

        Assert.Equal(2.5, session.Zoom);
        Assert.Equal(40, canvas.X, 6);
        Assert.Equal(80, canvas.Y, 6);
    }

    [Fact]
    public void DynamicPortChange_DetachesConnectionAndUndoRestoresIt()
    {
        var catalog = new WorkflowNodeCatalog()
            .RegisterStandardNodes()
            .Register(WorkflowNodeDescriptor.Create<DynamicPortNode>(ports: new[]
            {
                WorkflowPortDescriptor.Input(),
                WorkflowPortDescriptor.Output(WorkflowPorts.Success)
            }));
        var source = new DynamicPortNode { Id = "Dynamic", Title = "动态", HasAlternate = true };
        var end = new EndNodeModel { Id = "End", Title = "结束" };
        var canvasDocument = new WorkflowDocument();
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = source });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = end });
        var connection = new WorkflowConnectionModel
        {
            FromNodeId = source.Id,
            FromPort = "Alternate",
            ToNodeId = end.Id,
            ToPort = WorkflowPorts.Input
        };
        canvas.Connections.Add(connection);
        var session = new WorkflowDesignerSession(canvasDocument, catalog);

        session.ExecuteNodeConfigurationChange(source.Id, node => ((DynamicPortNode)node).HasAlternate = false);

        Assert.Same(connection, Assert.Single(canvas.Connections));
        Assert.Equal(WorkflowConnectionState.Detached, connection.State);
        Assert.Contains("Alternate", connection.Diagnostic, StringComparison.Ordinal);
        Assert.True(session.Undo());
        Assert.Equal(WorkflowConnectionState.Active, connection.State);
        Assert.True(((DynamicPortNode)source).HasAlternate);
    }

    private sealed class DynamicPortNode : WorkflowNodeModel, IWorkflowDynamicPortProvider
    {
        public override string NodeType => "DynamicPortTest";

        public bool HasAlternate { get; set; }

        public IReadOnlyList<WorkflowPortDescriptor> GetPorts(IReadOnlyList<WorkflowPortDescriptor> basePorts) =>
            HasAlternate
                ? basePorts.Concat(new[] { WorkflowPortDescriptor.Output("Alternate") }).ToArray()
                : basePorts;
    }

    private static WorkflowDesignerSession CreateSession() => new(
        new WorkflowDocument { Name = "UI" },
        new WorkflowNodeCatalog().RegisterStandardNodes());
}
