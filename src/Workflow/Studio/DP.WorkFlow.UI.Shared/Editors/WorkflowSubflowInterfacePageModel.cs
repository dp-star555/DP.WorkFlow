namespace DP.WorkFlow.UI;

/// <summary>“输入输出”页中的一行输入。</summary>
/// <param name="Index">在节点输入列表中的位置。</param>
/// <param name="Name">输入名称。</param>
/// <param name="Source">父画布中的来源；为空表示手动值。</param>
/// <param name="SourceText">来源的显示文本。</param>
/// <param name="TypeText">类型的显示文本；绑定来源时跟随来源。</param>
/// <param name="ValueType">手动值的类型。</param>
/// <param name="ValueText">手动值的显示文本。</param>
public sealed record WorkflowSubflowInputRow(
    int Index,
    string Name,
    WorkflowBindingKey? Source,
    string SourceText,
    string TypeText,
    E_WorkflowSubflowValueType ValueType,
    string ValueText)
{
    /// <summary>是否绑定了父画布中的来源；绑定时类型跟随来源，不能手动修改。</summary>
    public bool IsBound => Source.HasValue;
}

/// <summary>“输入输出”页中的一行输出。</summary>
/// <param name="Index">在节点输出列表中的位置。</param>
/// <param name="Name">输出名称。</param>
/// <param name="Source">子画布中的来源。</param>
/// <param name="SourceText">来源的显示文本。</param>
/// <param name="TypeText">类型的显示文本，跟随来源。</param>
public sealed record WorkflowSubflowOutputRow(
    int Index,
    string Name,
    WorkflowBindingKey? Source,
    string SourceText,
    string TypeText);

/// <summary>
/// 节点窗口“输入输出”页：编辑复合节点子流程接口的草稿，随节点窗口“应用/确定”提交，取消则丢弃。
/// 输入的来源是父画布中复合节点之前的节点输出，或者手动值（整数、小数、布尔、文本）；
/// 输出的来源是子画布中的节点输出。绑定来源的类型自动跟随来源。
/// </summary>
public sealed class WorkflowSubflowInterfacePageModel : IDisposable
{
    private readonly WorkflowDesignerSession _session;
    private readonly string _entryNodeId;
    private readonly WorkflowSubWorkflowEditorPageModel? _subWorkflow;
    private bool _notifying;

