using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>独立定义业务原点、轴、单位和版本，不持有图像或模板。</summary>
[WorkflowNode("Vision.DefineCoordinateSystem", DisplayName = "定义坐标系", Category = "5.Vision/Coordinates")]
public sealed class DefineVisionCoordinateSystemNodeModel : WorkflowNodeModel, IWorkflowNodeConfigurationValidator,
    IWorkflowNodeDocumentConfigurationValidator, IWorkflowVisionCoordinateDefinitionNode
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.DefineCoordinateSystem";
    /// <summary>稳定业务身份。</summary>
    [WorkflowProperty("坐标ID", "同文档唯一；不要使用每帧生成的身份。", Category = "定义")]
    public string CoordinateId { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>显示名称。</summary>
    [WorkflowProperty("坐标名称", "例如工件、夹具或测量基准。", Category = "定义")]
    public string CoordinateName { get; set; } = "工件坐标";
    /// <summary>语义版本。</summary>
    [WorkflowProperty("定义版本", "更改基准或标定时递增；旧ROI需要明确重新确认。", Category = "定义")]
    public int DefinitionVersion { get; set; } = 1;
    /// <summary>局部长度单位。</summary>
    [WorkflowProperty("长度单位", "毫米需要提供已知物理长度或有效标定，名称本身不产生标定。", Category = "定义")]
    public EVisionCoordinateUnit Unit { get; set; }
    /// <summary>原点的稳定业务含义。</summary>
    [WorkflowProperty("原点说明", "例如两基准线交点；改变含义会使旧ROI校验失败。", Category = "定义")]
    public string OriginDescription { get; set; } = "工件基准原点";
    /// <summary>轴的稳定业务含义。</summary>
    [WorkflowProperty("轴说明", "例如X沿长边，Y为顺时针90度；算法来源可更换，轴含义应保持。", Category = "定义")]
    public string AxisDescription { get; set; } = "X沿基准方向，Y顺时针90度";
    /// <inheritdoc/>
    public VisionCoordinateDefinition GetCoordinateDefinition() => new(CoordinateId, CoordinateName, DefinitionVersion, Unit, OriginDescription, AxisDescription);
    /// <inheritdoc/>
    public IReadOnlyList<string> ValidateConfiguration()
    {
        try { _ = GetCoordinateDefinition(); return []; } catch (ArgumentException ex) { return [ex.Message]; }
    }
    /// <inheritdoc/>
    public IReadOnlyList<string> ValidateDocumentConfiguration(IReadOnlyList<IWorkflowNodeModel> nodes) =>
        WorkflowVisionCoordinateCatalog.IsDuplicate(nodes, CoordinateId) ? ["本文档存在重复坐标定义ID。"] : [];
}

/// <summary>发布本配方的不可变定义。</summary>
public sealed class DefineVisionCoordinateSystemNodeHandler : WorkflowNodeHandler<DefineVisionCoordinateSystemNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(DefineVisionCoordinateSystemNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: node.GetCoordinateDefinition()));
    }
}
