using System.Collections;
using System.ComponentModel;
using System.Text.Json.Serialization;
using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>测量项类型。</summary>
public enum EVisionMeasureKind
{
    /// <summary>找线（线卡尺）。</summary>
    [Description("找线")]
    Line,
    /// <summary>找圆（扇环卡尺）。</summary>
    [Description("找圆")]
    Circle,
    /// <summary>找矩形（矩形卡尺）。</summary>
    [Description("找矩形")]
    Rectangle
}

/// <summary>测量项失败时整个测量组的处理方式。</summary>
public enum EVisionMeasureFailurePolicy
{
    /// <summary>任一启用的测量项失败，节点失败（走失败出口），消息写明是哪一项。</summary>
    [Description("全部必须成功")]
    RequireAll,
    /// <summary>节点成功；失败项的“是否找到”为假，下游读取它的结果时给出明确的绑定错误。</summary>
    [Description("允许部分失败")]
    AllowPartial
}

/// <summary>
/// 测量组中的一个测量项：稳定标识（绑定用）、显示名（可改）、类型，以及该类型的找线/找圆/找矩形配置。
/// 配置里的输入图像和坐标系不单独保存，由测量组统一提供。
/// </summary>
public sealed class VisionMeasureItem
{
    /// <summary>稳定标识，绑定路径为 <c>Items.&lt;Id&gt;.成员</c>；建项时生成，之后不变。</summary>
    public string Id { get; set; } = NewId();
    /// <summary>显示名，例如“左边”；改名不影响绑定。</summary>
    public string Name { get; set; } = "";
    /// <summary>是否启用；禁用项不测量、不计入失败、不出现在可绑定成员里。</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>测量类型。</summary>
    public EVisionMeasureKind Kind { get; set; }
    /// <summary>找线配置（类型为找线时）。</summary>
    public FindVisionLineNodeModel? Line { get; set; }
    /// <summary>找圆配置（类型为找圆时）。</summary>
    public FindVisionCircleNodeModel? Circle { get; set; }
    /// <summary>找矩形配置（类型为找矩形时）。</summary>
    public FindVisionRectangleNodeModel? Rectangle { get; set; }

    /// <summary>当前类型的配置；与类型不符或缺失时为空。</summary>
    [JsonIgnore, Browsable(false)]
    public FindVisionShapeNodeModel? Shape => Kind switch
    {
        EVisionMeasureKind.Line => Line,
        EVisionMeasureKind.Circle => Circle,
        EVisionMeasureKind.Rectangle => Rectangle,
        _ => null
    };

    /// <summary>该类型测量结果的类型。</summary>
    [JsonIgnore, Browsable(false)]
    public Type ResultType => Kind switch
    {
        EVisionMeasureKind.Circle => typeof(VisionFindCircleResult),
        EVisionMeasureKind.Rectangle => typeof(VisionFindRectangleResult),
        _ => typeof(VisionFindLineResult)
    };

    /// <summary>新建指定类型的测量项，配置带默认搜索范围。</summary>
    /// <param name="kind">类型。</param><param name="name">显示名。</param>
    public static VisionMeasureItem Create(EVisionMeasureKind kind, string name) => new()
    {
        Kind = kind, Name = name,
        Line = kind == EVisionMeasureKind.Line ? new FindVisionLineNodeModel() : null,
        Circle = kind == EVisionMeasureKind.Circle ? new FindVisionCircleNodeModel() : null,
        Rectangle = kind == EVisionMeasureKind.Rectangle ? new FindVisionRectangleNodeModel() : null
    };

    /// <summary>新标识：“m”加 8 位十六进制，不会与集合自身的属性名冲突。</summary>
    public static string NewId() => "m" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>标识格式是否有效。</summary>
    /// <param name="id">标识。</param>
    public static bool IsValidId(string? id) => id is { Length: >= 2 and <= 32 } && id[0] == 'm' && id.Skip(1).All(c => char.IsAsciiLetterOrDigit(c));
}

