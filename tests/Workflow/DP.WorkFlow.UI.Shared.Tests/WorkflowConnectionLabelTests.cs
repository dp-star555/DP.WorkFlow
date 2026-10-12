using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

/// <summary>连线标签：固定语义始终保留，备注附在其后；并行分支自动编号，可以停用。</summary>
public sealed class WorkflowConnectionLabelTests
{
    private static (WorkflowDesignerSession Session, WorkflowConnectionModel BranchA, WorkflowConnectionModel BranchB, WorkflowConnectionModel True, WorkflowConnectionModel Success) Create()
    {
        var session = new WorkflowDesignerSession(new WorkflowDocument { Name = "标签" }, new WorkflowNodeCatalog().RegisterStandardNodes());
        var start = session.AddNode("Start", 0, 0);
        var parallel = session.AddNode("ParallelAll", 0, 100);
        var a = session.AddNode("Decision", -100, 200);
        var b = session.AddNode("Delay", 100, 200);
        var merge = session.AddNode("WaitAllInputsCompleted", 0, 300);
        var success = session.Connect(start.Node.Id, WorkflowPorts.Success, parallel.Node.Id);
        var branchA = session.Connect(parallel.Node.Id, WorkflowPorts.Branch, a.Node.Id);
        var branchB = session.Connect(parallel.Node.Id, WorkflowPorts.Branch, b.Node.Id);
        var yes = session.Connect(a.Node.Id, WorkflowPorts.True, merge.Node.Id);
        session.Connect(b.Node.Id, WorkflowPorts.Success, merge.Node.Id);
        return (session, branchA, branchB, yes, success);
    }

    [Fact]
    public void Labels_KeepFixedSemanticsAndAppendNotes()
    {
        var (session, branchA, branchB, yes, success) = Create();
        Assert.Equal("分支1", WorkflowDesignerInteraction.GetConnectionLabel(session, branchA));
        Assert.Equal("分支2", WorkflowDesignerInteraction.GetConnectionLabel(session, branchB));
        Assert.Equal("成立", WorkflowDesignerInteraction.GetConnectionLabel(session, yes));
        Assert.False(WorkflowDesignerInteraction.ShouldDrawConnectionLabel(session, success)); // 单一成功出口不显示标签。

        Assert.True(session.SetConnectionNote(branchA, " 左相机 "));
        Assert.True(session.SetConnectionNote(yes, "有料"));
        Assert.True(session.SetConnectionNote(success, "复检"));
        Assert.Equal("分支 · 左相机", WorkflowDesignerInteraction.GetConnectionLabel(session, branchA));
        Assert.Equal("分支2", WorkflowDesignerInteraction.GetConnectionLabel(session, branchB));
        Assert.Equal("成立 · 有料", WorkflowDesignerInteraction.GetConnectionLabel(session, yes));
        Assert.Equal("复检", WorkflowDesignerInteraction.GetConnectionLabel(session, success));
        Assert.True(WorkflowDesignerInteraction.ShouldDrawConnectionLabel(session, success));

        Assert.False(session.SetConnectionNote(branchA, "左相机"));
        Assert.True(session.SetConnectionNote(branchA, "  "));
        Assert.Null(branchA.Note);
        Assert.Equal("分支1", WorkflowDesignerInteraction.GetConnectionLabel(session, branchA));
        Assert.True(session.Undo());
        Assert.Equal("左相机", branchA.Note);
        Assert.Throws<ArgumentException>(() => session.SetConnectionNote(yes, new string('长', 65)));

        session.SetConnectionNote(yes, new string('长', 30));
        var shown = WorkflowDesignerInteraction.GetConnectionLabelDisplay(session, yes);
        Assert.Equal(WorkflowDesignerInteraction.MaximumLabelLength, shown.Length);
        Assert.EndsWith("…", shown);
    }

    [Fact]
    public void ParallelBranches_CanBeDisabledAndReenabled()
    {
        var (session, branchA, branchB, yes, _) = Create();
        Assert.True(WorkflowDesignerInteraction.CanDisableConnection(session, branchB));
        Assert.False(WorkflowDesignerInteraction.CanDisableConnection(session, yes));
        Assert.Throws<InvalidOperationException>(() => session.SetConnectionEnabled(yes, false));

        Assert.True(session.SetConnectionEnabled(branchB, false));
        Assert.False(branchB.Enabled);
        Assert.Equal("分支2（停用）", WorkflowDesignerInteraction.GetConnectionLabel(session, branchB));
        Assert.True(session.Undo());
        Assert.True(branchB.Enabled);
        Assert.True(session.Redo());
        Assert.False(branchB.Enabled);
        Assert.True(session.SetConnectionEnabled(branchB, true));
        Assert.Equal("分支1", WorkflowDesignerInteraction.GetConnectionLabel(session, branchA));
    }
}
