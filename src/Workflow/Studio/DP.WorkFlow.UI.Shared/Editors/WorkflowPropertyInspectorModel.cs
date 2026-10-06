using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace DP.WorkFlow.UI;

/// <summary>指定属性面板应使用的编辑器类型。</summary>
public enum WorkflowPropertyEditorKind
{
    /// <summary>单行或多行文本编辑器。</summary>
    Text,
    /// <summary>带数值转换和校验的编辑器。</summary>
    Number,
    /// <summary>布尔开关编辑器。</summary>
    Boolean,
    /// <summary>枚举选项编辑器。</summary>
    Enum,
    /// <summary>支持常量值与数据绑定切换的工作流输入编辑器。</summary>
    WorkflowInput,
    /// <summary>C# 脚本专用编辑器。</summary>
    Script,
    /// <summary>集合、字典或复杂对象的结构化编辑器。</summary>
    Structured,
    /// <summary>从宿主提供的候选集中选择，不允许自由文本。</summary>
    Choice,
    /// <summary>请求宿主打开由稳定编辑器键标识的事务式编辑窗口。</summary>
    Action,
    /// <summary>仅用于显示、不允许修改的属性。</summary>
    ReadOnly
}

/// <summary>属性编辑器的一个候选值；<paramref name="Label"/> 只用于显示，<paramref name="Value"/> 才是提交给属性的实际值。</summary>
/// <param name="Label">面向操作员的显示文本。</param>
/// <param name="Value">提交给属性的实际值。</param>
public sealed record WorkflowPropertyChoice(string Label, object? Value)
{
    /// <inheritdoc/>
    public override string ToString() => Label;
}

/// <summary>
/// 按属性的专用编辑器键和内部名称提供候选值。
/// 宿主用它把机器配置（例如已发布的逻辑图像源）注入属性面板，而共享层不需要认识具体领域类型。
/// </summary>
/// <param name="editorKey">属性声明的专用编辑器键。</param>
/// <param name="propertyName">属性的 CLR 名称。</param>
/// <returns>候选值；返回空集合表示没有可选项，编辑器退回文本输入。</returns>
public delegate IReadOnlyList<WorkflowPropertyChoice> WorkflowPropertyChoiceProvider(string editorKey, string propertyName);

/// <summary>专用属性操作请求；宿主按键寻找页面，不要求共享属性表认识领域模型。</summary>
public sealed record WorkflowPropertyActionRequest(string NodeId, string EditorKey);

/// <summary>表示一个可由 WinForms/WPF 属性面板共同消费的节点属性。</summary>
public sealed class WorkflowPropertyEntry
{
    private readonly object _owner;
    private readonly PropertyInfo _property;
    private Func<object?>? _read;
    private Action<object?>? _write;
    private Func<Task>? _action;
    private Func<string>? _actionBlockReason;
    private bool _displayRadiansAsDegrees;

    /// <summary>初始化属性的反射访问、显示元数据和编辑器配置。</summary>
    /// <param name="owner">属性所属对象。</param>
    /// <param name="property">反射属性信息。</param>
    /// <param name="displayName">属性显示名。</param>
    /// <param name="category">映射类别名称。</param>
    /// <param name="description">属性说明。</param>
    /// <param name="editorKind">属性编辑器类型。</param>
    /// <param name="valueType">编辑器直接处理的值类型。</param>
    /// <param name="workflowInputType">工作流输入包装的值类型。</param>
    /// <param name="propertyEditor">属性声明的可选自定义编辑器元数据。</param>
    /// <param name="choices">候选编辑器可选项；非候选编辑器为空。</param>
    internal WorkflowPropertyEntry(
        object owner,
        PropertyInfo property,
        string displayName,
        string category,
        string description,
        WorkflowPropertyEditorKind editorKind,
        Type valueType,
        Type? workflowInputType = null,
        WorkflowPropertyEditorAttribute? propertyEditor = null,
        IReadOnlyList<WorkflowPropertyChoice>? choices = null)
    {
        _owner = owner;
        _property = property;
        Name = property.Name;
        DisplayName = displayName;
        Category = category;
        Description = description;
        EditorKind = editorKind;
        ValueType = valueType;
        WorkflowInputType = workflowInputType;
        EditorKey = propertyEditor?.EditorKey;
        EditorFilter = propertyEditor?.Filter;
        EditorDialogTitle = propertyEditor?.DialogTitle;
        EditorCheckExists = propertyEditor?.CheckExists == true;
        Choices = choices ?? Array.Empty<WorkflowPropertyChoice>();
    }

    /// <summary>获取 CLR 属性名称。</summary>
    public string Name { get; private set; }

