using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowNodeVisualTests
{
    [Fact]
    public void NodeRows_FollowContent()
    {
        var input = new WorkflowPortDescriptor(WorkflowPorts.Input, WorkflowPortDirection.Input);
        var success = new WorkflowPortDescriptor(WorkflowPorts.Success, WorkflowPortDirection.Output);
        var failed = new WorkflowPortDescriptor(WorkflowPorts.Failed, WorkflowPortDirection.Output);

        // 上进下出、单出口：没有正文，节点只有 48 高的标题行。
        var plain = WorkflowDesignerGeometry.GetNodeRows(new[] { (input, WorkflowPortSide.Top), (success, WorkflowPortSide.Bottom) }, 0);
        Assert.Equal(0, plain.Count);
        Assert.Equal(WorkflowDesignerGeometry.HeaderHeight, plain.NodeHeight);

        // 下边两个出口要显示名称：一行名称行；两个外置数据成员各一行。
        var rows = WorkflowDesignerGeometry.GetNodeRows(
            new[] { (input, WorkflowPortSide.Top), (success, WorkflowPortSide.Bottom), (failed, WorkflowPortSide.Bottom) }, 2);
        Assert.Equal(new WorkflowNodeRows(0, 0, 2, 1), rows);
        Assert.Equal(48 + 3 * 24, rows.NodeHeight);

        // 侧边端口逐行排列，行数取两边的较大者。
        var side = WorkflowDesignerGeometry.GetNodeRows(
            new[] { (input, WorkflowPortSide.Left), (success, WorkflowPortSide.Right), (failed, WorkflowPortSide.Right) }, 0);
        Assert.Equal(new WorkflowNodeRows(0, 2, 0, 0), side);
    }

    [Fact]
    public void PortLabels_StayInsideTheirRow()
    {
        var node = new WorkflowDesignerRect(0, 0, 192, 96);
        var (left, leftAlign) = WorkflowDesignerGeometry.GetPortLabelBounds(node, 1, WorkflowPortSide.Left, new WorkflowPoint(0, 60), 1);
        Assert.Equal(-1, leftAlign);
        Assert.Equal(48, left.Y);
        Assert.Equal(24, left.Height);
        Assert.True(left.X + left.Width <= 96);

        var (right, rightAlign) = WorkflowDesignerGeometry.GetPortLabelBounds(node, 1, WorkflowPortSide.Right, new WorkflowPoint(192, 60), 1);
        Assert.Equal(1, rightAlign);
        Assert.True(right.X >= 96 && right.X + right.Width <= 192);

        var (bottom, bottomAlign) = WorkflowDesignerGeometry.GetPortLabelBounds(node, 1, WorkflowPortSide.Bottom, new WorkflowPoint(64, 96), 2);
        Assert.Equal(0, bottomAlign);
        Assert.Equal(72, bottom.Y);
        Assert.Equal(64, bottom.X + bottom.Width / 2, 6);
    }

    [Fact]
    public void VisualState_FollowsRuntimeAndPausedWorkflow()
    {
        var session = new WorkflowDesignerSession(new WorkflowDocument { Name = "Visual" }, new WorkflowNodeCatalog().RegisterStandardNodes());
        var node = session.AddNode("Delay", 0, 0);
        Assert.Equal(WorkflowNodeVisualState.Idle, session.GetNodeVisualState(node.Node.Id));

        session.SetRuntimeSnapshot(Snapshot(E_WorkflowExecutionState.Running, node.Node.Id, E_NodeState.Running));
        Assert.Equal(WorkflowNodeVisualState.Running, session.GetNodeVisualState(node.Node.Id));
        session.SetRuntimeSnapshot(Snapshot(E_WorkflowExecutionState.Paused, node.Node.Id, E_NodeState.Running));
        Assert.Equal(WorkflowNodeVisualState.Paused, session.GetNodeVisualState(node.Node.Id));
        session.SetRuntimeSnapshot(Snapshot(E_WorkflowExecutionState.Canceled, node.Node.Id, E_NodeState.Canceled));
        Assert.Equal(WorkflowNodeVisualState.Paused, session.GetNodeVisualState(node.Node.Id));
        session.SetRuntimeSnapshot(Snapshot(E_WorkflowExecutionState.Faulted, node.Node.Id, E_NodeState.Failed));
        Assert.Equal(WorkflowNodeVisualState.Failed, session.GetNodeVisualState(node.Node.Id));
        session.SetRuntimeSnapshot(Snapshot(E_WorkflowExecutionState.Completed, node.Node.Id, E_NodeState.Completed));
        Assert.Equal(WorkflowNodeVisualState.Completed, session.GetNodeVisualState(node.Node.Id));
    }

    [Fact]
    public void NodeStyle_SchemeA()
    {
        // 平时、完成：中性边框；完成只把状态点变绿。
        var idle = WorkflowDesignerPalette.GetNodeStyle(WorkflowNodeVisualState.Idle, false);
        var completed = WorkflowDesignerPalette.GetNodeStyle(WorkflowNodeVisualState.Completed, false);
        Assert.Equal(WorkflowDesignerPalette.NodeBorder, idle.Border);
        Assert.Equal(WorkflowDesignerPalette.Idle, idle.Indicator);
        Assert.Equal(WorkflowDesignerPalette.NodeBorder, completed.Border);
        Assert.Equal(WorkflowDesignerPalette.Running, completed.Indicator);

        // 运行绿框加外发光，暂停黄框，异常红框且序号徽标红底。
        var running = WorkflowDesignerPalette.GetNodeStyle(WorkflowNodeVisualState.Running, false);
        Assert.Equal(WorkflowDesignerPalette.Running, running.Border);
        Assert.True(running.Glow.A > 0);
        Assert.Equal(WorkflowDesignerPalette.Paused, WorkflowDesignerPalette.GetNodeStyle(WorkflowNodeVisualState.Paused, false).Border);
        var failed = WorkflowDesignerPalette.GetNodeStyle(WorkflowNodeVisualState.Failed, false);
        Assert.Equal(WorkflowDesignerPalette.Failed, failed.Border);
        Assert.NotEqual(idle.BadgeBackground, failed.BadgeBackground);
        Assert.Equal(0, failed.Glow.A);

        // 选中只覆盖边框，状态点仍表示运行状态。
        var selected = WorkflowDesignerPalette.GetNodeStyle(WorkflowNodeVisualState.Failed, true);
        Assert.Equal(WorkflowDesignerPalette.Selected, selected.Border);
        Assert.Equal(WorkflowDesignerPalette.Failed, selected.Indicator);
        Assert.Equal(failed.BadgeBackground, selected.BadgeBackground);
    }

    [Fact]
    public void VectorPath_ParsesLinesCurvesAndArcs()
    {
        var figures = WorkflowVectorPath.Parse("M4 4h16v16H4z M12 2 l0 4");
        Assert.Equal(2, figures.Count);
        Assert.True(figures[0].Closed);
        Assert.Equal(new WorkflowPoint(4, 20), figures[0].Segments[^1].End);
        Assert.False(figures[1].Closed);
        Assert.Equal(new WorkflowPoint(12, 6), figures[1].Segments[0].End);

        // 整圆由两段半圆弧组成，转换为不超过 90° 的三次贝塞尔曲线，每段终点都在圆上。
        var circle = WorkflowVectorPath.Parse("M4 12a8 8 0 1 0 16 0a8 8 0 1 0 -16 0");
        var segments = circle.Single().Segments;
        Assert.Equal(4, segments.Count);
        Assert.All(segments, segment =>
        {
            Assert.True(segment.IsCubic);
            Assert.Equal(8, Math.Sqrt(Math.Pow(segment.End.X - 12, 2) + Math.Pow(segment.End.Y - 12, 2)), 6);
        });
        Assert.Equal(new WorkflowPoint(4, 12), segments[^1].End);

        // 紧凑写法：标志位贴着坐标、负号作分隔。
        Assert.Single(WorkflowVectorPath.Parse("M2 2c1-1 2-1 3 0s2 1 3 0"));
        Assert.Throws<FormatException>(() => WorkflowVectorPath.Parse("M0 0 X 1 1"));
    }

    [Fact]
    public void Icons_ResolveDeclaredThenTypeThenCategory()
    {
        Assert.Equal("camera", WorkflowNodeIcons.Resolve("Vision.Unknown", "5.Vision/1.取图/相机").Key);
        Assert.Equal(WorkflowNodeIcons.DefaultKey, WorkflowNodeIcons.Resolve("Unknown", "Unknown").Key);
        Assert.Equal("clock", WorkflowNodeIcons.Resolve("Unknown", null, "clock").Key);
        Assert.NotEqual(WorkflowNodeIcons.DefaultKey, WorkflowNodeIcons.Resolve("Delay", null).Key);

        // 节点定义可以直接声明路径数据；无法解析的声明被忽略，按类型和分类继续查找。
        var custom = WorkflowNodeIcons.Resolve("Unknown", null, "M4 4L20 20");
        Assert.Single(custom.Figures);
        Assert.Equal(WorkflowNodeIcons.DefaultKey, WorkflowNodeIcons.Resolve("Unknown", null, "not a path").Key);

        // 每个内置图标都能解析出至少一段线条。
        Assert.All(WorkflowNodeIcons.Keys, key => Assert.NotEmpty(WorkflowNodeIcons.Resolve("Unknown", null, key).Figures));
    }

    [Fact]
    public void ToolboxItems_CarryTheIconDeclaredByTheNodeDefinition()
    {
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        catalog.Register(WorkflowNodeDescriptor.Create<IconNode>());
        var session = new WorkflowDesignerSession(new WorkflowDocument { Name = "Icons" }, catalog);

        var item = session.GetToolboxItems().Single(item => item.NodeType == IconNode.Key);
        Assert.Equal("camera", item.ResolveIcon().Key);
        var node = session.AddNode(IconNode.Key, 0, 0);
        Assert.Equal("camera", session.GetNodeIcon(node.Node).Key);
    }

    [WorkflowNode(Key, DisplayName = "图标节点", Category = "Test", Icon = "camera")]
    private sealed class IconNode : WorkflowNodeModel
    {
        public const string Key = "Test.IconNode";
        public override string NodeType => Key;
    }

    private static WorkflowRuntimeSnapshot Snapshot(E_WorkflowExecutionState state, string nodeId, E_NodeState nodeState) => new(
        Guid.NewGuid(), 1, DateTimeOffset.UtcNow, "Visual", state, nodeId,
        Array.Empty<string>(), new Dictionary<long, WorkflowActiveTokenInfo>(), TimeSpan.Zero,
        new Dictionary<string, WorkflowNodeRuntimeInfo>
        {
            [nodeId] = new(nodeId, nodeState, 1, 1, DateTimeOffset.UtcNow, null, TimeSpan.FromMilliseconds(5))
        },
        new Dictionary<long, WorkflowParallelScopeInfo>(),
        new Dictionary<string, WorkflowChildRuntimeInfo>(),
        new Dictionary<string, WorkflowChildRuntimeInfo>(),
        Array.Empty<string>());
}
