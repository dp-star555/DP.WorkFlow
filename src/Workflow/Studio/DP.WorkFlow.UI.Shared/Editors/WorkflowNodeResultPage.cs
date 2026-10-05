using System.Collections;
using System.Globalization;
using System.Reflection;

namespace DP.WorkFlow.UI;

/// <summary>结果页中的一行只读信息。</summary>
/// <param name="Category">分组名称。</param>
/// <param name="Name">显示名称。</param>
/// <param name="Value">格式化后的值。</param>
public sealed record WorkflowNodeResultItem(string Category, string Name, string Value);

/// <summary>为每个节点提供通用“运行结果”页：最近一次执行状态与标准输出。</summary>
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
            PageId, "运行结果", WorkflowNodeEditorPageKind.Results, 900,
            new WorkflowNodeResultPageModel(context.RuntimeSession, context.Node.Id), "Results");
    }
}

/// <summary>
/// 运行结果页模型：从接收运行快照的原始会话读取节点最近一次执行信息和输出，只读且与 UI 技术无关。
/// </summary>
public sealed class WorkflowNodeResultPageModel : IDisposable
{
    /// <summary>运行状态分组名称。</summary>
    public const string StateCategory = "运行状态";
    /// <summary>输出分组名称。</summary>
    public const string OutputCategory = "输出";

    private const int MaximumOutputMembers = 64;
    private const int MaximumTextLength = 240;
    private readonly WorkflowDesignerSession? _session;
    private bool _disposed;

    /// <summary>为指定节点创建结果页模型。</summary>
    /// <param name="session">接收运行快照的设计会话；为空时只显示“尚未运行”。</param>
    /// <param name="nodeId">节点标识。</param>
    public WorkflowNodeResultPageModel(WorkflowDesignerSession? session, string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        _session = session;
        NodeId = nodeId;
        if (_session is not null) _session.Changed += OnSessionChanged;
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

        var output = _session!.GetLatestNodeOutput(NodeId);
        if (output is null)
        {
            items.Add(new(OutputCategory, "输出", "无输出记录"));
            return items;
        }
        AddOutput(items, output.Value);
        return items;
    }

    /// <summary>解除对设计会话的订阅。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_session is not null) _session.Changed -= OnSessionChanged;
    }

    /// <summary>将输出值展开为一层公开属性；简单值直接显示。</summary>
    public static void AddOutput(ICollection<WorkflowNodeResultItem> items, object? value)
    {
        if (value is null || IsScalar(value.GetType()) || value is IEnumerable and not IDictionary)
        {
            items.Add(new(OutputCategory, "值", FormatValue(value)));
            return;
        }
        var properties = value.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
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
            items.Add(new(OutputCategory, property.Name, text));
        }
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

    private static string StateText(E_NodeState state) => state switch
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
        if (e.Kind == WorkflowDesignerChangeKind.Runtime) Changed?.Invoke(this, EventArgs.Empty);
    }
}