    /// <summary>创建由领域描述驱动的属性，仍复用标量转换及宿主的撤销/重做。</summary>
    /// <param name="name">稳定属性身份。</param>
    /// <param name="displayName">显示名。</param>
    /// <param name="category">分组。</param>
    /// <param name="description">说明。</param>
    /// <param name="kind">编辑器类型。</param>
    /// <param name="valueType">标量类型。</param>
    /// <param name="read">读取当前配置。</param>
    /// <param name="write">写入已经转换过类型的值。</param>
    /// <param name="editor">可选文件等专用编辑器。</param>
    /// <returns>可提交的动态属性。</returns>
    public static WorkflowPropertyEntry Create(string name, string displayName, string category, string description,
        WorkflowPropertyEditorKind kind, Type valueType, Func<object?> read, Action<object?> write, WorkflowPropertyEditorAttribute? editor = null)
    {
        var access = new DynamicValue(read, write);
        return new WorkflowPropertyEntry(access, typeof(DynamicValue).GetProperty(nameof(DynamicValue.Value))!,
            displayName, category, description, kind, valueType, propertyEditor: editor) { Name = name, _read = read, _write = write };
    }

    private sealed class DynamicValue(Func<object?> read, Action<object?> write)
    {
        public object? Value { get => read(); set => write(value); }
    }

    /// <summary>创建显示在属性行中的操作按钮；操作不作为参数值提交或序列化。</summary>
    /// <param name="name">稳定属性身份。</param>
    /// <param name="displayName">属性名。</param>
    /// <param name="category">所在分组。</param>
    /// <param name="description">操作说明。</param>
    /// <param name="caption">按钮当前文字。</param>
    /// <param name="execute">异步操作，异常由属性面板显示。</param>
    /// <param name="blockReason">不可执行的原因；返回空文本表示可执行。</param>
    /// <returns>只读操作属性。</returns>
    public static WorkflowPropertyEntry CreateAction(string name, string displayName, string category, string description,
        Func<string> caption, Func<Task> execute, Func<string>? blockReason = null)
    {
        ArgumentNullException.ThrowIfNull(caption);
        ArgumentNullException.ThrowIfNull(execute);
        var entry = Create(name, displayName, category, description, WorkflowPropertyEditorKind.Action, typeof(string), () => caption(), _ => { });
        entry._action = execute; entry._actionBlockReason = blockReason;
        return entry;
    }

    /// <summary>此操作是否由领域描述直接处理；否则通过编辑器键请求宿主页面。</summary>
    public bool HasActionHandler => _action != null;
    /// <summary>操作不可执行的原因；空文本表示可执行。</summary>
    public string ActionBlockReason => _actionBlockReason?.Invoke() ?? "";
    /// <summary>执行属性按钮的领域操作，不写入普通属性值。</summary>
    /// <returns>操作完成任务。</returns>
    public Task ExecuteActionAsync()
    {
        if (_action == null) throw new InvalidOperationException("此属性操作需要宿主编辑窗口。");
        var reason = ActionBlockReason;
        if (reason.Length != 0) throw new InvalidOperationException(reason);
        return _action();
    }

    /// <summary>创建领域提供的动态候选属性，复用类型转换与撤销/重做。</summary>
    public static WorkflowPropertyEntry CreateChoice(string name, string displayName, string category, string description,
        Func<object?> read, Action<object?> write, IReadOnlyList<WorkflowPropertyChoice> choices, WorkflowPropertyEditorAttribute editor)
    {
        var access = new DynamicValue(read, write);
        return new WorkflowPropertyEntry(access, typeof(DynamicValue).GetProperty(nameof(DynamicValue.Value))!,
            displayName, category, description, WorkflowPropertyEditorKind.Choice, typeof(string), propertyEditor: editor, choices: choices)
            { Name = name, _read = read, _write = write };
    }

    /// <summary>获取适合属性面板显示的名称。</summary>
    public string DisplayName { get; private set; }

    /// <summary>获取属性面板分组名称。</summary>
    public string Category { get; private set; }

    /// <summary>分类下的可展开子组；保持叶属性身份不变，提交仍经过统一撤销/重做。</summary>
    public IReadOnlyList<string> GroupPath { get; private set; } = Array.Empty<string>();

    /// <summary>设置领域属性的分类和子组；仅影响展示，不改变叶属性身份和访问方式。</summary>
    public WorkflowPropertyEntry InGroup(string category, params string[] path)
    {
        Category = category;
        GroupPath = path.ToArray();
        return this;
    }

    /// <summary>用于属性树底部帮助区的参数说明。</summary>
    public string Description { get; }

    /// <summary>获取属性应使用的编辑器类型。</summary>
    public WorkflowPropertyEditorKind EditorKind { get; }

    /// <summary>获取编辑器直接读写的值类型。</summary>
    public Type ValueType { get; }

    /// <summary>数值编辑器在显示单位下的可选下限；未指定时由平台使用默认范围。</summary>
    public double? NumberMinimum { get; private set; }

    /// <summary>领域数值编辑器的可选上限。</summary>
    public double? NumberMaximum { get; private set; }

