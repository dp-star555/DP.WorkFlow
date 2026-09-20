namespace DP.WorkFlow.UI;

/// <summary>表示父流程值到子流程输入的一行映射。</summary>
/// <param name="Index">集合中的索引。</param>
/// <param name="TargetVariableName">映射目标变量名称。</param>
/// <param name="Source">轨迹来源。</param>
/// <param name="LiteralValue">映射使用的常量值。</param>
/// <param name="ParentVariableName">父流程变量名称。</param>
/// <param name="ParentBinding">父流程绑定键。</param>
public sealed record WorkflowBlockInputMappingRow(
    int Index,
    string TargetVariableName,
    E_BlockInputSource Source,
    object? LiteralValue,
    string ParentVariableName,
    WorkflowBindingKey? ParentBinding);

/// <summary>表示子流程值到父流程输出的一行映射。</summary>
/// <param name="Index">集合中的索引。</param>
/// <param name="TargetVariableName">映射目标变量名称。</param>
/// <param name="Source">轨迹来源。</param>
/// <param name="ChildVariableName">子流程变量名称。</param>
/// <param name="ChildBinding">子流程绑定键。</param>
public sealed record WorkflowBlockOutputMappingRow(
    int Index,
    string TargetVariableName,
    E_BlockOutputSource Source,
    string ChildVariableName,
    WorkflowBindingKey? ChildBinding);

/// <summary>Block 输入/输出集合的 UI 无关可撤销编辑模型。</summary>
public sealed class WorkflowBlockMappingEditorModel : IDisposable
{
    private readonly WorkflowDesignerSession _session;
    private readonly string _blockNodeId;