/// <summary>
/// 测量组：在同一张图、同一坐标系下测量多个找线/找圆/找矩形项。每项产出与对应单节点相同的结果类型，
/// 下游按 <c>Items.&lt;测量项标识&gt;.成员</c> 绑定（例如 <c>Items.m3f9a1c2b.MeasuredLine</c>），设计器显示为“测量项名 · 成员”。
/// </summary>
[WorkflowNode("Vision.MeasureGroup", DisplayName = "测量组", Category = WorkflowVisionCategories.Measurement,
    Description = "同一张图、同一坐标系下测量多个找线/找圆/找矩形项；每项结果可单独绑定。")]
public sealed class MeasureVisionGroupNodeModel : AnalyzeVisionFrameNodeModel, IWorkflowVisionAlgorithmNode, IWorkflowDynamicOutputNode
{
    internal const string LineFitterSlot = "line-fitter", CircleFitterSlot = "circle-fitter", CaliperSlot = "caliper";

    /// <inheritdoc/>
    public override string NodeType => "Vision.MeasureGroup";
    /// <inheritdoc/>
    public override EWorkflowVisionRange RangeCapability => EWorkflowVisionRange.SamplingBand;
    /// <inheritdoc/>
    public override bool SupportsRegionMask => false;

    /// <summary>测量项，按绘制和输出顺序排列。</summary>
    [Browsable(false)]
    public List<VisionMeasureItem> Items { get; set; } = new();

    /// <summary>测量项失败时的处理方式。</summary>
    [WorkflowProperty("失败策略", "全部必须成功：任一启用项失败则节点失败；允许部分失败：节点成功，失败项标记为未找到。", Category = "测量组")]
    public EVisionMeasureFailurePolicy FailurePolicy { get; set; }

    /// <summary>卡尺实现选择（所有测量项共用）。</summary>
    [Browsable(false)]
    public VisionAlgorithmSelection CaliperAlgorithm { get; set; } = new() { ImplementationId = "managed.caliper" };
    /// <summary>直线拟合实现选择（找线、找矩形共用）。</summary>
    [Browsable(false)]
    public VisionAlgorithmSelection LineFitterAlgorithm { get; set; } = new() { ImplementationId = "managed.robust-line" };
    /// <summary>圆拟合实现选择。</summary>
    [Browsable(false)]
    public VisionAlgorithmSelection CircleFitterAlgorithm { get; set; } = new() { ImplementationId = "managed.robust-circle" };

    /// <inheritdoc/>
    public IReadOnlyList<WorkflowVisionAlgorithmSlot> GetAlgorithmSlots()
    {
        var kinds = Enabled.Select(i => i.Kind).ToHashSet();
        var slots = new List<WorkflowVisionAlgorithmSlot> { new(CaliperSlot, typeof(ICaliperMeasurer), CaliperAlgorithm) };
        if (kinds.Contains(EVisionMeasureKind.Line) || kinds.Contains(EVisionMeasureKind.Rectangle))
            slots.Add(new(LineFitterSlot, typeof(IRobustLineFitter), LineFitterAlgorithm));
        if (kinds.Contains(EVisionMeasureKind.Circle))
            slots.Add(new(CircleFitterSlot, typeof(IRobustCircleFitter), CircleFitterAlgorithm));
        return slots;
    }

