namespace DP.WorkFlow;

/// <summary>一条运行环境问题：节点在文档里的路径（子文档节点 ID 用 / 连接）和说明。</summary>
/// <param name="NodePath">节点路径，例如 loop/pick。</param>
/// <param name="NodeTitle">节点标题；没有时为节点 ID。</param>
/// <param name="Message">问题说明。</param>
public sealed record WorkflowEnvironmentIssue(string NodePath, string NodeTitle, string Message)
{
    /// <inheritdoc />
    public override string ToString() => $"{NodeTitle}（{NodePath}）：{Message}";
}

/// <summary>按宿主服务检查整份流程（含子画布）里节点引用的外部对象，见 <see cref="IWorkflowNodeEnvironmentValidator"/>。</summary>
public static class WorkflowEnvironmentCheck
{
    /// <summary>检查文档；节点自己抛错时记成一条问题，不中断其余节点。</summary>
    /// <param name="document">流程文档。</param>
    /// <param name="services">流程用的宿主服务表。</param>
    public static IReadOnlyList<WorkflowEnvironmentIssue> Check(WorkflowDocument document, IServiceProvider services)
    {
        Guard.NotNull(document);
        Guard.NotNull(services);
        var issues = new List<WorkflowEnvironmentIssue>();
        Visit(document, string.Empty, services, issues, 0);
        return issues;
    }

    private static void Visit(WorkflowDocument document, string prefix, IServiceProvider services, List<WorkflowEnvironmentIssue> issues, int depth)
    {
        if (depth > 64) return;
        foreach (var node in document.Graph.Nodes)
        {
            var path = prefix.Length == 0 ? node.Id : prefix + "/" + node.Id;
            var title = string.IsNullOrWhiteSpace(node.Title) ? node.Id : node.Title;
            if (node is IWorkflowNodeEnvironmentValidator validator)
            {
                try
                {
                    foreach (var message in validator.ValidateEnvironment(services))
                        if (!string.IsNullOrWhiteSpace(message)) issues.Add(new WorkflowEnvironmentIssue(path, title, message));
                }
                catch (Exception ex)
                {
                    issues.Add(new WorkflowEnvironmentIssue(path, title, ex.Message));
                }
            }
            if (node is IWorkflowSubDocumentNode { SubDocument: { } sub })
                Visit(sub, path, services, issues, depth + 1);
        }
    }
}
