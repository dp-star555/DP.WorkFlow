namespace DP.WorkFlow.UI;

/// <summary>区分绑定树中的分组、来源、路径和可选择值节点。</summary>
public enum WorkflowBindingTreeNodeKind
{
    /// <summary>工作流节点输出形成的来源根节点。</summary>
    SourceNode,
    /// <summary>公共数据的分组节点。</summary>
    PublicDataGroup,
    /// <summary>可以直接选择的公共数据节点。</summary>
    PublicData,
    /// <summary>对象成员路径中的中间节点。</summary>
    Member,
    /// <summary>可以转换为绑定键的最终候选节点。</summary>
    Candidate
}

/// <summary>树节点。</summary>
public sealed class WorkflowBindingTreeNode
{
    /// <summary>初始化一个不可变绑定树节点。</summary>
    /// <param name="label">界面显示文本。</param>
    /// <param name="searchText">用于过滤的完整搜索文本。</param>
    /// <param name="kind">节点、页面或变更类型。</param>
    /// <param name="candidate">绑定候选项。</param>
    /// <param name="children">字节点</param>
    internal WorkflowBindingTreeNode(
        string label,
        string searchText,
        WorkflowBindingTreeNodeKind kind,
        WorkflowBindingCandidate? candidate,
        IReadOnlyList<WorkflowBindingTreeNode> children)
    {
        Label = label;
        SearchText = searchText;
        Kind = kind;
        Candidate = candidate;
        Children = children;
    }

    /// <summary>获取树节点的界面显示文本。</summary>
    public string Label { get; }

    /// <summary>获取包含来源和成员路径的搜索文本。</summary>
    public string SearchText { get; }

    /// <summary>获取树节点种类。</summary>
    public WorkflowBindingTreeNodeKind Kind { get; }

    /// <summary>获取叶节点关联的绑定候选项；分组节点为 <see langword="null"/>。</summary>
    public WorkflowBindingCandidate? Candidate { get; }

    /// <summary>获取当前节点的只读子节点集合。</summary>
    public IReadOnlyList<WorkflowBindingTreeNode> Children { get; }
}

/// <summary>成员路径树。</summary>
public sealed class WorkflowBindingTreeModel
{
    private IReadOnlyList<WorkflowBindingCandidate> _candidates = Array.Empty<WorkflowBindingCandidate>();

    /// <summary>获取当前搜索条件下的绑定树根节点。</summary>
    public IReadOnlyList<WorkflowBindingTreeNode> Roots { get; private set; } = Array.Empty<WorkflowBindingTreeNode>();

    /// <summary>获取当前绑定树搜索文本。</summary>
    public string SearchText { get; private set; } = string.Empty;

    /// <summary>获取与当前搜索条件匹配的可选候选项数量。</summary>
    public int MatchCount { get; private set; }

    /// <summary>在模型内容发生变化、界面需要刷新时发生。</summary>
    public event EventHandler? Changed;

    /// <summary>替换绑定候选项并重建树。</summary>
    /// <param name="candidates">候选项集合。</param>
    public void SetCandidates(IEnumerable<WorkflowBindingCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        _candidates = candidates
            .DistinctBy(item => item.ToBindingKey())
            .OrderBy(item => item.SourceNodeId, StringComparer.Ordinal)
            .ThenBy(item => item.MemberPath, StringComparer.Ordinal)
            .ToArray();
        Rebuild();
    }

    /// <summary>设置搜索关键字并过滤绑定树。</summary>
    /// <param name="text">输入文本。</param>
    public void Search(string? text)
    {
        var normalized = text?.Trim() ?? string.Empty;
        if (string.Equals(SearchText, normalized, StringComparison.Ordinal))
            return;
        SearchText = normalized;
        Rebuild();
    }