    /// <summary>创建输入输出页模型。</summary>
    /// <param name="session">节点窗口的编辑副本会话（父画布）。</param>
    /// <param name="entryNodeId">父画布入口节点，用于计算可选的输入来源。</param>
    /// <param name="node">编辑副本中的复合节点。</param>
    /// <param name="subWorkflow">同一窗口的子画布页；接口变化后通知它刷新启动节点成员和诊断。</param>
    public WorkflowSubflowInterfacePageModel(
        WorkflowDesignerSession session,
        string entryNodeId,
        IWorkflowSubflowNode node,
        WorkflowSubWorkflowEditorPageModel? subWorkflow)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _entryNodeId = entryNodeId ?? throw new ArgumentNullException(nameof(entryNodeId));
        Node = node ?? throw new ArgumentNullException(nameof(node));
        _subWorkflow = subWorkflow;
        _session.Changed += OnSessionChanged;
        if (_subWorkflow is not null)
            _subWorkflow.Session.Changed += OnSessionChanged;
    }

    /// <summary>编辑副本中的复合节点。</summary>
    public IWorkflowSubflowNode Node { get; }

    /// <summary>接口或其中类型变化、界面需要刷新时发生。</summary>
    public event EventHandler? Changed;

    /// <summary>当前全部输入行。</summary>
    public IReadOnlyList<WorkflowSubflowInputRow> Inputs
    {
        get
        {
            var types = WorkflowSubflowInterfaces.DescribeInputs(Node, _session.Document, _session.Catalog)
                .ToDictionary(member => member.Path, member => member.Type, StringComparer.Ordinal);
            return Node.Inputs.Select((input, index) => new WorkflowSubflowInputRow(
                index,
                input.Name,
                input.Source,
                input.Source is { } source ? DescribeSource(source, _session.Document) : "手动值",
                input.Source is null
                    ? WorkflowSubflowInput.DisplayName(input.ValueType)
                    : types.TryGetValue(input.Name.Trim(), out var type) && type != typeof(object) ? TypeName(type) : "未知（来源无效）",
                input.ValueType,
                input.Source is null ? FormatValue(input) : string.Empty)).ToArray();
        }
    }

    /// <summary>当前全部输出行。</summary>
    public IReadOnlyList<WorkflowSubflowOutputRow> Outputs
    {
        get
        {
            var types = WorkflowSubflowInterfaces.DescribeOutputs(Node, _session.Catalog)
                .ToDictionary(member => member.Path, member => member.Type, StringComparer.Ordinal);
            return Node.Outputs.Select((output, index) => new WorkflowSubflowOutputRow(
                index,
                output.Name,
                output.Source,
                output.Source is { } source ? DescribeSource(source, Node.SubDocument) : "未选择",
                output.Source is null
                    ? "—"
                    : types.TryGetValue(output.Name.Trim(), out var type) && type != typeof(object) ? TypeName(type) : "未知（来源无效）")).ToArray();
        }
    }

    /// <summary>可作为输入来源的父画布节点输出：保证在复合节点之前执行的节点。</summary>
    public IReadOnlyList<WorkflowBindingCandidate> GetInputCandidates()
    {
        try
        {
            return new WorkflowBindingAnalyzer(_session.Catalog)
                .Analyze(_session.Document, _entryNodeId)
                .GetCandidates(Node.Id, typeof(object));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Array.Empty<WorkflowBindingCandidate>();
        }
    }

    /// <summary>可作为输出来源的子画布节点输出。</summary>
    public IReadOnlyList<WorkflowBindingCandidate> GetOutputCandidates() =>
        new WorkflowBindingAnalyzer(_session.Catalog).GetOutputCandidates(Node.SubDocument);

    /// <summary>新增一项手动整数输入。</summary>
    public void AddInput()
    {
        Node.Inputs.Add(new WorkflowSubflowInput
        {
            Name = UniqueName("输入", Node.Inputs.Select(input => input.Name)),
            ValueType = E_WorkflowSubflowValueType.Integer,
            Value = 0
        });
        Commit();
    }

    /// <summary>删除一项输入。</summary>
    /// <param name="index">输入位置。</param>
    public void RemoveInput(int index)
    {
        Node.Inputs.RemoveAt(index);
        Commit();
    }

    /// <summary>修改输入名称；子画布中按新名称读取。</summary>
    /// <param name="index">输入位置。</param>
    /// <param name="name">新名称。</param>
    public void RenameInput(int index, string name)
    {
        if (Node.Inputs[index].Name == name) return;
        Node.Inputs[index].Name = name.Trim();
        Commit();
    }

    /// <summary>设置输入来源；为空时改为手动值。</summary>
    /// <param name="index">输入位置。</param>
    /// <param name="source">父画布中的来源。</param>
    public void SetInputSource(int index, WorkflowBindingKey? source)
    {
        var input = Node.Inputs[index];
        input.Source = source;
        if (source is null && !input.TryGetManualValue(out _, out _))
            input.Value = DefaultValue(input.ValueType);
        Commit();
    }

    /// <summary>设置手动值的类型；当前值无法转换时改为该类型的默认值。</summary>
    /// <param name="index">输入位置。</param>
    /// <param name="valueType">手动值类型。</param>
    public void SetInputValueType(int index, E_WorkflowSubflowValueType valueType)
    {
        var input = Node.Inputs[index];
        if (input.ValueType == valueType) return;
        input.ValueType = valueType;
        input.Value = input.TryGetManualValue(out var converted, out _) ? converted : DefaultValue(valueType);
        Commit();
    }

    /// <summary>设置手动值；无法转换时保留原文，由诊断提示。</summary>
    /// <param name="index">输入位置。</param>
    /// <param name="text">输入的文本。</param>
    public void SetInputValue(int index, string text)
    {
        var input = Node.Inputs[index];
        input.Value = text;
        if (input.TryGetManualValue(out var converted, out _))
            input.Value = converted;
        Commit();
    }

    /// <summary>新增一项输出。</summary>
    public void AddOutput()
    {
        Node.Outputs.Add(new WorkflowSubflowOutput { Name = UniqueName("输出", Node.Outputs.Select(output => output.Name)) });
        Commit();
    }

    /// <summary>删除一项输出。</summary>
    /// <param name="index">输出位置。</param>
    public void RemoveOutput(int index)
    {
        Node.Outputs.RemoveAt(index);
        Commit();
    }

    /// <summary>修改输出名称；父画布中按新名称读取。</summary>
    /// <param name="index">输出位置。</param>
    /// <param name="name">新名称。</param>
    public void RenameOutput(int index, string name)
    {
        if (Node.Outputs[index].Name == name) return;
        Node.Outputs[index].Name = name.Trim();
        Commit();
    }

    /// <summary>设置输出来源。</summary>
    /// <param name="index">输出位置。</param>
    /// <param name="source">子画布中的来源。</param>
    public void SetOutputSource(int index, WorkflowBindingKey? source)
    {
        Node.Outputs[index].Source = source;
        Commit();
    }

    /// <summary>解除事件订阅。</summary>
    public void Dispose()
    {
        _session.Changed -= OnSessionChanged;
        if (_subWorkflow is not null)
            _subWorkflow.Session.Changed -= OnSessionChanged;
    }

    /// <summary>
    /// 接口修改后：按父画布重新解析类型并注入子画布启动节点，再通知父画布和子画布刷新
    /// （绑定候选、数据端口、诊断）。
    /// </summary>
    private void Commit()
    {
        _notifying = true;
        try
        {
            WorkflowSubflowInterfaces.Refresh(_session.Document, _session.Catalog);
            _session.NotifyNodeConfigurationChanged();
            _subWorkflow?.Session.NotifyNodeConfigurationChanged();
        }
        finally
        {
            _notifying = false;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>父画布或子画布变化可能改变来源类型，刷新显示。</summary>
    private void OnSessionChanged(object? sender, WorkflowDesignerChangedEventArgs e)
    {
        if (_notifying || e.Kind != WorkflowDesignerChangeKind.Document)
            return;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string DescribeSource(WorkflowBindingKey source, WorkflowDocument document)
    {
        var node = document.Graph.Nodes.FirstOrDefault(item => string.Equals(item.Id, source.NodeId, StringComparison.Ordinal));
        var nodeName = node is null ? $"{source.NodeId}（不存在）" : string.IsNullOrWhiteSpace(node.Title) ? node.Id : node.Title;
        return source.MemberPath == "$" ? nodeName : $"{nodeName}.{source.MemberPath}";
    }

    private static string FormatValue(WorkflowSubflowInput input) =>
        input.TryGetManualValue(out var value, out _)
            ? value switch
            {
                bool flag => flag ? "true" : "false",
                IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => value?.ToString() ?? string.Empty
            }
            : input.Value?.ToString() ?? string.Empty;

    private static object DefaultValue(E_WorkflowSubflowValueType valueType) => valueType switch
    {
        E_WorkflowSubflowValueType.Integer => 0,
        E_WorkflowSubflowValueType.Number => 0d,
        E_WorkflowSubflowValueType.Boolean => false,
        _ => string.Empty
    };

    private static string TypeName(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(int)) return "整数";
        if (type == typeof(long)) return "长整数";
        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal)) return "小数";
        if (type == typeof(bool)) return "布尔";
        if (type == typeof(string)) return "文本";
        return type.Name;
    }

    private static string UniqueName(string prefix, IEnumerable<string> existing)
    {
        var names = existing.ToHashSet(StringComparer.Ordinal);
        for (var index = 1; ; index++)
        {
            var candidate = prefix + index;
            if (!names.Contains(candidate))
                return candidate;
        }
    }
}