    /// <summary>向平台传递数值范围，使步进按钮遵循领域约束；写入端仍负责最终校验。</summary>
    /// <param name="minimum">存储单位的下限；为空表示不限制。</param>
    /// <param name="maximum">存储单位的上限；为空表示不限制。</param>
    /// <returns>当前属性条目。</returns>
    public WorkflowPropertyEntry WithNumberRange(double? minimum, double? maximum)
    {
        if (EditorKind != WorkflowPropertyEditorKind.Number) throw new InvalidOperationException("只有数值属性可以指定范围。");
        if (minimum > maximum || minimum is { } min && !double.IsFinite(min) || maximum is { } max && !double.IsFinite(max))
            throw new ArgumentException("数值范围无效。");
        var factor = _displayRadiansAsDegrees ? 180 / Math.PI : 1;
        NumberMinimum = minimum * factor; NumberMaximum = maximum * factor;
        return this;
    }

    /// <summary>把弧度属性投影为度；模型字段、集合元素和输入绑定仍保持原有弧度契约。</summary>
    /// <returns>供两个平台共用的属性条目。</returns>
    public WorkflowPropertyEntry WithRadiansAsDegrees()
    {
        var type = WorkflowInputType ?? ValueType;
        if (type != typeof(double) && type != typeof(double?) && type != typeof(List<double>) && type != typeof(double[]))
            throw new InvalidOperationException("角度显示转换只支持double及其集合或工作流输入。");
        if (_displayRadiansAsDegrees) return this;
        _displayRadiansAsDegrees = true;
        if (!DisplayName.Contains('°')) DisplayName += "（°）";
        NumberMinimum *= 180 / Math.PI;
        NumberMaximum *= 180 / Math.PI;
        return this;
    }

    private object? ReadStoredValue() => _read is not null ? _read() : _property.GetValue(_owner);

    private object? AngleValue(object? value, bool toStorage)
    {
        if (!_displayRadiansAsDegrees || value == null) return value;
        double Scale(double angle)
        {
            if (toStorage && (!double.IsFinite(angle) || angle < NumberMinimum || angle > NumberMaximum))
                throw new ArgumentOutOfRangeException(Name, angle, $"{DisplayName}允许范围：{NumberMinimum?.ToString(CultureInfo.InvariantCulture) ?? "无下限"}～{NumberMaximum?.ToString(CultureInfo.InvariantCulture) ?? "无上限"}，角度须为有限数值。");
            var scaled = toStorage ? angle * (Math.PI / 180) : angle * (180 / Math.PI);
            if (toStorage && !double.IsFinite(scaled)) throw new ArgumentOutOfRangeException(nameof(value), "角度必须是有限数值。");
            return scaled;
        }
        return value switch
        {
            double angle => Scale(angle),
            List<double> angles => angles.Select(Scale).ToList(),
            double[] angles => angles.Select(Scale).ToArray(),
            _ => value
        };
    }

    /// <summary>获取工作流输入包装的值类型；非工作流输入时为空。</summary>
    public Type? WorkflowInputType { get; }

    /// <summary>工作流输入的固定值是否能由通用文本编辑器转换；复杂对象须由绑定或专用编辑器提供。</summary>
    public bool CanEditInputLiteralAsText => WorkflowInputType is { } type && CanConvertFromText(type);

    private static bool CanConvertFromText(Type type)
    {
        var core = Nullable.GetUnderlyingType(type) ?? type;
        return core.IsEnum || core == typeof(Guid) || core == typeof(TimeSpan)
            || Type.GetTypeCode(core) is TypeCode.String or TypeCode.Boolean or TypeCode.Char or TypeCode.DateTime
                or TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32
                or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal
            || !core.IsValueType && core.GetConstructor([typeof(string)]) is not null;
    }

    /// <summary>获取宿主自定义编辑器键。</summary>
    public string? EditorKey { get; }

    /// <summary>获取文件等特殊编辑器使用的过滤条件。</summary>
    public string? EditorFilter { get; }

    /// <summary>获取特殊编辑器对话框标题。</summary>
    public string? EditorDialogTitle { get; }

    /// <summary>获取编辑器是否必须校验目标路径存在。</summary>
    public bool EditorCheckExists { get; }

    /// <summary>获取候选编辑器可选项；非候选编辑器为空集合。</summary>
    public IReadOnlyList<WorkflowPropertyChoice> Choices { get; }

    public bool IsReadOnly => EditorKind is WorkflowPropertyEditorKind.ReadOnly or WorkflowPropertyEditorKind.Action;

    /// <summary>获取面板显示单位下的当前值；WorkflowInput通过GetInputLiteral投影固定值。</summary>
    public object? Value => EditorKind == WorkflowPropertyEditorKind.WorkflowInput ? ReadStoredValue() : AngleValue(ReadStoredValue(), false);