    /// <summary>根据候选项和搜索条件重新构建绑定树。</summary>
    private void Rebuild()
    {
        var visible = string.IsNullOrWhiteSpace(SearchText)
            ? _candidates
            : _candidates.Where(Matches).ToArray();
        MatchCount = visible.Count;
        var roots = visible
            .Where(item => item.SourceKind == WorkflowBindingCandidateSourceKind.NodeOutput)
            .GroupBy(item => item.SourceNodeId, StringComparer.Ordinal)
            .Select(group => BuildSource(group.Key, group))
            .ToList();
        var publicData = visible.Where(item => item.SourceKind == WorkflowBindingCandidateSourceKind.PublicData).ToArray();
        if (publicData.Length > 0) roots.Add(BuildPublicData(publicData));
        Roots = roots;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>判断绑定候选项是否匹配当前搜索条件。</summary>
    /// <param name="candidate">绑定候选项。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private bool Matches(WorkflowBindingCandidate candidate) =>
        candidate.DisplayPath.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
        || candidate.SourceNodeId.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
        || candidate.MemberPath.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
        || candidate.ValueType.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
        || (candidate.ValueType.FullName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>构建公共数据绑定分组节点。</summary>
    /// <param name="candidates">候选项集合。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static WorkflowBindingTreeNode BuildPublicData(IReadOnlyList<WorkflowBindingCandidate> candidates)
    {
        var root = new Builder("公共数据", WorkflowBindingTreeNodeKind.PublicDataGroup);
        foreach (var categoryGroup in candidates.GroupBy(item => item.Category ?? "公共数据", StringComparer.Ordinal))
        {
            var category = root.GetOrAdd(categoryGroup.Key, WorkflowBindingTreeNodeKind.PublicDataGroup);
            foreach (var variableGroup in categoryGroup.GroupBy(item => item.SourceNodeId, StringComparer.Ordinal))
            {
                var first = variableGroup.First();
                var variable = category.GetOrAdd($"{first.SourceDisplayName ?? first.SourceNodeId} [{first.SourceNodeId}]", WorkflowBindingTreeNodeKind.PublicData);
                foreach (var candidate in variableGroup) AddCandidate(variable, candidate);
            }
        }
        return root.Build(candidates.Select(item => $"{item.DisplayPath} {item.ValueType.FullName}"));
    }

    /// <summary>按照绑定路径的层级将候选项加入临时树。</summary>
    /// <param name="root">树的根构建节点。</param>
    /// <param name="candidate">绑定候选项。</param>
    private static void AddCandidate(Builder root, WorkflowBindingCandidate candidate)
    {
        var segments = candidate.MemberPath == "$"
            ? new[] { "(value)" }
            : candidate.MemberPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        for (var index = 0; index < segments.Length - 1; index++)
            current = current.GetOrAdd(segments[index], WorkflowBindingTreeNodeKind.Member);
        var leaf = segments.Length == 0 ? "(value)" : segments[^1];
        current.Children.Add(new Builder(
            $"{leaf} : {FriendlyType(candidate.ValueType)}",
            WorkflowBindingTreeNodeKind.Candidate,
            candidate));
    }

    /// <summary>构建指定来源节点的绑定树分支。</summary>
    /// <param name="sourceNodeId">数据来源节点标识。</param>
    /// <param name="candidates">候选项集合。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static WorkflowBindingTreeNode BuildSource(
        string sourceNodeId,
        IEnumerable<WorkflowBindingCandidate> candidates)
    {
        var sourceCandidates = candidates.ToArray();
        var displayPrefix = sourceCandidates
            .Select(item => item.DisplayPath.Split('/')[0])
            .FirstOrDefault() ?? sourceNodeId;
        var root = new Builder($"{displayPrefix} [{sourceNodeId}]", WorkflowBindingTreeNodeKind.SourceNode);
        foreach (var candidate in sourceCandidates) AddCandidate(root, candidate);
        return root.Build(sourceCandidates.Select(item => $"{item.DisplayPath} {item.ValueType.FullName}"));
    }

    /// <summary>生成适合界面显示的类型名称。</summary>
    /// <param name="type">目标 CLR 类型。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static string FriendlyType(Type type)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
            return $"{FriendlyType(nullable)}?";
        if (!type.IsGenericType)
            return type.Name;
        var name = type.Name[..type.Name.IndexOf('`')];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FriendlyType))}>";
    }

    /// <summary>用于逐层构造不可变绑定树的内部可变节点。</summary>
    private sealed class Builder
    {
        /// <summary>初始化绑定树的内部可变构建节点。</summary>
        /// <param name="label">界面显示文本。</param>
        /// <param name="kind">节点、页面或变更类型。</param>
        /// <param name="candidate">绑定候选项。</param>
        public Builder(string label, WorkflowBindingTreeNodeKind kind, WorkflowBindingCandidate? candidate = null)
        {
            Label = label;
            Kind = kind;
            Candidate = candidate;
        }

        /// <summary>获取构建节点的显示文本。</summary>
        public string Label { get; }
        /// <summary>获取绑定树节点种类。</summary>
        public WorkflowBindingTreeNodeKind Kind { get; }
        /// <summary>获取构建节点关联的绑定候选项。</summary>
        public WorkflowBindingCandidate? Candidate { get; }
        public List<Builder> Children { get; } = new();

        /// <summary>取得指定子节点；不存在时创建。</summary>
        /// <param name="label">界面显示文本。</param>
        /// <param name="kind">节点、页面或变更类型。</param>
        /// <returns>返回操作结果；具体含义参见方法说明。</returns>
        public Builder GetOrAdd(string label, WorkflowBindingTreeNodeKind kind)
        {
            var existing = Children.FirstOrDefault(item => item.Label == label && item.Kind == kind);
            if (existing is not null) return existing;
            var child = new Builder(label, kind);
            Children.Add(child);
            return child;
        }

        /// <summary>将临时构建节点转换为只读绑定树节点。</summary>
        /// <param name="sourceSearch">可选的来源搜索结果。</param>
        /// <returns>返回操作结果；具体含义参见方法说明。</returns>
        public WorkflowBindingTreeNode Build(IEnumerable<string>? sourceSearch = null)
        {
            var children = Children
                .OrderBy(item => item.Kind)
                .ThenBy(item => item.Label, StringComparer.Ordinal)
                .Select(item => item.Build())
                .ToArray();
            var search = string.Join(" ", new[] { Label }.Concat(sourceSearch ?? Array.Empty<string>()));
            return new WorkflowBindingTreeNode(Label, search, Kind, Candidate, children);
        }
    }
}
