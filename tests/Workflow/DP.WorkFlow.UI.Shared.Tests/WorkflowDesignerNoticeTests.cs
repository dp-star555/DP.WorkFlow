using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowDesignerNoticeTests
{
    [Fact]
    public void PlanPath_MatchesEngineFormatAndSplitsBack()
    {
        var path = WorkflowNoticeLocation.PlanPath(new[] { "Loop 1", "Sub/2" });

        Assert.Equal("$/Loop%201/Sub%2F2", path);
        Assert.Equal(new[] { "Loop 1", "Sub/2" }, WorkflowNoticeLocation.Split(path));
        Assert.Empty(WorkflowNoticeLocation.Split("$"));
        Assert.Equal("$", WorkflowNoticeLocation.PlanPath(Array.Empty<string>()));
    }

    [Fact]
    public void Describe_NamesCanvasAndNode()
    {
        var node = new WorkflowNotice(1, DateTimeOffset.UtcNow, Guid.Empty, "$", "Camera", "超时", "相机响应慢");
        var canvas = new WorkflowNotice(2, DateTimeOffset.UtcNow, Guid.Empty, "$/Loop", null, "记录", "丢弃");

        Assert.Equal("主流程 · Camera", WorkflowNoticeLocation.Describe(node));
        Assert.Equal("Loop · 画布", WorkflowNoticeLocation.Describe(canvas));
    }

    [Fact]
    public void Session_ReadsAndAcknowledgesNoticesForItsOwnCanvas()
    {
        var (session, nodeId) = CreateSession();
        var board = new WorkflowNoticeBoard();
        session.Notices = board;
        board.Raise(Guid.Empty, "$", nodeId, "超时", "相机响应慢");
        board.Raise(Guid.Empty, "$", nodeId, "超时", "相机响应慢");
        board.Raise(Guid.Empty, "$/Other", nodeId, "超时", "别的画布");
        board.Raise(Guid.Empty, "$", null, "记录", "丢弃最早的记录");

        var notices = session.GetNodeNotices(nodeId);
        Assert.Single(notices);
        Assert.Equal(2, notices[0].Count);
        Assert.Single(session.GetCanvasNotices());

        var version = session.NoticeVersion;
        Assert.Equal(1, session.AcknowledgeNodeNotices(nodeId));
        Assert.Empty(session.GetNodeNotices(nodeId));
        Assert.True(session.NoticeVersion > version);
        // 其他画布的同名节点不受影响。
        Assert.Single(board.Get("$/Other", nodeId));
    }

    [Fact]
    public void Session_ChildCanvasUsesRuntimePath()
    {
        var (session, nodeId) = CreateSession();
        var board = new WorkflowNoticeBoard();
        session.Notices = board;
        session.RuntimePath = new[] { "Loop" };
        board.Raise(Guid.Empty, "$/Loop", nodeId, "超时", "子画布里的提示");

        Assert.Equal("$/Loop", session.NoticePlanPath);
        Assert.Single(session.GetNodeNotices(nodeId));
    }

    [Fact]
    public void NoticeBadge_SitsOnTopRightEdgeAndIsHitBeforeNode()
    {
        var (session, nodeId) = CreateSession();
        var board = new WorkflowNoticeBoard();
        session.Notices = board;
        var node = session.Document.Nodes.Single(item => item.Node.Id == nodeId);
        var rect = WorkflowDesignerGeometry.GetNodeScreenRect(session, node);

        Assert.Null(WorkflowDesignerGeometry.HitNoticeBadge(session, rect.X + rect.Width, rect.Y));
        board.Raise(Guid.Empty, "$", nodeId, "超时", "相机响应慢");

        var badge = WorkflowDesignerGeometry.NoticeBadgeBounds(rect, session.Zoom, 1);
        // 角标跨在节点上边框上，右边超出节点右边框。
        Assert.True(badge.Y < rect.Y && badge.Y + badge.Height > rect.Y);
        Assert.True(badge.X + badge.Width > rect.X + rect.Width);
        Assert.Same(node, WorkflowDesignerGeometry.HitNoticeBadge(session, badge.X + badge.Width / 2, badge.Y + badge.Height / 2));
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(99, "99")]
    [InlineData(100, "99+")]
    public void NoticeBadgeText_CapsAtNinetyNine(int count, string expected) =>
        Assert.Equal(expected, WorkflowDesignerGeometry.NoticeBadgeText(count));

    private static (WorkflowDesignerSession Session, string NodeId) CreateSession()
    {
        var document = new WorkflowDocument();
        var node = new DelayNodeModel { Id = "Delay", Title = "等待" };
        document.Nodes.Add(new WorkflowCanvasNode { Node = node, X = 96, Y = 96 });
        var session = new WorkflowDesignerSession(document, new WorkflowNodeCatalog().RegisterStandardNodes());
        return (session, node.Id);
    }
}
