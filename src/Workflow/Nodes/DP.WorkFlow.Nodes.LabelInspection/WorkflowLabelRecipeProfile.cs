using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>外部标签配方版本：原生配方、资产引用及配置帧位姿；不携带生产图像或运行绑定。</summary>
public sealed record WorkflowLabelRecipeProfile
{
    /// <summary>稳定配方标识，不是显示名称。</summary>
    public string Id { get; init; } = string.Empty;
    /// <summary>正整数版本；发布后内容不可原位改写。</summary>
    public int Version { get; init; } = 1;
    /// <summary>完整SDK原生配方JSON。</summary>
    public string RecipeJson { get; init; } = string.Empty;
    /// <summary>字库/异常库根，相对节点资源根。</summary>
    public string DataDirectory { get; init; } = "LabelInspectionData";
    /// <summary>参考图，相对节点资源根。</summary>
    public string ReferenceImagePath { get; init; } = string.Empty;
    /// <summary>OCR模型，相对节点资源根。</summary>
    public string RecognitionModelPath { get; init; } = string.Empty;
    /// <summary>异常骨干，相对节点资源根。</summary>
    public string AnomalyBackbonePath { get; init; } = string.Empty;
    /// <summary>配置样张，仅供编辑。</summary>
    public string AuthorImagePath { get; init; } = string.Empty;
    /// <summary>可选参考图内容校验；发布工具会填写活动参考的SHA256。</summary>
    public string? ReferenceSha256 { get; init; }
    /// <summary>可选OCR文件内容校验。</summary>
    public string? RecognitionSha256 { get; init; }
    /// <summary>可选异常骨干文件内容校验。</summary>
    public string? AnomalySha256 { get; init; }
    /// <summary>该配方自己的配置帧位姿，不能沿用其它配方的位姿。</summary>
    public WorkflowLabelRecipePose? ReferencePose { get; init; }

    /// <summary>只替换标签配置，不修改图像、任务、目录选择输入或资源根。</summary>
    /// <param name="node">待装配的独立节点副本或编辑草稿。</param>
    public void ApplyTo(InspectLabelNodeModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.RecipeJson = RecipeJson; node.DataDirectory = DataDirectory;
        node.ReferenceImagePath = ReferenceImagePath; node.RecognitionModelPath = RecognitionModelPath;
        node.AnomalyBackbonePath = AnomalyBackbonePath; node.AuthorImagePath = AuthorImagePath;
        node.SetReferencePose(ReferencePose?.ToMatrix());
    }
    /// <summary>从已配置节点捕获独立配方版本，资源根与运行输入留在节点。</summary>
    /// <param name="node">配置节点。</param><param name="id">稳定标识。</param><param name="version">正整数版本。</param>
    /// <returns>待发布的配方描述。</returns>
    public static WorkflowLabelRecipeProfile FromNode(InspectLabelNodeModel node, string id, int version)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new() { Id = id, Version = version, RecipeJson = node.RecipeJson, DataDirectory = node.DataDirectory,
            ReferenceImagePath = node.ReferenceImagePath, RecognitionModelPath = node.RecognitionModelPath,
            AnomalyBackbonePath = node.AnomalyBackbonePath, AuthorImagePath = node.AuthorImagePath,
            ReferencePose = WorkflowLabelRecipePose.FromMatrix(node.GetReferencePose()) };
    }
}

/// <summary>可持久化的配置帧局部→原图仿射位姿。</summary>
/// <param name="M11">矩阵M11。</param><param name="M12">矩阵M12。</param><param name="Tx">X平移。</param>
/// <param name="M21">矩阵M21。</param><param name="M22">矩阵M22。</param><param name="Ty">Y平移。</param>
public sealed record WorkflowLabelRecipePose(double M11, double M12, double Tx, double M21, double M22, double Ty)
{
    /// <summary>转换为视觉坐标矩阵。</summary>
    public CoordinateMatrix2D ToMatrix() => CoordinateMatrix2D.FromAffine(M11, M12, Tx, M21, M22, Ty);
    /// <summary>捕获矩阵；未记录位姿时返回null。</summary>
    /// <param name="matrix">局部→原图矩阵。</param>
    public static WorkflowLabelRecipePose? FromMatrix(CoordinateMatrix2D? matrix) => matrix is null ? null :
        new(matrix.M11, matrix.M12, matrix.Tx, matrix.M21, matrix.M22, matrix.Ty);
}

/// <summary>按本次任务选择配方的运行能力；固定配方能力保持兼容。</summary>
public interface IWorkflowLabelRecipeInspectionService
{
    /// <summary>在异步等待前固定目录版本选择；未知标识不回退到节点内配方。</summary>
    /// <param name="context">执行上下文。</param><param name="frame">本次输入帧。</param>
    /// <param name="recipeKey">配方ID或ID@版本。</param><param name="cycleId">本次周期。</param>
    /// <param name="taskData">本次期望数据。</param><param name="coordinates">本帧标签定位，按所选配方的位姿放置ROI。</param>
    /// <param name="cancellationToken">取消本次请求，不取消其它使用者共同需要的加载。</param>
    /// <returns>含实际配方ID/版本和资源身份的完整结果。</returns>
    Task<WorkflowLabelInspectionResult> InspectRecipeAsync(IWorkflowNodeExecutionContext context, DP.Vision.ImageFrame frame,
        string recipeKey, string? cycleId, DP.LabelInspection.Contracts.TaskDataSnapshot? taskData,
        VisionCoordinateSystem? coordinates, CancellationToken cancellationToken);
}