    /// <summary>设置普通标量属性。</summary>
    /// <param name="value">要校验、转换或写入的值。</param>
    public void SetValue(object? value)
    {
        if (IsReadOnly)
            throw new InvalidOperationException($"属性 {Name} 为只读。");
        if (EditorKind == WorkflowPropertyEditorKind.WorkflowInput)
            throw new InvalidOperationException($"属性 {Name} 必须使用 SetWorkflowInput。");
        var converted = AngleValue(ConvertValue(value, ValueType), true);
        ValidateSpecialEditorValue(converted);
        if (_write is not null) _write(converted);
        else _property.SetValue(_owner, converted);
    }

    /// <summary>将集合或复杂对象导出为缩进 JSON。</summary>
    public string GetStructuredJson() => JsonSerializer.Serialize(Value, ValueType, StructuredJsonOptions);

    /// <summary>从 JSON 整体替换集合或复杂对象。</summary>
    /// <param name="json">结构化 JSON 文本。</param>
    public void SetStructuredJson(string json)
    {
        if (EditorKind != WorkflowPropertyEditorKind.Structured)
            throw new InvalidOperationException($"属性 {Name} 不是结构化属性。");
        var value = JsonSerializer.Deserialize(json, ValueType, StructuredJsonOptions)
            ?? throw new InvalidOperationException($"{DisplayName} 不能设置为空。");
        SetValue(value);
    }

    /// <summary>读取 WorkflowInput 的来源。</summary>
    public WorkflowValueSource GetInputSource() =>
        (WorkflowValueSource)GetRequiredInputProperty(nameof(WorkflowInput<object>.Source)).GetValue(Value)!;

    /// <summary>读取 WorkflowInput 的固定值。</summary>
    public object? GetInputLiteral() =>
        AngleValue(GetRequiredInputProperty(nameof(WorkflowInput<object>.LiteralValue)).GetValue(Value), false);

    /// <summary>读取 WorkflowInput 的绑定。</summary>
    public WorkflowBindingKey? GetInputBinding() =>
        (WorkflowBindingKey?)GetRequiredInputProperty(nameof(WorkflowInput<object>.Binding)).GetValue(Value);

    /// <summary>整体替换 WorkflowInput 配置。</summary>
    /// <param name="source">源数据或路径点集合。</param>
    /// <param name="literalValue">输入使用的常量值。</param>
    /// <param name="binding">输入使用的绑定键。</param>
    public void SetWorkflowInput(
        WorkflowValueSource source,
        object? literalValue,
        WorkflowBindingKey? binding)
    {
        if (EditorKind != WorkflowPropertyEditorKind.WorkflowInput || WorkflowInputType is null)
            throw new InvalidOperationException($"属性 {Name} 不是 WorkflowInput。");
        if (!Enum.IsDefined(source))
            throw new InvalidOperationException("输入来源类型未定义。");
        if (source == WorkflowValueSource.Binding && !binding.HasValue)
            throw new InvalidOperationException("绑定模式必须选择绑定键。");

        var input = Activator.CreateInstance(ValueType)
            ?? throw new InvalidOperationException($"无法创建 {ValueType.Name}。");
        GetRequiredInputProperty(nameof(WorkflowInput<object>.Source)).SetValue(input, source);
        GetRequiredInputProperty(nameof(WorkflowInput<object>.LiteralValue)).SetValue(
            input,
            source == WorkflowValueSource.Binding && literalValue is null
                ? GetRequiredInputProperty(nameof(WorkflowInput<object>.LiteralValue)).GetValue(Value)
                : AngleValue(ConvertValue(literalValue, WorkflowInputType), true));
        GetRequiredInputProperty(nameof(WorkflowInput<object>.Binding)).SetValue(input, binding);
        _property.SetValue(_owner, input);
    }

    /// <summary>校验特殊编辑器返回值是否符合属性要求。</summary>
    /// <param name="value">要校验、转换或写入的值。</param>
    private void ValidateSpecialEditorValue(object? value)
    {
        if (!EditorCheckExists || value is not string path || string.IsNullOrWhiteSpace(path)) return;
        if (string.Equals(EditorKey, WorkflowPropertyEditorKeys.FilePath, StringComparison.Ordinal)
            && !File.Exists(path))
            throw new InvalidOperationException($"文件不存在：{path}。");
        if (string.Equals(EditorKey, WorkflowPropertyEditorKeys.FolderPath, StringComparison.Ordinal)
            && !Directory.Exists(path))
            throw new InvalidOperationException($"文件夹不存在：{path}。");
    }

    private static readonly JsonSerializerOptions StructuredJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>取得工作流输入对象的必需子属性。</summary>
    /// <param name="name">名称。</param>
    private PropertyInfo GetRequiredInputProperty(string name) =>
        ValueType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException($"{ValueType.Name} 缺少属性 {name}。");

