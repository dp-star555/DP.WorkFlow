using DP.Vision.Acquisition;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.Vision.UI;

/// <summary>
/// 把机器配置已发布的逻辑源投影成属性面板候选，并按节点类型过滤采集几何形态：
/// 面阵节点只看到面阵源，线扫节点只看到线扫源。
/// <para>
/// 放在这里而不是DP.WorkFlow.UI.Shared，是因为过滤需要认识 <see cref="EVisionAcquisitionKind"/>；
/// 共享属性模型只认识编辑器键，不能反向依赖采集领域。
/// </para>
/// </summary>
public static class WorkflowVisionSourceChoices
{
    /// <summary>
    /// 创建属性面板共用的候选提供者。宿主只需注册一次，
    /// 具体属性声明哪个编辑器键由 <see cref="WorkflowPropertyEditorKeys"/> 决定。
    /// </summary>
    /// <param name="catalog">机器配置投影出的逻辑源目录。</param>
    /// <returns>按编辑器键返回候选的提供者；未知键返回空候选，编辑器退回文本输入。</returns>
    /// <exception cref="ArgumentNullException">目录为空。</exception>
    public static WorkflowPropertyChoiceProvider CreateProvider(IWorkflowVisionSourceCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return (editorKey, _) => editorKey switch
        {
            WorkflowPropertyEditorKeys.VisionImageSourceMode =>
            [
                new("文件", EWorkflowVisionImageSource.File),
                new("文件夹", EWorkflowVisionImageSource.Folder),
                new("面阵相机", EWorkflowVisionImageSource.AreaCamera),
                new("线扫相机", EWorkflowVisionImageSource.LineCamera)
            ],
            WorkflowPropertyEditorKeys.VisionAreaSource => Project(catalog, EVisionAcquisitionKind.AreaScan),
            WorkflowPropertyEditorKeys.VisionLineScanSource => Project(catalog, EVisionAcquisitionKind.LineScan),
            _ => Array.Empty<WorkflowPropertyChoice>()
        };
    }

    /// <summary>
    /// 投影指定形态的候选。宿主未声明形态（<see cref="WorkflowVisionSourceInfo.Kind"/> 为空）的源
    /// 在两个列表里都保留：形态未知时不猜成面阵或线扫，而是标注出来交给操作员判断。
    /// 若把未知源一律排除，V1组合或对应Type未安装的机器会得到空下拉框，反而无法完成配置。
    /// </summary>
    /// <param name="catalog">逻辑源目录。</param>
    /// <param name="kind">该属性要求的采集几何形态。</param>
    /// <returns>面朝操作员的候选集合。</returns>
    private static WorkflowPropertyChoice[] Project(IWorkflowVisionSourceCatalog catalog, EVisionAcquisitionKind kind) =>
        catalog.Sources
            .Where(source => source.Kind == kind || source.Kind is null)
            .Select(source => new WorkflowPropertyChoice(Describe(source), new VisionSourceReference(source.SourceId)))
            .ToArray();

    /// <summary>候选标签直接说明不可用原因与形态是否已声明，避免操作员选到一个必然失败或语义不明的源。</summary>
    private static string Describe(WorkflowVisionSourceInfo source)
    {
        var label = source.IsAvailable ? source.SourceId : $"{source.SourceId}（不可用：{source.Diagnostic}）";
        return source.Kind is null ? $"{label}（采集类型未声明）" : label;
    }
}
