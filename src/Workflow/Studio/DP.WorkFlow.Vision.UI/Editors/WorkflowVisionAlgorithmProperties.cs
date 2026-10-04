using System.Globalization;
using DP.Vision.Algorithms;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.Vision.UI;

/// <summary>按描述投影算法和嵌套依赖，不创建模型或算法实例。</summary>
public static class WorkflowVisionAlgorithmProperties
{
    /// <summary>提供行内实现选择、参数子组、递归依赖及轻量检查；不会创建算法实例。</summary>
    public static Func<IWorkflowNodeModel, IReadOnlyList<WorkflowPropertyEntry>> CreateProvider(VisionAlgorithmCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return node => node is IWorkflowVisionAlgorithmNode algorithm
            ? algorithm.GetAlgorithmSlots().SelectMany(slot => Project(slot, catalog, node)).ToArray()
            : Array.Empty<WorkflowPropertyEntry>();
    }

    private static IReadOnlyList<WorkflowPropertyEntry> Project(WorkflowVisionAlgorithmSlot slot, VisionAlgorithmCatalog catalog, IWorkflowNodeModel node)
    {
        var entries = new List<WorkflowPropertyEntry>();
        var objects = new HashSet<VisionAlgorithmSelection>(ReferenceEqualityComparer.Instance);
        var implementations = new HashSet<string>(StringComparer.Ordinal);
        var visited = 0;
        // 每次重新定位；撤销和失败回滚会替换整个选择图。
        VisionAlgorithmSelection Root() => ((IWorkflowVisionAlgorithmNode)node).GetAlgorithmSlots().Single(s => s.Name == slot.Name).Selection;
        VisionAlgorithmSelection? Find(IReadOnlyList<string> path)
        {
            var current = Root();
            foreach (var part in path)
            {
                if (current?.Dependencies is null || !current.Dependencies.TryGetValue(part, out current)) return null;
            }
            return current;
        }
        VisionAlgorithmSelection Required(IReadOnlyList<string> path) => Find(path)
            ?? throw new InvalidOperationException("依赖选择已经变化，请重新选择当前属性。");
        var rootName = "Algorithm." + Escape(slot.Name);
        var capability = (VisionCapabilityAttribute?)Attribute.GetCustomAttribute(slot.ContractType, typeof(VisionCapabilityAttribute));
        var rootLabel = capability?.DisplayName ?? slot.Name;
        if (slot.Name != "algorithm") rootLabel += "（" + slot.Name + "）";
        Walk(slot.ContractType, slot.RequiredFeatures ?? Array.Empty<string>(), Array.Empty<string>(), rootName,
            "算法/" + rootLabel, node is LocateVisionTemplateNodeModel && slot.Name == "locator");
        foreach (var entry in entries)
            entry.InGroup("算法", entry.Category.Split('/').Skip(1).ToArray());
        return entries;

        void Message(string name, string category, string text)
        {
            var identity = name + ".Status";
            var existing = entries.FindIndex(entry => entry.Name == identity);
            if (existing >= 0) text = entries[existing].Description + Environment.NewLine + text;
            var message = WorkflowPropertyEntry.Create(identity, "配置检查", category, text,
                WorkflowPropertyEditorKind.ReadOnly, typeof(string), () => text, _ => { });
            if (existing >= 0) entries[existing] = message; else entries.Add(message);
        }

        void Walk(Type? contract, IReadOnlyList<string> features, string[] path, string name, string category, bool legacyRoot = false)
        {
            if (++visited > 512 || path.Length > 64) { Message(name, category, "依赖数量或深度超过编辑预算，请修正配方或清空上级配置。"); return; }
            var selection = Find(path);
            var id = selection?.ImplementationId ?? string.Empty;
            if (selection is not null && (objects.Contains(selection) || (!string.IsNullOrEmpty(id) && implementations.Contains(id))))
            { Message(name, category, "检测到依赖循环，可在上级“高级”子组清空配置后重新选择。"); return; }
            if (selection is not null) { objects.Add(selection); if (!string.IsNullOrEmpty(id)) implementations.Add(id); }
            try
            {
                var descriptors = contract is null ? Array.Empty<VisionAlgorithmDescriptor>() : catalog.Implementations
                    .Where(d => Compatible(contract, d.ContractType) && features.All(f => d.Features.Contains(f, StringComparer.Ordinal))).ToArray();
                var descriptor = descriptors.FirstOrDefault(d => d.ImplementationId == id);
                if (!legacyRoot)
                {
                    var choices = descriptors.Select(d => new WorkflowPropertyChoice($"{d.Engine} / {d.ImplementationId}", d.ImplementationId)).ToList();
                    if (path.Length != 0 || string.IsNullOrEmpty(id)) choices.Insert(0, new WorkflowPropertyChoice("未选择", string.Empty));
                    if (!string.IsNullOrEmpty(id) && !choices.Any(c => Equals(c.Value, id)))
                        choices.Add(new WorkflowPropertyChoice(id + "（未安装或不兼容）", id));
                    entries.Add(WorkflowPropertyEntry.CreateChoice(name + ".ImplementationId", "算法实现", category,
                        "按当前能力选择实现；有配置时先点击清空配置。未选择的依赖会阻止运行。",
                        () => Find(path)?.ImplementationId ?? string.Empty, value =>
                        {
                            var nextId = (string)value!;
                            var current = Find(path);
                            if (current?.ImplementationId == nextId) return;
                            if (current is not null && (current.SettingsVersion != 1 || current.Settings?.Count > 0 || current.Dependencies?.Count > 0))
                                throw new InvalidOperationException("请先清空原实现的初始化参数和依赖选择，再切换实现。");
                            if (path.Length == 0) Required(path).ImplementationId = nextId;
                            else
                            {
                                var parent = Required(path[..^1]);
                                if (nextId.Length == 0) parent.Dependencies.Remove(path[^1]);
                                else parent.Dependencies[path[^1]] = new VisionAlgorithmSelection { ImplementationId = nextId };
                            }
                        }, choices, new WorkflowPropertyEditorAttribute(WorkflowPropertyEditorKeys.VisionAlgorithmPrefix
                            + (contract is null ? "unknown" : ((VisionCapabilityAttribute?)Attribute.GetCustomAttribute(contract, typeof(VisionCapabilityAttribute)))?.Id))));
                }
                if (selection is null)
                { Message(name, category, "尚未选择依赖实现；请选择后配置其参数。"); return; }
                entries.Add(WorkflowPropertyEntry.Create(name + ".Reset", "清空配置以切换实现", category + "/高级",
                    "开启一次即清空此实现的参数和下级依赖，参数版本恢复为 1；可以撤销。当前实现选择保留。",
                    WorkflowPropertyEditorKind.Boolean, typeof(bool), () => false, value =>
                    {
                        if (value is not true) return;
                        var current = Required(path);
                        current.Settings = new(StringComparer.Ordinal); current.Dependencies = new(StringComparer.Ordinal); current.SettingsVersion = 1;
                    }));
                if (!legacyRoot)
                {
                    entries.Add(WorkflowPropertyEntry.Create(name + ".SettingsVersion", "参数版本", category + "/高级",
                        "按实现的配置版本解释参数；需要升级时请使用算法面板的配置升级。", WorkflowPropertyEditorKind.Number, typeof(int),
                        () => Required(path).SettingsVersion, value =>
                        {
                            if ((int)value! < 1) throw new ArgumentOutOfRangeException(nameof(value), "参数版本必须大于 0。");
                            Required(path).SettingsVersion = (int)value;
                        }));
                }
                if (selection.Settings is null || selection.Dependencies is null) { Message(name, category, "参数或依赖为空，请清空配置后重新设置。"); return; }
                // 未安装实现、未知键和插件不支持的参数类型仍用普通字符串子项展示，不能丢弃配方内容。
                foreach (var key in selection.Settings.Keys.Where(key => descriptor is null || !descriptor.Parameters.Any(parameter => parameter.Id == key && Supported(parameter.ValueType))))
                    AddUnknownParameter(key, path, name, category);
                if (descriptor is null)
                {
                    Message(name, category, contract is null ? "上级实现没有声明此槽位，原配置保留，请修正。" : "实现未选择、未安装或不兼容，原配置保留。");
                    foreach (var unknown in selection.Dependencies.Keys)
                    {
                        if (visited >= 512) { Message(name + ".Truncated", category, "依赖数量超过编辑预算。"); break; }
                        Walk(null, [], [.. path, unknown], name + ".Dependency." + Escape(unknown), category + "/依赖/未知依赖（" + unknown + "）");
                    }
                    return;
                }
                foreach (var parameter in descriptor.Parameters)
                    AddParameter(parameter, descriptor, path, name, category);
                IReadOnlyList<VisionAlgorithmDependency> dependencies;
                try
                {
                    var configuration = new VisionAlgorithmConfiguration(selection.SettingsVersion, selection.Settings);
                    if (descriptor.Factory is IVisionAlgorithmConfigurationValidator validator)
                    {
                        var errors = validator.ValidateConfiguration(configuration);
                        if (errors.Count != 0) Message(name, category, string.Join(Environment.NewLine, errors));
                    }
                    dependencies = descriptor.Factory.GetDependencies(configuration);
                    if (dependencies is null || dependencies.Any(d => d is null || string.IsNullOrWhiteSpace(d.Slot) || d.ContractType is null))
                        throw new InvalidOperationException("依赖描述包含空槽位或空契约。");
                }
                catch (Exception error)
                { Message(name, category, "无法读取依赖描述：" + error.Message); return; }
                var declared = dependencies.GroupBy(d => d.Slot, StringComparer.Ordinal).ToArray();
                if (declared.Any(g => g.Count() > 1)) Message(name + ".Duplicate", category, "实现重复声明依赖槽位，请检查插件。");
                foreach (var dependency in declared.Select(g => g.First()))
                {
                    if (visited >= 512) { Message(name + ".Truncated", category, "依赖数量超过编辑预算，请修正配方或清空上级配置。"); break; }
                    var capability = (VisionCapabilityAttribute?)Attribute.GetCustomAttribute(dependency.ContractType, typeof(VisionCapabilityAttribute));
                    Walk(dependency.ContractType, Array.Empty<string>(), [.. path, dependency.Slot],
                        name + ".Dependency." + Escape(dependency.Slot), category + "/依赖/" + (capability?.DisplayName ?? dependency.ContractType.Name) + "（" + dependency.Slot + "）");
                }
                foreach (var unknown in selection.Dependencies.Keys.Where(k => !declared.Any(g => g.Key == k)))
                {
                    if (visited >= 512) { Message(name + ".UnknownTruncated", category, "未知依赖数量超过编辑预算，请修正配方或清空上级配置。"); break; }
                    Walk(null, Array.Empty<string>(), [.. path, unknown], name + ".Dependency." + Escape(unknown), category + "/依赖/未知依赖（" + unknown + "）");
                }
            }
            finally
            {
                if (selection is not null) { objects.Remove(selection); if (!string.IsNullOrEmpty(id)) implementations.Remove(id); }
            }
        }

        void AddUnknownParameter(string key, string[] path, string name, string category)
        {
            entries.Add(WorkflowPropertyEntry.Create(name + ".RawParameter." + Escape(key), key, category + "/未识别参数",
                "此参数没有可用的类型描述，按原始字符串保留和编辑。", WorkflowPropertyEditorKind.Text, typeof(string),
                () => Required(path).Settings.TryGetValue(key, out var value) ? value : string.Empty,
                value => Required(path).Settings[key] = (string)value!));
        }

        void AddParameter(VisionAlgorithmParameter parameter, VisionAlgorithmDescriptor descriptor, string[] path, string name, string category)
        {
            var type = parameter.ValueType;
            var kind = type == typeof(bool) ? WorkflowPropertyEditorKind.Boolean : type.IsEnum ? WorkflowPropertyEditorKind.Enum
                : type == typeof(string) ? WorkflowPropertyEditorKind.Text : WorkflowPropertyEditorKind.Number;
            if (!Supported(type)) return;
            // 保留首版常见参数身份；保留字使用独立前缀，避免参数碰撞控制项。
            var parameterName = new[] { "ImplementationId", "SettingsVersion", "Settings", "Dependencies", "Reset", "Status", "Dependency" }.Contains(parameter.Id)
                ? "Parameter." + Escape(parameter.Id) : Escape(parameter.Id);
            VisionAlgorithmSelection Current()
            {
                var current = Required(path);
                if (current.ImplementationId != descriptor.ImplementationId) throw new InvalidOperationException("算法实现已变化，请刷新参数。");
                return current;
            }
            entries.Add(WorkflowPropertyEntry.Create(name + "." + parameterName, parameter.DisplayName, category + "/初始化参数",
                parameter.Description ?? string.Empty, kind, type, () =>
                {
                    var text = Current().Settings.TryGetValue(parameter.Id, out var existing) ? existing : parameter.DefaultValue;
                    if (text is null) return type.IsValueType ? Activator.CreateInstance(type) : string.Empty;
                    try { return type.IsEnum ? Enum.Parse(type, text) : Convert.ChangeType(text, type, CultureInfo.InvariantCulture); }
                    catch (Exception error) when (error is FormatException or OverflowException or ArgumentException) { return text; }
                }, value =>
                {
                    if (kind == WorkflowPropertyEditorKind.Number)
                    {
                        var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                        if (!double.IsFinite(number) || number < parameter.Minimum || number > parameter.Maximum)
                            throw new ArgumentOutOfRangeException(parameter.Id, "初始化参数超出允许范围。");
                    }
                    if (type.IsEnum && !Enum.IsDefined(type, value!)) throw new ArgumentException("枚举值未定义。");
                    Current().Settings[parameter.Id] = value is IFormattable formattable
                        ? formattable.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? string.Empty;
                }, parameter.IsFilePath ? new WorkflowPropertyEditorAttribute(WorkflowPropertyEditorKeys.FilePath) : null));
        }
    }

    private static bool Supported(Type type) => type == typeof(string) || type == typeof(bool) || type.IsEnum
        || type == typeof(int) || type == typeof(long) || type == typeof(double) || type == typeof(float) || type == typeof(decimal);

    private static bool Compatible(Type contract, Type implementation) => contract.IsAssignableFrom(implementation) || implementation.IsAssignableFrom(contract);
    private static string Escape(string value) => Uri.EscapeDataString(value).Replace(".", "%2E", StringComparison.Ordinal);
}