    private IEnumerable<VisionMeasureItem> Enabled => (Items ?? new()).Where(i => i is { Enabled: true });

    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (!Enum.IsDefined(FailurePolicy)) errors.Add("失败策略无效。");
        if (Items is null || Items.Count == 0 || Items.Count > 64) { errors.Add("测量组需要 1..64 个测量项。"); return errors; }
        if (Items.Any(i => i is null || !VisionMeasureItem.IsValidId(i.Id)) || Items.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != Items.Count)
            errors.Add("测量项标识缺失、格式无效或重复。");
        if (Items.Any(i => string.IsNullOrWhiteSpace(i?.Name)) || Items.Select(i => i?.Name?.Trim()).Distinct(StringComparer.Ordinal).Count() != Items.Count)
            errors.Add("测量项名称不能为空或重复。");
        if (!Enabled.Any()) errors.Add("至少启用一个测量项。");
        foreach (var item in Enabled)
        {
            if (item.Shape is not { } shape) { errors.Add($"测量项 {item.Name}：缺少{KindName(item.Kind)}配置。"); continue; }
            using var _ = Share(shape);
            // 输入图像、坐标系由测量组统一提供，这里只报告测量项自身的配置问题。
            errors.AddRange(shape.ValidateConfiguration().Except(base.ValidateConfiguration()).Select(e => $"测量项 {item.Name}：{e}"));
        }
        return errors;
    }

    /// <summary>
    /// 把测量组的输入图像和坐标系临时交给测量项配置（找线/找圆的校验、排布和图上编辑都读取这两项），
    /// 释放时恢复，测量项本身不保存这两项。
    /// </summary>
    /// <param name="shape">测量项配置。</param>
    public IDisposable Share(FindVisionShapeNodeModel shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var (frame, coordinates, full) = (shape.Frame, shape.Coordinates, shape.FullImage);
        shape.Frame = Frame; shape.Coordinates = Coordinates; shape.FullImage = true;
        return new Restore(() => { shape.Frame = frame; shape.Coordinates = coordinates; shape.FullImage = full; });
    }

    /// <inheritdoc/>
    public IReadOnlyList<WorkflowBindableMember> DescribeOutputs()
    {
        // 整组成员（不展开测量项集合），再逐个启用项展开：信封成员 + 该类型结果的成员。
        var members = WorkflowOutputMembers.OfType(typeof(VisionMeasureGroupResult), maxDepth: 1).ToList();
        foreach (var item in Enabled.Where(i => VisionMeasureItem.IsValidId(i.Id)))
        {
            string path = "Items." + item.Id, prefix = item.Name + " · ";
            members.Add(new WorkflowBindableMember(path, typeof(VisionMeasureItemResult), item.Name));
            members.AddRange(WorkflowOutputMembers.OfType(typeof(VisionMeasureItemResult), path, prefix, 1).Skip(1)
                .Where(m => !m.Path.EndsWith(".Value", StringComparison.Ordinal)));
            members.AddRange(WorkflowOutputMembers.OfType(item.ResultType, path, prefix, 2).Skip(1));
        }
        return members;
    }

    /// <summary>测量类型的中文名。</summary>
    /// <param name="kind">类型。</param>
    public static string KindName(EVisionMeasureKind kind) => kind switch
    {
        EVisionMeasureKind.Circle => "找圆", EVisionMeasureKind.Rectangle => "找矩形", _ => "找线"
    };

    private sealed class Restore(Action restore) : IDisposable
    {
        private Action? _restore = restore;
        public void Dispose() { _restore?.Invoke(); _restore = null; }
    }
}

/// <summary>一个测量项的结果：信封（标识、名称、类型、是否找到、失败原因）+ 实际结果。</summary>
public sealed class VisionMeasureItemResult : IWorkflowKeyedOutput
{
    internal VisionMeasureItemResult(VisionMeasureItem item, object? value, string? error)
    {
        Id = item.Id; Name = item.Name; Kind = item.Kind; Value = value; Error = error;
    }
    /// <summary>测量项标识。</summary>
    public string Id { get; }
    /// <summary>测量项名称。</summary>
    public string Name { get; }
    /// <summary>测量类型。</summary>
    public EVisionMeasureKind Kind { get; }
    /// <summary>是否找到。</summary>
    public bool Found => Value is not null;
    /// <summary>失败原因；找到时为空。</summary>
    public string? Error { get; }
    /// <summary>实际结果：<see cref="VisionFindLineResult"/>、<see cref="VisionFindCircleResult"/> 或 <see cref="VisionFindRectangleResult"/>；失败时为空。</summary>
    public object? Value { get; }