    /// <summary>将编辑器输入值转换为目标属性类型。</summary>
    /// <param name="value">要校验、转换或写入的值。</param>
    /// <param name="targetType">目标转换类型。</param>
    private static object? ConvertValue(object? value, Type targetType)
    {
        var nullable = Nullable.GetUnderlyingType(targetType);
        var coreType = nullable ?? targetType;
        if (value is null || value is string { Length: 0 } && nullable is not null)
        {
            if (nullable is not null || !coreType.IsValueType)
                return null;
            throw new InvalidOperationException($"{coreType.Name} 不允许空值。");
        }
        if (coreType.IsInstanceOfType(value))
            return value;
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (coreType == typeof(string))
            return text;
        if (coreType.IsEnum)
            return Enum.Parse(coreType, text, true);
        if (coreType == typeof(Guid))
            return Guid.Parse(text);
        if (coreType == typeof(TimeSpan))
            return TimeSpan.Parse(text, CultureInfo.InvariantCulture);
        // 只接受单个字符串的取值对象（例如逻辑源身份）：编辑器给出的是文本，由类型自己校验并 Trim。
        if (!coreType.IsValueType && coreType.GetConstructor(new[] { typeof(string) }) is { } textConstructor)
            return textConstructor.Invoke(new object[] { text });
        if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(coreType))
            return Convert.ChangeType(value, coreType, CultureInfo.InvariantCulture);
        throw new InvalidOperationException($"{coreType.Name} 不能由普通文本设置，请使用绑定或专用编辑器。");
    }
}

/// <summary>共享节点属性面板模型，并提供强类型绑定候选。</summary>
public sealed class WorkflowPropertyInspectorModel : IDisposable
{
    private readonly WorkflowDesignerSession _session;
    private readonly WorkflowPropertyChoiceProvider? _choiceProvider;
    private readonly Func<IWorkflowNodeModel, IReadOnlyList<WorkflowPropertyEntry>>? _additionalProperties;
    private IReadOnlyList<WorkflowPropertyEntry> _entries = Array.Empty<WorkflowPropertyEntry>();

    /// <summary>保留已有三参数构造入口，领域动态属性可使用四参数重载。</summary>
    public WorkflowPropertyInspectorModel(WorkflowDesignerSession session, string startNodeId, WorkflowPropertyChoiceProvider? choiceProvider = null)
        : this(session, startNodeId, choiceProvider, null) { }

