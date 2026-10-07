using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowNodeEditorPropertyContributorTests
{
    [Fact]
    public void Compose_AppendsPageContributionsAfterHostProperties_AndKeepsHostWhenNoContributor()
    {
        var node = new CSharpScriptNodeModel { Id = "Script" };
        Func<IWorkflowNodeModel, IReadOnlyList<WorkflowPropertyEntry>> host = _ => new[] { Entry("Host") };
        var plain = new WorkflowNodeEditorPageDescriptor("Plain", "页", WorkflowNodeEditorPageKind.Custom, 1, new object());
        Assert.Same(host, WorkflowNodeEditorPropertyContributors.Compose(host, new[] { plain }));
        Assert.Null(WorkflowNodeEditorPropertyContributors.Compose(null, new[] { plain }));

        var contributor = new Contributor();
        var page = new WorkflowNodeEditorPageDescriptor("Custom", "页", WorkflowNodeEditorPageKind.Custom, 2, contributor);
        var composed = WorkflowNodeEditorPropertyContributors.Compose(host, new[] { plain, page })!;
        Assert.Empty(composed(node).Skip(1));
        // 贡献按每次重建读取：页面控件连接后才出现的按钮在下一次重建时显示。
        contributor.Ready = true;
        Assert.Equal(new[] { "Host", "Action" }, composed(node).Select(e => e.Name));
        Assert.Same(node, contributor.LastNode);
        Assert.Equal(new[] { "Action" }, WorkflowNodeEditorPropertyContributors.Compose(null, new[] { page })!(node).Select(e => e.Name));
    }

    private static WorkflowPropertyEntry Entry(string name) => WorkflowPropertyEntry.Create(name, name, "分组", "", WorkflowPropertyEditorKind.Text,
        typeof(string), () => "", _ => { });

    private sealed class Contributor : IWorkflowNodeEditorPropertyContributor
    {
        public bool Ready;
        public IWorkflowNodeModel? LastNode;
        public IEnumerable<WorkflowPropertyEntry> CreateProperties(IWorkflowNodeModel editingNode)
        {
            LastNode = editingNode;
            if (Ready)
                yield return WorkflowPropertyEntry.CreateAction("Action", "操作", "分组", "", () => "执行", () => Task.CompletedTask);
        }
    }
}