    /// <summary>信封上没有的成员按实际结果的公开属性读取，绑定路径不必写 <c>.Value</c>。</summary>
    public bool TryGetKeyedValue(string key, out object? value)
    {
        value = null;
        var property = Value?.GetType().GetProperty(key, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
        if (property is null || !property.CanRead || property.GetIndexParameters().Length != 0) return false;
        value = property.GetValue(Value);
        return true;
    }

    /// <inheritdoc/>
    public string DescribeMissingKey(string key) => Found ? $"测量项 {Name} 的结果没有成员 {key}。" : $"测量项 {Name} 未找到：{Error}";
}

/// <summary>测量项结果集合：按配置顺序遍历，也可按测量项标识取单项。</summary>
public sealed class VisionMeasureItemResults : IReadOnlyList<VisionMeasureItemResult>, IWorkflowKeyedOutput
{
    private readonly IReadOnlyList<VisionMeasureItemResult> _items;
    internal VisionMeasureItemResults(IReadOnlyList<VisionMeasureItemResult> items) => _items = items;
    /// <inheritdoc/>
    public int Count => _items.Count;
    /// <inheritdoc/>
    public VisionMeasureItemResult this[int index] => _items[index];
    /// <summary>按标识取测量项结果。</summary>
    /// <param name="id">测量项标识。</param>
    public VisionMeasureItemResult? Find(string id) => _items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal));
    /// <inheritdoc/>
    public bool TryGetKeyedValue(string key, out object? value) { value = Find(key); return value is not null; }
    /// <inheritdoc/>
    public string DescribeMissingKey(string key) => $"测量项 {key} 不存在（已删除或未启用）。";
    /// <inheritdoc/>
    public IEnumerator<VisionMeasureItemResult> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>测量组结果。</summary>
public sealed class VisionMeasureGroupResult : IVisionGeometryFact, IVisionDisplayFact, IWorkflowResultRows
{
    private readonly IReadOnlyList<VisionDisplayItem> _failedDisplay;
    internal VisionMeasureGroupResult(string frameId, IReadOnlyList<VisionMeasureItemResult> items, IReadOnlyList<VisionDisplayItem> failedDisplay)
    {
        FrameId = frameId; Items = new VisionMeasureItemResults(items); _failedDisplay = failedDisplay;
    }
    /// <inheritdoc/>
    public string FrameId { get; }
    /// <summary>各测量项结果（启用项，按配置顺序）；绑定路径 <c>Items.&lt;标识&gt;.成员</c>。</summary>
    public VisionMeasureItemResults Items { get; }
    /// <summary>是否全部找到。</summary>
    public bool AllFound => Items.All(i => i.Found);
    /// <summary>找到的测量项数。</summary>
    public int FoundCount => Items.Count(i => i.Found);
    /// <summary>失败的测量项数。</summary>
    public int FailedCount => Items.Count - FoundCount;
    /// <summary>失败的测量项名称，逗号分隔；全部找到时为空串。</summary>
    public string FailedNames => string.Join("，", Items.Where(i => !i.Found).Select(i => i.Name));
    /// <inheritdoc/>
    public IReadOnlyList<Geometry> DisplayGeometry => VisionDisplay.Geometries(DisplayItems);
    /// <inheritdoc/>
    [Browsable(false)]
    public IReadOnlyList<VisionDisplayItem> DisplayItems
    {
        get
        {
            // 每项的显示项：主结果标签前加项名，点击说明前加“项名：”；失败项的卡尺画成 NG。
            var items = new List<VisionDisplayItem>();
            foreach (var item in Items)
                if (item.Value is IVisionDisplayFact display)
                    items.AddRange(display.DisplayItems.Select(d => Prefix(d, item.Name)));
            items.AddRange(_failedDisplay);
            return items;
        }
    }
    /// <summary>结果页每个测量项一行：找到时为该项的摘要，失败时为原因。</summary>
    [Browsable(false)]
    public IReadOnlyList<KeyValuePair<string, object?>> ResultRows => Items.Select(i => new KeyValuePair<string, object?>(
        "测量项 · " + i.Name, i.Value is IVisionGeometryFact fact ? fact.Summary : "未找到：" + i.Error)).ToArray();
    /// <inheritdoc/>
    public string Summary => $"测量组：{FoundCount}/{Items.Count} 项找到" + (FailedCount == 0 ? "" : $"，失败：{FailedNames}") + "。"
        + string.Concat(Items.Select(i => i.Value is IVisionGeometryFact fact ? $" [{i.Name}] {fact.Summary}" : $" [{i.Name}] {i.Error}"));