    /// <summary>加载指定复合块节点的输入、输出映射并订阅会话变更。</summary>
    /// <param name="session">设计器会话。</param>
    /// <param name="blockNodeId">要编辑的复合块节点标识。</param>
    public WorkflowBlockMappingEditorModel(WorkflowDesignerSession session, string blockNodeId)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _blockNodeId = blockNodeId ?? throw new ArgumentNullException(nameof(blockNodeId));
        _ = Block;
        _session.Changed += OnSessionChanged;
    }

    public IWorkflowBlockMappingNode Block => _session.Document.Graph.Nodes
        .OfType<IWorkflowBlockMappingNode>()
        .FirstOrDefault(node => node.Id == _blockNodeId)
        ?? throw new InvalidOperationException($"当前画布不存在 Block 节点 {_blockNodeId}。");

    public IReadOnlyList<WorkflowBlockInputMappingRow> Inputs => Block.InputMappings
        .Select((mapping, index) => new WorkflowBlockInputMappingRow(
            index,
            mapping.TargetVariableName,
            mapping.Source,
            mapping.LiteralValue,
            mapping.ParentVariableName,
            mapping.ParentBinding))
        .ToArray();

    public IReadOnlyList<WorkflowBlockOutputMappingRow> Outputs => Block.OutputMappings
        .Select((mapping, index) => new WorkflowBlockOutputMappingRow(
            index,
            mapping.TargetVariableName,
            mapping.Source,
            mapping.ChildVariableName,
            mapping.ChildBinding))
        .ToArray();

    /// <summary>在模型内容发生变化、界面需要刷新时发生。</summary>
    public event EventHandler? Changed;

    /// <summary>新增一行子流程输入映射。</summary>
    public void AddInput()
    {
        var mapping = new BlockInputMapping
        {
            TargetVariableName = CreateUniqueName("Input", Block.InputMappings.Select(item => item.TargetVariableName)),
            Source = E_BlockInputSource.Literal,
            LiteralValue = string.Empty
        };
        var index = Block.InputMappings.Count;
        _session.ExecuteDocumentOperation(
            () => Block.InputMappings.Insert(Math.Min(index, Block.InputMappings.Count), mapping),
            () => Block.InputMappings.Remove(mapping));
    }

    /// <summary>替换指定索引的子流程输入映射。</summary>
    /// <param name="index">目标元素索引。</param>
    /// <param name="value">要校验、转换或写入的值。</param>
    public void ReplaceInput(int index, WorkflowBlockInputMappingRow value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (index < 0 || index >= Block.InputMappings.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        ValidateName(value.TargetVariableName, nameof(value.TargetVariableName));
        var old = Block.InputMappings[index];
        var replacement = new BlockInputMapping
        {
            TargetVariableName = value.TargetVariableName.Trim(),
            Source = value.Source,
            LiteralValue = value.LiteralValue,
            ParentVariableName = value.ParentVariableName?.Trim() ?? string.Empty,
            ParentBinding = value.ParentBinding
        };
        _session.ExecuteDocumentOperation(
            () => Block.InputMappings[index] = replacement,
            () => Block.InputMappings[index] = old);
    }

    /// <summary>删除指定索引的子流程输入映射。</summary>
    /// <param name="index">目标元素索引。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    public bool RemoveInput(int index)
    {
        if (index < 0 || index >= Block.InputMappings.Count)
            return false;
        var mapping = Block.InputMappings[index];
        _session.ExecuteDocumentOperation(
            () => Block.InputMappings.RemoveAt(index),
            () => Block.InputMappings.Insert(Math.Min(index, Block.InputMappings.Count), mapping));
        return true;
    }

    /// <summary>新增一行子流程输出映射。</summary>
    public void AddOutput()
    {
        var mapping = new BlockOutputMapping
        {
            TargetVariableName = CreateUniqueName("Output", Block.OutputMappings.Select(item => item.TargetVariableName)),
            Source = E_BlockOutputSource.ChildVariable,
            ChildVariableName = "Result"
        };
        var index = Block.OutputMappings.Count;
        _session.ExecuteDocumentOperation(
            () => Block.OutputMappings.Insert(Math.Min(index, Block.OutputMappings.Count), mapping),
            () => Block.OutputMappings.Remove(mapping));
    }

    /// <summary>替换指定索引的子流程输出映射。</summary>
    /// <param name="index">目标元素索引。</param>
    /// <param name="value">要校验、转换或写入的值。</param>
    public void ReplaceOutput(int index, WorkflowBlockOutputMappingRow value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (index < 0 || index >= Block.OutputMappings.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        ValidateName(value.TargetVariableName, nameof(value.TargetVariableName));
        var old = Block.OutputMappings[index];
        var replacement = new BlockOutputMapping
        {
            TargetVariableName = value.TargetVariableName.Trim(),
            Source = value.Source,
            ChildVariableName = value.ChildVariableName?.Trim() ?? string.Empty,
            ChildBinding = value.ChildBinding
        };
        _session.ExecuteDocumentOperation(
            () => Block.OutputMappings[index] = replacement,
            () => Block.OutputMappings[index] = old);
    }

    /// <summary>删除指定索引的子流程输出映射。</summary>
    /// <param name="index">目标元素索引。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    public bool RemoveOutput(int index)
    {
        if (index < 0 || index >= Block.OutputMappings.Count)
            return false;
        var mapping = Block.OutputMappings[index];
        _session.ExecuteDocumentOperation(
            () => Block.OutputMappings.RemoveAt(index),
            () => Block.OutputMappings.Insert(Math.Min(index, Block.OutputMappings.Count), mapping));
        return true;
    }

    /// <summary>以一次 Undo 操作替换全部输入输出映射。</summary>
    /// <param name="inputs">输入映射集合。</param>
    /// <param name="outputs">输出映射集合。</param>
    public void ReplaceAll(
        IEnumerable<WorkflowBlockInputMappingRow> inputs,
        IEnumerable<WorkflowBlockOutputMappingRow> outputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(outputs);
        var inputRows = inputs.ToArray();
        var outputRows = outputs.ToArray();
        ValidateUniqueTargets(inputRows.Select(item => item.TargetVariableName), "输入");
        ValidateUniqueTargets(outputRows.Select(item => item.TargetVariableName), "输出");
        var oldInputs = Block.InputMappings.ToArray();
        var oldOutputs = Block.OutputMappings.ToArray();
        var newInputs = inputRows.Select(item => new BlockInputMapping
        {
            TargetVariableName = item.TargetVariableName.Trim(),
            Source = item.Source,
            LiteralValue = item.LiteralValue,
            ParentVariableName = item.ParentVariableName?.Trim() ?? string.Empty,
            ParentBinding = item.ParentBinding
        }).ToArray();
        var newOutputs = outputRows.Select(item => new BlockOutputMapping
        {
            TargetVariableName = item.TargetVariableName.Trim(),
            Source = item.Source,
            ChildVariableName = item.ChildVariableName?.Trim() ?? string.Empty,
            ChildBinding = item.ChildBinding
        }).ToArray();
        _session.ExecuteDocumentOperation(
            () => ReplaceCollections(newInputs, newOutputs),
            () => ReplaceCollections(oldInputs, oldOutputs));
    }

    /// <summary>获取当前父画布中在 Block 前可用的候选。</summary>
    /// <param name="startNodeId">工作流开始节点标识。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    public IReadOnlyList<WorkflowBindingCandidate> GetParentCandidates(string startNodeId) =>
        new WorkflowBindingAnalyzer(_session.Catalog)
            .Analyze(_session.Document, startNodeId)
            .GetCandidates(Block.Id, typeof(object));

    /// <summary>获取子画布中所有具有静态输出 Schema 的候选。</summary>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    public IReadOnlyList<WorkflowBindingCandidate> GetChildCandidates() =>
        new WorkflowBindingAnalyzer(_session.Catalog).GetOutputCandidates(Block.SubDocument);

    /// <summary>解除事件订阅并释放当前模型持有的资源。</summary>
    public void Dispose() => _session.Changed -= OnSessionChanged;

    /// <summary>处理设计会话变更，并同步刷新派生模型。</summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="e">事件参数。</param>
    private void OnSessionChanged(object? sender, WorkflowDesignerChangedEventArgs e)
    {
        if (e.Kind == WorkflowDesignerChangeKind.Document)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>生成在现有名称集合中不重复的名称。</summary>
    /// <param name="prefix">名称前缀。</param>
    /// <param name="existingNames">已经占用的名称集合。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static string CreateUniqueName(string prefix, IEnumerable<string> existingNames)
    {
        var existing = existingNames.ToHashSet(StringComparer.Ordinal);
        var index = 1;
        string candidate;
        do candidate = $"{prefix}{index++}";
        while (existing.Contains(candidate));
        return candidate;
    }

    /// <summary>将映射行写回块节点的输入、输出映射集合。</summary>
    /// <param name="inputs">输入映射集合。</param>
    /// <param name="outputs">输出映射集合。</param>
    private void ReplaceCollections(
        IEnumerable<BlockInputMapping> inputs,
        IEnumerable<BlockOutputMapping> outputs)
    {
        Block.InputMappings.Clear();
        foreach (var input in inputs) Block.InputMappings.Add(input);
        Block.OutputMappings.Clear();
        foreach (var output in outputs) Block.OutputMappings.Add(output);
    }

    /// <summary>检查映射目标名称是否为空且不存在重复。</summary>
    /// <param name="names">待校验的名称集合。</param>
    /// <param name="category">映射类别名称。</param>
    private static void ValidateUniqueTargets(IEnumerable<string> names, string category)
    {
        var values = names.ToArray();
        foreach (var value in values) ValidateName(value, category);
        var duplicate = values.GroupBy(value => value, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"{category}映射目标变量重复：{duplicate.Key}。");
    }

    /// <summary>检查名称参数是否为有效的非空名称。</summary>
    /// <param name="value">要校验、转换或写入的值。</param>
    /// <param name="parameterName">异常中使用的参数名。</param>
    private static void ValidateName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("映射变量名不能为空。", parameterName);
    }
}
