using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace DP.WorkFlow.UI;

/// <summary>结果页中的一行只读信息。</summary>
/// <param name="Category">分组名称。</param>
/// <param name="Name">显示名称。</param>
/// <param name="Value">格式化后的值。</param>
public sealed record WorkflowNodeResultItem(string Category, string Name, string Value);

/// <summary>结果页中的一个标准输出成员：未运行时值为空占位。下游节点通过参数“选来源”引用这些成员。</summary>
/// <param name="Name">成员路径；标准输出为简单值时为 <c>$</c>。</param>
/// <param name="DisplayName">中文显示名称。</param>
/// <param name="Value">最近一次运行的格式化值；尚未运行时为 <see cref="WorkflowNodeResultPageModel.PendingValue"/>。</param>
public sealed record WorkflowNodeResultMember(string Name, string DisplayName, string Value);

/// <summary>为每个节点提供通用“输出”页：多输出节点的输出端口开关、最近一次执行状态与标准输出。</summary>
public sealed class WorkflowNodeResultPageProvider : IWorkflowNodeEditorPageProvider
{
    /// <summary>结果页稳定标识。</summary>
    public const string PageId = "Results";

    /// <inheritdoc />
    public string ExtensionId => "Workflow.BuiltIn.Results";

    /// <inheritdoc />
    public bool CanProvide(WorkflowNodeEditorContext context) => context.RequestedPropertyEditor is null;

    /// <inheritdoc />
    public IEnumerable<WorkflowNodeEditorPageDescriptor> CreatePages(WorkflowNodeEditorContext context)
    {
        yield return new WorkflowNodeEditorPageDescriptor(
            PageId, "输出", WorkflowNodeEditorPageKind.Results, 900,
            new WorkflowNodeResultPageModel(context.RuntimeSession, context.Node.Id, context.Session), "Results");
    }
}

/// <summary>
/// 输出页模型：输出端口开关写在编辑会话上；运行状态和输出从接收运行快照的原始会话读取，与 UI 技术无关。
/// </summary>
public sealed class WorkflowNodeResultPageModel : IDisposable
{
    /// <summary>运行状态分组名称。</summary>
    public const string StateCategory = "运行状态";
    /// <summary>输出分组名称。</summary>
    public const string OutputCategory = "输出";
    /// <summary>尚未运行时输出成员的占位值。</summary>
    public const string PendingValue = "—";

    private const int MaximumOutputMembers = 64;
    private const int MaximumTextLength = 240;
    private readonly WorkflowDesignerSession? _session;
    private readonly WorkflowDesignerSession? _portSession;
    private bool _disposed;

    /// <summary>为指定节点创建结果页模型。</summary>
    /// <param name="session">接收运行快照的设计会话；为空时只显示“尚未运行”。</param>
    /// <param name="nodeId">节点标识。</param>
    /// <param name="portSession">编辑输出端口开关的会话（节点窗口里是隔离编辑副本，确定后才提交）；为空时不显示开关。</param>
    public WorkflowNodeResultPageModel(WorkflowDesignerSession? session, string nodeId, WorkflowDesignerSession? portSession = null)
    {
        Guard.NotNullOrWhiteSpace(nodeId);
        _session = session;
        _portSession = portSession;
        NodeId = nodeId;
        if (_session is not null) _session.Changed += OnSessionChanged;
        if (_portSession is not null && !ReferenceEquals(_portSession, _session)) _portSession.Changed += OnSessionChanged;
    }

    /// <summary>节点标识。</summary>
    public string NodeId { get; }

    /// <summary>运行快照变化时发生；平台页面据此刷新显示。</summary>
    public event EventHandler? Changed;