    private static VisionDisplayItem Prefix(VisionDisplayItem item, string name)
    {
        string? label = item.Role == EVisionDisplayRole.Result && item.Label is { } text ? Clip($"{name} {text}") : item.Label;
        string? detail = item.Detail is { } d ? $"{name}：{d}" : null;
        var copy = item.Marker is { } marker ? VisionDisplayItem.Point(marker, item.Role, label, detail) : new VisionDisplayItem(item.Geometry!, item.Role, label, detail);
        return copy.WithTone(item.Tone).WithColor(item.Argb);
    }

    internal static string Clip(string text) => text.Length <= 64 ? text : text[..63] + "…";
}

/// <summary>测量组：逐项测量，按失败策略汇总。</summary>
public sealed class MeasureVisionGroupNodeHandler : WorkflowNodeHandler<MeasureVisionGroupNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(MeasureVisionGroupNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var errors = node.ValidateConfiguration();
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("；", errors));
        var frame = context.ResolveInput(node.Frame) ?? throw new InvalidOperationException("输入帧为空。");
        var coordinates = node.ResolveCoordinates(frame, context);
        var results = new List<VisionMeasureItemResult>();
        var failedDisplay = new List<VisionDisplayItem>();
        foreach (var item in node.Items.Where(i => i.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shape = item.Shape!;
            using var _ = node.Share(shape);
            try
            {
                object value = shape switch
                {
                    FindVisionRectangleNodeModel rectangle => VisionShapeMeasure.Rectangle(rectangle, context, frame, coordinates, cancellationToken, MeasureVisionGroupNodeModel.LineFitterSlot),
                    FindVisionCircleNodeModel circle => VisionShapeMeasure.Circle(circle, context, frame, coordinates, cancellationToken, MeasureVisionGroupNodeModel.CircleFitterSlot),
                    _ => VisionShapeMeasure.Line(shape, context, frame, coordinates, cancellationToken, MeasureVisionGroupNodeModel.LineFitterSlot)
                };
                results.Add(new VisionMeasureItemResult(item, value, null));
            }
            catch (InvalidOperationException error)
            {
                if (node.FailurePolicy == EVisionMeasureFailurePolicy.RequireAll)
                    throw new InvalidOperationException($"测量项 {item.Name}：{error.Message}", error);
                results.Add(new VisionMeasureItemResult(item, null, error.Message));
                failedDisplay.AddRange(FailedDisplay(item, shape, coordinates, error.Message));
            }
        }
        var result = new VisionMeasureGroupResult(frame.FrameId, results, failedDisplay);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result, projection: WorkflowVisionFrameScope.Stage(context, frame, result)));
    }

    // 失败项：把它的卡尺扫描线画成 NG，第一把卡尺起点标“项名 未找到”，点击给出原因。
    private static IEnumerable<VisionDisplayItem> FailedDisplay(VisionMeasureItem item, FindVisionShapeNodeModel shape, VisionCoordinateSystem? coordinates, string error)
    {
        IReadOnlyList<VisionCaliperScan> scans;
        try { scans = shape.CaliperScans(coordinates); }
        catch (InvalidOperationException) { yield break; }
        string detail = $"{item.Name}：{error}";
        foreach (var scan in scans)
            yield return VisionDisplayItem.Segment(scan.Start, scan.End, EVisionDisplayRole.Input, detail: detail).WithTone(EVisionDisplayTone.Ng);
        if (scans.Count > 0)
            yield return VisionDisplayItem.Point(scans[0].Start, EVisionDisplayRole.Result, VisionMeasureGroupResult.Clip($"{item.Name} 未找到"), detail).WithTone(EVisionDisplayTone.Ng);
    }
}
