using DP.WorkFlow.Persistence.Json;

namespace DP.WorkFlow.UI;

/// <summary>
/// 发布和放弃草稿的共同流程，流程库页和设计页共用：先让宿主检查草稿（编译、设备检查），
/// 再经过操作准入（鉴权、审计），最后改文件。界面只负责问说明和显示结果。
/// </summary>
public sealed class WorkflowDraftPublisher
{
    /// <summary>创建发布器。</summary>
    /// <param name="library">流程库。</param>
    public WorkflowDraftPublisher(WorkflowFlowLibrary library) =>
        Library = library ?? throw new ArgumentNullException(nameof(library));

    /// <summary>流程库。</summary>
    public WorkflowFlowLibrary Library { get; }

    /// <summary>发布前检查草稿：参数是库里的名字和草稿 JSON，返回不能发布的原因；为空时不检查。</summary>
    public Func<string, string, IReadOnlyList<string>>? CheckDraft { get; set; }

    /// <summary>不能发布的原因（例如“有流程在运行”）；返回空表示可以。为空时总是可以。</summary>
    public Func<string?>? BlockedReason { get; set; }

    /// <summary>操作准入（鉴权、审计）；默认直接执行。</summary>
    public IWorkflowOperationGate Gate { get; set; } = WorkflowOperationGates.Direct;

    /// <summary>当前操作人（记进版本）。</summary>
    public Func<string?>? CurrentUser { get; set; }

    /// <summary>草稿和已发布的差别，给确认框看。</summary>
    /// <param name="name">库里的名字。</param>
    public string DescribeChanges(string name)
    {
        var entry = Library.List().FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        var title = entry is { IsPublished: false } ? "第一次发布" : "草稿 → 已发布";
        return WorkflowFlowComparisonText.Describe(title, Library.CompareDraft(name));
    }

    /// <summary>发布前检查，返回问题；没有问题时为空集合。</summary>
    /// <param name="name">库里的名字。</param>
    public IReadOnlyList<string> Check(string name)
    {
        if (BlockedReason?.Invoke() is { Length: > 0 } blocked)
            return new[] { blocked };
        if (!File.Exists(Library.DraftPathOf(name)))
            return new[] { $"{name} 没有草稿，不需要发布。" };
        return CheckDraft?.Invoke(name, File.ReadAllText(Library.DraftPathOf(name))) ?? Array.Empty<string>();
    }

    /// <summary>检查后发布。检查不过时抛 <see cref="InvalidOperationException"/>，消息是全部问题。</summary>
    /// <param name="name">库里的名字。</param>
    /// <param name="note">发布说明。</param>
    /// <returns>发布后的版本。</returns>
    public async Task<WorkflowLibraryVersion> PublishAsync(string name, string? note)
    {
        var issues = Check(name);
        if (issues.Count > 0)
            throw new InvalidOperationException("不能发布：" + string.Join("；", issues));
        WorkflowLibraryVersion? version = null;
        await Gate.RunAsync(new WorkflowOperation(WorkflowOperationKind.Publish, name, string.IsNullOrWhiteSpace(note) ? null : note!.Trim()), () =>
        {
            version = Library.Publish(name, CurrentUser?.Invoke(), note);
            return Task.CompletedTask;
        }).ConfigureAwait(true);
        return version ?? throw new InvalidOperationException("发布被拒绝。");
    }

    /// <summary>放弃草稿（经过操作准入）。</summary>
    /// <param name="name">库里的名字。</param>
    public Task DiscardAsync(string name) =>
        Gate.RunAsync(new WorkflowOperation(WorkflowOperationKind.DiscardDraft, name), () =>
        {
            Library.DiscardDraft(name);
            return Task.CompletedTask;
        });
}

/// <summary>把流程比较结果写成几行中文。</summary>
public static class WorkflowFlowComparisonText
{
    /// <summary>描述差别。</summary>
    /// <param name="title">标题，例如“v2 → 当前”。</param>
    /// <param name="diff">比较结果。</param>
    public static string Describe(string title, WorkflowFlowComparison diff)
    {
        if (diff is null) throw new ArgumentNullException(nameof(diff));
        if (diff.IsSame) return title + "：没有差别。";
        var lines = new List<string> { title + "：" };
        void Add(string label, IReadOnlyList<string> items)
        {
            if (items.Count > 0) lines.Add($"{label}（{items.Count}）：{string.Join("、", items)}");
        }
        Add("新增节点", diff.AddedNodes);
        Add("删掉节点", diff.RemovedNodes);
        Add("参数变了", diff.ChangedNodes);
        Add("只挪了位置", diff.MovedNodes);
        Add("新增连线", diff.AddedConnections);
        Add("删掉连线", diff.RemovedConnections);
        foreach (var other in diff.OtherChanges) lines.Add(other);
        return string.Join(Environment.NewLine, lines);
    }
}