    /// <summary>初始化属性检查器并订阅会话和公共数据声明变化。</summary>
    /// <param name="session">设计器会话。</param>
    /// <param name="startNodeId">工作流开始节点标识。</param>
    /// <param name="choiceProvider">
    /// 可选候选提供者；宿主用它把机器配置注入属性面板。
    /// 未提供时所有候选编辑器退回文本输入，不会因为缺少宿主装配而无法编辑。
    /// </param>
    /// <param name="additionalProperties">由领域描述提供的附加属性条目。</param>
    public WorkflowPropertyInspectorModel(
        WorkflowDesignerSession session,
        string startNodeId,
        WorkflowPropertyChoiceProvider? choiceProvider,
        Func<IWorkflowNodeModel, IReadOnlyList<WorkflowPropertyEntry>>? additionalProperties)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        EntryNodeId = startNodeId ?? throw new ArgumentNullException(nameof(startNodeId));
        _choiceProvider = choiceProvider;
        _additionalProperties = additionalProperties;
        _session.Changed += OnSessionChanged;
        _session.PublicDataCatalog.Changed += OnPublicDataChanged;
        Refresh();
    }

    /// <summary>获取或设置当前工作流的开始节点标识。</summary>
    public string EntryNodeId { get; set; }

    /// <summary>获取当前检查器选中的节点。</summary>
    public IWorkflowNodeModel? SelectedNode { get; private set; }

    public IReadOnlyList<WorkflowPropertyEntry> Entries => _entries;

    /// <summary>在模型内容发生变化、界面需要刷新时发生。</summary>
    public event EventHandler? Changed;

    /// <summary>提交普通属性值并通知画布刷新。</summary>
    /// <param name="entry">目标属性条目。</param>
    /// <param name="value">要校验、转换或写入的值。</param>
    public void SetValue(WorkflowPropertyEntry entry, object? value)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ExecuteConfigurationChange(() => entry.SetValue(value));
    }

    /// <summary>提交表格化集合并通知画布刷新。</summary>
    /// <param name="table">集合表格模型。</param>
    /// <param name="rows">表格行数据。</param>
    public void ApplyCollectionTable(WorkflowCollectionTableModel table, IEnumerable<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(table);
        ExecuteConfigurationChange(() => table.Apply(rows));
    }

    /// <summary>提交集合或复杂对象 JSON 并通知画布刷新。</summary>
    /// <param name="entry">目标属性条目。</param>
    /// <param name="json">结构化 JSON 文本。</param>
    public void SetStructuredJson(WorkflowPropertyEntry entry, string json)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ExecuteConfigurationChange(() => entry.SetStructuredJson(json));
    }

    /// <summary>提交 WorkflowInput 配置并通知画布刷新。</summary>
    /// <param name="entry">目标属性条目。</param>
    /// <param name="source">源数据或路径点集合。</param>
    /// <param name="literalValue">输入使用的常量值。</param>
    /// <param name="binding">输入使用的绑定键。</param>
    public void SetWorkflowInput(
        WorkflowPropertyEntry entry,
        WorkflowValueSource source,
        object? literalValue,
        WorkflowBindingKey? binding)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ExecuteConfigurationChange(() => entry.SetWorkflowInput(source, literalValue, binding));
    }

    private void ExecuteConfigurationChange(Action change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var node = SelectedNode
            ?? throw new InvalidOperationException("当前没有选中的节点。");
        _session.ExecuteNodeConfigurationChange(node.Id, _ => change());
    }

    /// <summary>获取当前消费者与输入类型兼容的绑定候选。</summary>
    /// <param name="entry">目标属性条目。</param>
    public IReadOnlyList<WorkflowBindingCandidate> GetBindingCandidates(WorkflowPropertyEntry entry)
    {
        if (SelectedNode is null || entry.WorkflowInputType is null)
            return Array.Empty<WorkflowBindingCandidate>();
        if (!string.Equals(_session.Document.EntryNodeId, EntryNodeId, StringComparison.Ordinal))
            return Array.Empty<WorkflowBindingCandidate>();
        var analysis = new WorkflowBindingAnalyzer(_session.Catalog).Analyze(_session.Document);
        var nodeCandidates = analysis.GetCandidates(SelectedNode.Id, entry.WorkflowInputType);
        var globalCandidates = _session.PublicDataCatalog.Items
            .SelectMany(item => WorkflowBindingAnalyzer.EnumerateBindableMembers(item.ValueType)
                .Where(member => WorkflowBindingAnalyzer.IsTypeCompatible(member.Type, entry.WorkflowInputType))
                .Select(member => new WorkflowBindingCandidate(
                    item.Key,
                    member.Path,
                    member.Type,
                    $"{item.Category}/{item.DisplayName}/{member.Path}",
                    WorkflowBindingCandidateSourceKind.PublicData,
                    item.DisplayName,
                    item.Category)))
            .ToArray();
        return nodeCandidates.Concat(globalCandidates)
            .DistinctBy(candidate => candidate.ToBindingKey())
            .ToArray();
    }

    /// <summary>解除事件订阅并释放当前模型持有的资源。</summary>
    public void Dispose()
    {
        _session.Changed -= OnSessionChanged;
        _session.PublicDataCatalog.Changed -= OnPublicDataChanged;
    }

    /// <summary>处理公共数据声明变化并通知属性面板刷新。</summary>
    private void OnPublicDataChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>处理设计会话变更，并同步刷新派生模型。</summary>
    private void OnSessionChanged(object? sender, WorkflowDesignerChangedEventArgs e)
    {
        if (e.Kind is WorkflowDesignerChangeKind.Selection or WorkflowDesignerChangeKind.Document)
            Refresh();
    }

    /// <summary>重新构建当前模型的数据并通知界面刷新。</summary>
    private void Refresh()
    {
        SelectedNode = _session.SelectedNodeId is { } selectedId
            ? _session.Canvas.Nodes.FirstOrDefault(item => item.Node.Id == selectedId)?.Node
            : null;
        _entries = SelectedNode is null ? Array.Empty<WorkflowPropertyEntry>() : BuildEntries(SelectedNode, _choiceProvider, _session.Canvas.Nodes.Select(item => item.Node).ToArray())
            .Concat(_additionalProperties?.Invoke(SelectedNode) ?? Array.Empty<WorkflowPropertyEntry>()).ToArray();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>通过反射为节点构建可编辑属性条目。</summary>
    /// <param name="node">目标画布节点或节点模型。</param>
    /// <param name="choiceProvider">可选候选提供者。</param>
    /// <param name="documentNodes">同文档节点，供节点自己提供的文档候选使用。</param>
    private static IReadOnlyList<WorkflowPropertyEntry> BuildEntries(IWorkflowNodeModel node, WorkflowPropertyChoiceProvider? choiceProvider,
        IReadOnlyList<IWorkflowNodeModel> documentNodes)
    {
        var entries = new List<WorkflowPropertyEntry>();
        foreach (var property in node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(property => property.CanRead && property.GetIndexParameters().Length == 0))
        {
            if (property.Name is nameof(IWorkflowNodeModel.NodeType) or nameof(IWorkflowScriptNode.ScriptId) or nameof(IWorkflowScriptNode.ScriptLanguage) or nameof(IWorkflowScriptReferenceNode.ScriptReferencePaths) or "SubDocument" or "ChildNodeCount"
                || property.GetCustomAttribute<BrowsableAttribute>() is { Browsable: false }
                || !IsPropertyVisible(node, property))
                continue;
            var propertyType = property.PropertyType;
            var propertyEditor = property.GetCustomAttribute<WorkflowPropertyEditorAttribute>();
            var candidates = propertyEditor?.EditorKey == WorkflowPropertyEditorKeys.DocumentChoice
                ? ResolveDocumentChoices(node, property.Name, documentNodes)
                : ResolveChoices(propertyEditor, property.Name, choiceProvider);
            Type? inputType = null;
            WorkflowPropertyEditorKind kind;
            if (propertyEditor?.IsAction == true)
                kind = WorkflowPropertyEditorKind.Action;
            else if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(WorkflowInput<>))
            {
                inputType = propertyType.GetGenericArguments()[0];
                kind = WorkflowPropertyEditorKind.WorkflowInput;
            }
            else if (!property.CanWrite || property.Name == nameof(IWorkflowNodeModel.Id))
                kind = WorkflowPropertyEditorKind.ReadOnly;
            else if (IsChoiceEditor(propertyEditor) && candidates.Count > 0)
                kind = WorkflowPropertyEditorKind.Choice;
            else if (propertyType == typeof(bool))
                kind = WorkflowPropertyEditorKind.Boolean;
            else if (propertyEditor is null && LabeledEnumChoices(propertyType) is { Count: > 0 } labeled)
            {
                candidates = labeled;
                kind = WorkflowPropertyEditorKind.Choice;
            }
            else if ((Nullable.GetUnderlyingType(propertyType) ?? propertyType).IsEnum)
                kind = WorkflowPropertyEditorKind.Enum;
            else if (IsNumber(Nullable.GetUnderlyingType(propertyType) ?? propertyType))
                kind = WorkflowPropertyEditorKind.Number;
            else if (IsChoiceEditor(propertyEditor))
                kind = candidates.Count > 0 ? WorkflowPropertyEditorKind.Choice : WorkflowPropertyEditorKind.Text;
            else if (propertyType == typeof(string) && property.Name == nameof(IWorkflowScriptNode.Script) && node is IWorkflowScriptNode)
                kind = WorkflowPropertyEditorKind.Script;
            else if (propertyType == typeof(string) || propertyType == typeof(TimeSpan) || propertyType == typeof(Guid))
                kind = WorkflowPropertyEditorKind.Text;
            else if (property.CanWrite && (propertyType.IsClass || typeof(System.Collections.IEnumerable).IsAssignableFrom(propertyType)))
                kind = WorkflowPropertyEditorKind.Structured;
            else
                continue;

            if (propertyEditor is not null && propertyType != typeof(string) && !IsChoiceEditor(propertyEditor))
                throw new InvalidOperationException($"专用属性编辑器 {propertyEditor.EditorKey} 当前只支持字符串属性：{node.GetType().Name}.{property.Name}。");
            var workflowMetadata = property.GetCustomAttribute<WorkflowPropertyAttribute>();
            var chineseMetadata = WorkflowPropertyChineseMetadata.Resolve(property.Name);
            var displayName = workflowMetadata?.DisplayName
                ?? property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName
                ?? chineseMetadata.DisplayName;
            var category = workflowMetadata?.Category
                ?? property.GetCustomAttribute<CategoryAttribute>()?.Category
                ?? chineseMetadata.Category;
            var description = workflowMetadata?.Description
                ?? property.GetCustomAttribute<DescriptionAttribute>()?.Description
                ?? chineseMetadata.Description;
            if (!string.IsNullOrWhiteSpace(workflowMetadata?.Unit))
                description += $" 单位：{workflowMetadata.Unit}。";
            var enumType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (enumType.IsEnum && kind == WorkflowPropertyEditorKind.Enum) description = AppendEnumOptions(description, enumType);
            var entry = new WorkflowPropertyEntry(
                node,
                property,
                displayName,
                category,
                description,
                kind,
                propertyType,
                inputType,
                propertyEditor,
                candidates);
            if (workflowMetadata?.DisplayRadiansAsDegrees == true) entry.WithRadiansAsDegrees();
            entries.Add(entry);
        }
        return entries.OrderBy(entry => entry.Category, StringComparer.Ordinal)
            .ThenBy(entry => entry.DisplayName, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>判断属性是否声明了候选编辑器；该键允许作用在非字符串的取值对象上。</summary>
    private static bool IsChoiceEditor(WorkflowPropertyEditorAttribute? propertyEditor) =>
        propertyEditor?.EditorKey is WorkflowPropertyEditorKeys.VisionAreaSource
            or WorkflowPropertyEditorKeys.VisionLineScanSource
            or WorkflowPropertyEditorKeys.DocumentChoice
        || propertyEditor?.EditorKey.StartsWith(WorkflowPropertyEditorKeys.VisionAlgorithmPrefix, StringComparison.Ordinal) == true;

    /// <summary>
    /// 解析候选值。宿主提供者抛错时退回文本编辑而不是让整个属性面板不可用；
    /// 缺少候选时操作员仍然可以手写标识，只是拿不到下拉提示。
    /// </summary>
    private static IReadOnlyList<WorkflowPropertyChoice> ResolveChoices(
        WorkflowPropertyEditorAttribute? propertyEditor,
        string propertyName,
        WorkflowPropertyChoiceProvider? choiceProvider)
    {
        if (propertyEditor is null || choiceProvider is null)
            return Array.Empty<WorkflowPropertyChoice>();
        try
        {
            return choiceProvider(propertyEditor.EditorKey, propertyName) ?? Array.Empty<WorkflowPropertyChoice>();
        }
        catch (Exception)
        {
            return Array.Empty<WorkflowPropertyChoice>();
        }
    }

    /// <summary>由节点按同文档内容提供候选；节点未实现或出错时退回无候选。</summary>
    private static IReadOnlyList<WorkflowPropertyChoice> ResolveDocumentChoices(IWorkflowNodeModel node, string propertyName,
        IReadOnlyList<IWorkflowNodeModel> documentNodes)
    {
        if (node is not IWorkflowDocumentPropertyChoices source) return Array.Empty<WorkflowPropertyChoice>();
        try { return source.GetPropertyChoices(propertyName, documentNodes).Select(item => new WorkflowPropertyChoice(item.Key, item.Value)).ToArray(); }
        catch (Exception) { return Array.Empty<WorkflowPropertyChoice>(); }
    }

    /// <summary>根据属性元数据和节点状态判断属性是否显示。</summary>
    /// <param name="node">目标画布节点或节点模型。</param>
    /// <param name="property">反射属性信息。</param>
    private static bool IsPropertyVisible(IWorkflowNodeModel node, PropertyInfo property)
    {
        var rules = property.GetCustomAttributes<WorkflowPropertyVisibleWhenAttribute>().ToArray();
        if (rules.Length > 0)
        {
            return rules.All(rule =>
            {
                var dependency = node.GetType().GetProperty(rule.PropertyName, BindingFlags.Instance | BindingFlags.Public);
                var actual = Convert.ToString(dependency?.GetValue(node), CultureInfo.InvariantCulture) ?? string.Empty;
                return rule.ExpectedValues.Count == 0
                    ? !string.IsNullOrWhiteSpace(actual)
                    : rule.ExpectedValues.Any(expected => string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase));
            });
        }

        if (property.Name.EndsWith("Binding", StringComparison.Ordinal))
        {
            var baseName = property.Name[..^"Binding".Length];
            var source = node.GetType().GetProperty(baseName + "Source", BindingFlags.Instance | BindingFlags.Public)?.GetValue(node);
            if (source is not null)
                return string.Equals(Convert.ToString(source, CultureInfo.InvariantCulture), "Binding", StringComparison.OrdinalIgnoreCase);
        }
        var ownSource = node.GetType().GetProperty(property.Name + "Source", BindingFlags.Instance | BindingFlags.Public)?.GetValue(node);
        if (ownSource is not null)
            return !string.Equals(Convert.ToString(ownSource, CultureInfo.InvariantCulture), "Binding", StringComparison.OrdinalIgnoreCase);
        if (property.Name == "LiteralValue")
        {
            var valueSource = node.GetType().GetProperty("ValueSource", BindingFlags.Instance | BindingFlags.Public)?.GetValue(node);
            if (valueSource is not null)
                return !string.Equals(Convert.ToString(valueSource, CultureInfo.InvariantCulture), "Binding", StringComparison.OrdinalIgnoreCase);
        }
        return true;
    }

    /// <summary>枚举每个取值都用 [Description] 给出中文标签时，按标签生成下拉候选；否则返回空，仍按枚举名编辑。</summary>
    /// <param name="type">属性类型，可为可空枚举。</param>
    private static IReadOnlyList<WorkflowPropertyChoice> LabeledEnumChoices(Type type)
    {
        var core = Nullable.GetUnderlyingType(type) ?? type;
        if (!core.IsEnum) return Array.Empty<WorkflowPropertyChoice>();
        var fields = core.GetFields(BindingFlags.Public | BindingFlags.Static);
        var labels = fields.Select(field => field.GetCustomAttribute<DescriptionAttribute>()?.Description).ToArray();
        return labels.All(label => !string.IsNullOrWhiteSpace(label))
            ? fields.Select((field, index) => new WorkflowPropertyChoice(labels[index]!, field.GetValue(null))).ToArray()
            : Array.Empty<WorkflowPropertyChoice>();
    }

    private static string AppendEnumOptions(string description, Type enumType)
    {
        var values = Enum.GetNames(enumType).Select(name =>
        {
            var field = enumType.GetField(name, BindingFlags.Public | BindingFlags.Static);
            var definition = field?.GetCustomAttribute<DescriptionAttribute>()?.Description
                ?? field?.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName;
            return string.IsNullOrWhiteSpace(definition)
                ? $"- {name}"
                : $"- {name} - {definition.Trim()}";
        });
        return $"{description}{Environment.NewLine}可选值：{Environment.NewLine}{string.Join(Environment.NewLine, values)}";
    }

    /// <summary>判断类型是否为 CLR 数值类型。</summary>
    /// <param name="type">目标 CLR 类型。</param>
    private static bool IsNumber(Type type) => Type.GetTypeCode(type) is
        TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32
        or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single
        or TypeCode.Double or TypeCode.Decimal;
}