    /// <summary>按“运行状态、输出”顺序生成当前结果行。</summary>
    public IReadOnlyList<WorkflowNodeResultItem> GetItems()
    {
        var items = new List<WorkflowNodeResultItem>();
        var info = _session?.GetNodeRuntimeInfo(NodeId);
        if (info is null)
        {
            items.Add(new(StateCategory, "状态", "尚未运行"));
            return items;
        }
        items.Add(new(StateCategory, "状态", StateText(info.State)));
        items.Add(new(StateCategory, "执行次数", info.ExecutionCount.ToString(CultureInfo.InvariantCulture)));
        items.Add(new(StateCategory, "执行序号", info.ExecutionSequence.ToString(CultureInfo.InvariantCulture)));
        if (info.StartedAt is { } started) items.Add(new(StateCategory, "开始时间", FormatTime(started)));
        if (info.CompletedAt is { } completed) items.Add(new(StateCategory, "完成时间", FormatTime(completed)));
        items.Add(new(StateCategory, "耗时", WorkflowDesignerInteraction.FormatElapsed(info.Elapsed)));
        if (!string.IsNullOrWhiteSpace(info.Message)) items.Add(new(StateCategory, "消息", info.Message!));
        AddChildProgress(items);

        // 已声明输出类型的节点由 GetMembers 列出全部成员；这里只展开未声明类型的实际输出。
        if ((_portSession ?? _session)!.GetOutputMembers(NodeId).Count > 0) return items;
        var output = _session!.GetLatestNodeOutput(NodeId);
        if (output is null)
        {
            items.Add(new(OutputCategory, "输出", "无输出记录"));
            return items;
        }
        AddOutput(items, output.Value);
        return items;
    }

    /// <summary>复合节点（子流程、循环块）的子运行进度，随状态一起显示：当前圈、正在执行的内部节点和本圈耗时。</summary>
    private void AddChildProgress(ICollection<WorkflowNodeResultItem> items)
    {
        var child = _session?.RuntimeSnapshot is { } snapshot
            ? snapshot.ActiveChildWorkflows.Values.Where(info => info.ParentNodeId == NodeId)
                  .OrderByDescending(info => info.Snapshot.Sequence).FirstOrDefault()
              ?? (snapshot.LatestChildWorkflowByParentNode.TryGetValue(NodeId, out var latest) ? latest : null)
            : null;
        if (child is null) return;
        if (child.Cycle > 0) items.Add(new(StateCategory, "当前圈", child.Cycle.ToString(CultureInfo.InvariantCulture)));
        if (child.Snapshot.ExecutionState is E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused
            && !string.IsNullOrEmpty(child.Snapshot.CurrentNodeId))
            items.Add(new(StateCategory, "正在执行", child.Snapshot.CurrentNodeId!));
        items.Add(new(StateCategory, child.Cycle > 0 ? "本圈耗时" : "子流程耗时",
            WorkflowDesignerInteraction.FormatElapsed(child.Snapshot.TotalElapsed)));
    }

    /// <summary>
    /// 按输出类型声明顺序列出节点标准输出的全部成员。尚未运行时也全部列出，值显示为占位符；
    /// 输出类型未声明时返回空集合，结果改由 <see cref="GetItems"/> 展开实际输出。
    /// </summary>
    public IReadOnlyList<WorkflowNodeResultMember> GetMembers()
    {
        var session = _portSession ?? _session;
        if (session is null) return Array.Empty<WorkflowNodeResultMember>();
        var members = session.GetOutputMembers(NodeId);
        if (members.Count == 0) return Array.Empty<WorkflowNodeResultMember>();
        var hasOutput = _session?.GetLatestNodeOutput(NodeId) is not null;
        var output = _session?.GetLatestNodeOutput(NodeId)?.Value;
        return members.Select(member => new WorkflowNodeResultMember(
            member.Name,
            member.DisplayName,
            hasOutput ? ReadMember(output, member.Name) : PendingValue)).ToArray();
    }

    /// <summary>多输出节点的输出端口及是否显示；只有一个输出或没有编辑会话时为空。</summary>
    public IReadOnlyList<(WorkflowPortDescriptor Port, bool Visible)> GetOutputPorts()
    {
        if (_portSession?.Document.Nodes.FirstOrDefault(node => node.Node.Id == NodeId) is not { } canvasNode)
            return Array.Empty<(WorkflowPortDescriptor, bool)>();
        var outputs = _portSession.GetDeclaredPorts(NodeId, WorkflowPortDirection.Output);
        return outputs.Count <= 1
            ? Array.Empty<(WorkflowPortDescriptor, bool)>()
            : outputs.Select(port => (port, !canvasNode.IsOutputPortHidden(port))).ToArray();
    }

