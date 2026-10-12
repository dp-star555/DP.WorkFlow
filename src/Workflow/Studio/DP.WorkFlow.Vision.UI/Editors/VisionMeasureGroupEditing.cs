using DP.Vision;
using DP.Vision.Algorithms;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.Vision.UI;

/// <summary>测量项列表中的一行。</summary>
/// <param name="Id">测量项标识（绑定用）。</param><param name="Name">名称。</param><param name="Kind">类型名称。</param>
/// <param name="Enabled">是否启用。</param><param name="Status">最近一次运行结果：已找到 / 未找到：原因 / 空。</param>
public sealed record VisionMeasureItemRow(string Id, string Name, string Kind, bool Enabled, string Status);

// 测量组：测量项的增删、启用、选中；选中项用找线/找圆/找矩形的图上编辑器编辑，参数显示在参数页“测量项”分组里。
public sealed partial class VisionFrameEditorPageModel : IWorkflowNodeEditorPropertyContributor
{
    private const uint OtherItemColor = 0x9094A3B8;
    private string? _selectedMeasureItemId;
    private (int Width, int Height)? _lastFrameSize;
    private string? _measureResultKey;

    // 运行结果变化时刷新测量项列表的“结果”列。
    private void NotifyMeasureResults(long? sequence)
    {
        var key = $"{sequence}:{_trialVersion}";
        if (MeasureGroup is null || key == _measureResultKey) return;
        _measureResultKey = key;
        MeasureItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>测量组节点（模板编辑等其它页面为空）。</summary>
    public MeasureVisionGroupNodeModel? MeasureGroup => IsTemplateEditor ? null : _node as MeasureVisionGroupNodeModel;

    /// <summary>当前选中的测量项标识。</summary>
    public string? SelectedMeasureItemId => _selectedMeasureItemId;

    /// <summary>测量项增删、改名、启用或选中变化时发生。</summary>
    public event EventHandler? MeasureItemsChanged;

    /// <summary>按配置顺序列出测量项，带最近一次运行的结果。</summary>
    public IReadOnlyList<VisionMeasureItemRow> MeasureItems
    {
        get
        {
            if (MeasureGroup is not { } group) return Array.Empty<VisionMeasureItemRow>();
            VisionMeasureGroupResult? last;
            lock (_gate)
            {
                // 本窗口“运行”的结果优先，其次是最近一次流程运行。
                using var preview = _disposed ? null : _frames?.Capture(_node.Id);
                last = _trial is { } trial && trial.BaseSequence == preview?.Sequence ? trial.Facts as VisionMeasureGroupResult : preview?.Facts as VisionMeasureGroupResult;
            }
            return group.Items.Select(item =>
            {
                var result = last?.Items.Find(item.Id);
                string status = result is null ? "" : result.Found ? "已找到" : "未找到：" + result.Error;
                return new VisionMeasureItemRow(item.Id, item.Name, MeasureVisionGroupNodeModel.KindName(item.Kind), item.Enabled, status);
            }).ToArray();
        }
    }

    /// <summary>选中测量项；画布上的拖动编辑和参数页随之切换。未知标识被忽略。</summary>
    /// <param name="id">测量项标识；为空表示不选。</param>
    public void SelectMeasureItem(string? id)
    {
        if (MeasureGroup is null || id == _selectedMeasureItemId) return;
        if (id is not null && MeasureGroup.Items.All(i => i.Id != id)) return;
        SelectMeasureItemCore(id);
        InvalidatePreview();
        MeasureItemsChanged?.Invoke(this, EventArgs.Empty);
        NotifyConfigurationChanged();
    }

    private void SelectMeasureItemCore(string? id)
    {
        var group = (MeasureVisionGroupNodeModel)_node;
        _selectedMeasureItemId = id;
        var shape = group.Items.FirstOrDefault(i => i.Id == id)?.Shape;
        Caliper = shape is null ? null : new VisionFindShapeGizmo(shape, () => group.Coordinates);
    }

    /// <summary>新增一个测量项并选中；名称按类型自动编号，搜索范围按最近一帧图像居中。</summary>
    /// <param name="kind">类型。</param>
    public VisionMeasureItem AddMeasureItem(EVisionMeasureKind kind)
    {
        var group = MeasureGroup ?? throw new InvalidOperationException("不是测量组节点。");
        if (group.Items.Count >= 64) throw new InvalidOperationException("测量组最多 64 个测量项。");
        var prefix = MeasureVisionGroupNodeModel.KindName(kind);
        var name = Enumerable.Range(1, 999).Select(i => prefix + i).First(n => group.Items.All(item => item.Name != n));
        var item = VisionMeasureItem.Create(kind, name);
        if (group.Coordinates is null && _lastFrameSize is { } size) item.Shape!.FitPlaceholderSearchRoi(size.Width, size.Height);
        group.Items.Add(item);
        SelectMeasureItem(item.Id);
        Status = $"已添加测量项 {name}；绑定路径 Items.{item.Id}.成员。";
        return item;
    }

    /// <summary>删除选中的测量项；绑定它的下游会在编译时报“成员路径不存在”。</summary>
    public void RemoveSelectedMeasureItem()
    {
        if (MeasureGroup is not { } group || group.Items.FindIndex(i => i.Id == _selectedMeasureItemId) is not (>= 0 and var index)) return;
        var removed = group.Items[index];
        group.Items.RemoveAt(index);
        var next = group.Items.Count == 0 ? null : group.Items[Math.Min(index, group.Items.Count - 1)].Id;
        _selectedMeasureItemId = null;
        SelectMeasureItem(next);
        if (next is null) { InvalidatePreview(); MeasureItemsChanged?.Invoke(this, EventArgs.Empty); NotifyConfigurationChanged(); }
        Status = $"已删除测量项 {removed.Name}；原来绑定它的下游需要重新绑定。";
    }

    /// <summary>启用或停用测量项。</summary>
    /// <param name="id">测量项标识。</param><param name="enabled">是否启用。</param>
    public void SetMeasureItemEnabled(string id, bool enabled)
    {
        if (MeasureGroup?.Items.FirstOrDefault(i => i.Id == id) is not { } item || item.Enabled == enabled) return;
        item.Enabled = enabled;
        InvalidatePreview(); MeasureItemsChanged?.Invoke(this, EventArgs.Empty); NotifyConfigurationChanged();
    }

    /// <summary>改名；名称不能为空或与其它测量项重复，改名不影响绑定。</summary>
    /// <param name="id">测量项标识。</param><param name="name">新名称。</param>
    public void RenameMeasureItem(string id, string name)
    {
        if (MeasureGroup is not { } group || group.Items.FirstOrDefault(i => i.Id == id) is not { } item) return;
        name = (name ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("测量项名称不能为空。");
        if (group.Items.Any(i => i != item && i.Name == name)) throw new ArgumentException($"已有名为 {name} 的测量项。");
        if (item.Name == name) return;
        item.Name = name;
        InvalidatePreview(); MeasureItemsChanged?.Invoke(this, EventArgs.Empty); NotifyConfigurationChanged();
    }

    /// <summary>参数页“测量项：名称”分组：选中项的名称、启用、类型、绑定路径，以及该类型的卡尺、边缘与拟合参数。</summary>
    public IEnumerable<WorkflowPropertyEntry> CreateProperties(IWorkflowNodeModel editingNode)
    {
        if (_disposed || MeasureGroup is not { } group || editingNode.Id != group.Id
            || group.Items.FirstOrDefault(i => i.Id == _selectedMeasureItemId) is not { Shape: { } shape } item) yield break;
        var category = "测量项：" + item.Name;
        yield return WorkflowPropertyEntry.Create("MeasureItem.Name", "名称", category, "测量项显示名；改名不影响绑定。",
            WorkflowPropertyEditorKind.Text, typeof(string), () => item.Name, value => RenameMeasureItem(item.Id, value as string ?? ""));
        yield return WorkflowPropertyEntry.Create("MeasureItem.Enabled", "启用", category, "停用的测量项不测量，也不能被下游绑定。",
            WorkflowPropertyEditorKind.Boolean, typeof(bool), () => item.Enabled, value => SetMeasureItemEnabled(item.Id, value is true));
        yield return WorkflowPropertyEntry.Create("MeasureItem.Kind", "类型", category, "测量类型；要换类型请删除后重新添加。",
            WorkflowPropertyEditorKind.ReadOnly, typeof(string), () => MeasureVisionGroupNodeModel.KindName(item.Kind), _ => { });
        yield return WorkflowPropertyEntry.Create("MeasureItem.Path", "绑定路径", category, "下游绑定该测量项结果时使用的成员路径前缀。",
            WorkflowPropertyEditorKind.ReadOnly, typeof(string), () => $"Items.{item.Id}", _ => { });
        // 只取找线/找圆/找矩形自己的参数；输入图像、坐标系、范围等由测量组统一提供。
        foreach (var entry in WorkflowPropertyInspectorModel.CreateEntries(shape))
        {
            var declaring = shape.GetType().GetProperty(entry.Name)?.DeclaringType;
            if (declaring is null || !typeof(FindVisionShapeNodeModel).IsAssignableFrom(declaring)) continue;
            yield return entry.InGroup(category, entry.Category);
        }
    }

    // 未选中的启用项：画出它们的卡尺扫描线（淡灰）并在第一把卡尺起点标名称，点击显示名称和绑定路径。
    private IReadOnlyList<Visual> OtherMeasureItemVisuals()
    {
        if (MeasureGroup is not { } group) return Array.Empty<Visual>();
        if (group.Coordinates is not null && !CoordinateEditingReady) return Array.Empty<Visual>();
        var coordinates = group.Coordinates is null ? null : _displayCoordinates;
        var visuals = new List<Visual>();
        foreach (var item in group.Items.Where(i => i.Enabled && i.Id != _selectedMeasureItemId && i.Shape is not null))
        {
            IReadOnlyList<VisionCaliperScan> scans;
            using (group.Share(item.Shape!))
            {
                try { scans = item.Shape!.CaliperScans(coordinates); }
                catch (Exception error) when (error is InvalidOperationException or ArgumentException) { continue; }
            }
            for (int i = 0; i < scans.Count; i++)
                visuals.Add(new Visual($"measure-{item.Id}-{i}", new ContourGeometry(new[] { scans[i].Start, scans[i].End }), OtherItemColor, i == 0 ? item.Name : null));
        }
        return visuals;
    }

    private string MeasureKey => MeasureGroup is not { } group ? "" : _selectedMeasureItemId + "|" + string.Join(";", group.Items.Select(i =>
        FormattableString.Invariant($"{i.Id}:{i.Name}:{i.Enabled}:{i.Kind}:{(i.Shape is { } shape ? new VisionFindShapeGizmo(shape, () => group.Coordinates).Key : "")}")));
}
