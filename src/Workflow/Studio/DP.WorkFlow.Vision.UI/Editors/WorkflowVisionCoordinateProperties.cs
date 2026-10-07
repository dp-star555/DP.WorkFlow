using DP.WorkFlow.UI;

namespace DP.WorkFlow.Vision.UI;

/// <summary>属性面板中的“坐标系”下拉：选择本文档的坐标来源即绑定并换算范围，选择“不使用”即解除并换算回原图。</summary>
public static class WorkflowVisionCoordinateProperties
{
    /// <summary>属性条目名称。</summary>
    public const string EntryName = "Coordinates.Source";

    /// <summary>创建附加属性提供者，可与其它提供者合并使用。</summary>
    /// <param name="frames">本轮运行预览，用于按本帧坐标系换算范围。</param>
    /// <param name="documentNodes">读取当前文档节点，用于列出坐标来源。</param>
    /// <returns>按节点返回属性条目的提供者。</returns>
    public static Func<IWorkflowNodeModel, IReadOnlyList<WorkflowPropertyEntry>> CreateProvider(IWorkflowVisionPreviewSource frames,
        Func<IReadOnlyList<IWorkflowNodeModel>> documentNodes)
    {
        ArgumentNullException.ThrowIfNull(frames); ArgumentNullException.ThrowIfNull(documentNodes);
        return node => node is AnalyzeVisionFrameNodeModel { SupportsCoordinates: true } analysis
            ? [Create(analysis, frames, documentNodes())] : [];
    }

    private static WorkflowPropertyEntry Create(AnalyzeVisionFrameNodeModel node, IWorkflowVisionPreviewSource frames, IReadOnlyList<IWorkflowNodeModel> nodes)
    {
        var choices = new List<WorkflowPropertyChoice> { new("不使用（原图坐标）", string.Empty) };
        foreach (var source in nodes.Where(n => n.Id != node.Id && n is IWorkflowVisionCoordinateProducerNode))
        {
            var label = string.IsNullOrWhiteSpace(source.Title) ? source.Id : source.Title;
            try { var definition = ((IWorkflowVisionCoordinateProducerNode)source).GetCoordinateDefinition(); label = $"{definition.Name}（v{definition.Version}，{definition.UnitName}）— {label}"; }
            catch (ArgumentException) { }
            choices.Add(new(label, source.Id));
        }
        string Current() => node.Coordinates?.System.Binding?.NodeId ?? string.Empty;
        if (Current() is { Length: > 0 } current && choices.All(c => !Equals(c.Value, current)))
            choices.Add(new($"{current}（来源节点已不存在）", current));
        return WorkflowPropertyEntry.CreateChoice(EntryName, "坐标系", "坐标系",
            "绑定坐标来源后，每轮使用其本帧输出；上游模板或坐标定义变化不要求重新绑定。选择或解除来源时按本帧矩阵换算已有范围以保持图上位置，因此该换算需要输入图像和坐标来源的预览。",
            Current, value =>
            {
                var target = value as string ?? string.Empty;
                if (target == Current()) return;
                if (target.Length == 0) VisionCoordinateRebinding.Unbind(node, frames);
                else VisionCoordinateRebinding.Bind(node, target, frames);
            }, choices, new WorkflowPropertyEditorAttribute(WorkflowPropertyEditorKeys.DocumentChoice));
    }
}