    /// <summary>显示或隐藏一个输出端口；改不了（例如只剩一个显示的输出）时返回 <see langword="false"/>。</summary>
    public bool SetOutputPortVisible(string portKey, bool visible) =>
        _portSession is not null && _portSession.SetOutputPortVisible(NodeId, portKey, visible);

    private static string ReadMember(object? output, string name)
    {
        if (name == "$" || output is null) return FormatValue(output);
        try
        {
            var property = output.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            return property is null ? PendingValue : FormatValue(property.GetValue(output));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return "读取失败：" + (exception.InnerException ?? exception).Message;
        }
    }

    /// <summary>解除对设计会话的订阅。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_session is not null) _session.Changed -= OnSessionChanged;
        if (_portSession is not null) _portSession.Changed -= OnSessionChanged;
    }

    /// <summary>将输出值展开为一层公开属性，名称取自属性特性或节点包登记的中文名称；简单值直接显示。标记为不可浏览的诊断成员不显示，但仍可绑定。</summary>
    public static void AddOutput(ICollection<WorkflowNodeResultItem> items, object? value)
    {
        if (value is null || IsScalar(value.GetType()) || value is IEnumerable and not IDictionary)
        {
            items.Add(new(OutputCategory, "值", FormatValue(value)));
            return;
        }
        var properties = value.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0
                && property.GetCustomAttribute<BrowsableAttribute>()?.Browsable != false)
            .Take(MaximumOutputMembers)
            .ToArray();
        if (properties.Length == 0)
        {
            items.Add(new(OutputCategory, "值", FormatValue(value)));
            return;
        }
        foreach (var property in properties)
        {
            string text;
            try { text = FormatValue(property.GetValue(value)); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                text = "读取失败：" + (exception.InnerException ?? exception).Message;
            }
            items.Add(new(OutputCategory, WorkflowOutputDisplayNames.Resolve(property), text));
        }
        if (value is IWorkflowResultRows rows)
            foreach (var row in rows.ResultRows.Take(MaximumOutputMembers))
                items.Add(new(OutputCategory, row.Key, FormatValue(row.Value)));
    }

    /// <summary>将任意值格式化为单行只读文本，过长时截断。</summary>
    public static string FormatValue(object? value)
    {
        var text = FormatRaw(value).ReplaceLineEndings(" ");
        return text.Length <= MaximumTextLength ? text : text[..MaximumTextLength] + "…";
    }

    private static string FormatRaw(object? value) => value switch
    {
        null => "（空）",
        string text => text,
        bool flag => flag ? "True" : "False",
        IFormattable formattable when IsScalar(value.GetType()) => formattable.ToString(null, CultureInfo.InvariantCulture),
        ICollection collection => $"{collection.Count} 项",
        IEnumerable sequence => $"{sequence.Cast<object?>().Take(10_001).Count() switch { > 10_000 => "10000+", var count => count.ToString(CultureInfo.InvariantCulture) }} 项",
        _ => HasOwnToString(value.GetType()) ? value.ToString() ?? value.GetType().Name : value.GetType().Name
    };

    private static bool IsScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(Guid);
    }

    private static bool HasOwnToString(Type type) =>
        type.GetMethod(nameof(ToString), Type.EmptyTypes)?.DeclaringType is { } declaring && declaring != typeof(object)
        && declaring != typeof(ValueType);

    /// <summary>节点运行状态的显示文字。</summary>
    /// <param name="state">节点状态。</param>
    public static string StateText(E_NodeState state) => state switch
    {
        E_NodeState.Idle => "未执行",
        E_NodeState.Running => "运行中",
        E_NodeState.Completed => "已完成",
        E_NodeState.Failed => "失败",
        E_NodeState.Canceled => "已取消",
        _ => state.ToString()
    };

    private static string FormatTime(DateTimeOffset time) =>
        time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

    private void OnSessionChanged(object? sender, WorkflowDesignerChangedEventArgs e)
    {
        if (e.Kind is WorkflowDesignerChangeKind.Runtime or WorkflowDesignerChangeKind.Document) Changed?.Invoke(this, EventArgs.Empty);
    }
}
