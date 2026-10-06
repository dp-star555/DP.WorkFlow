using System.ComponentModel;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>可编辑且可持久化的坐标对应点；不持有运行标定对象。</summary>
public sealed class WorkflowCalibrationSample
{
    /// <summary>源X。</summary>
    public double SourceX { get; set; }
    /// <summary>源Y。</summary>
    public double SourceY { get; set; }
    /// <summary>目标X。</summary>
    public double TargetX { get; set; }
    /// <summary>目标Y。</summary>
    public double TargetY { get; set; }
}

/// <summary>输出独立DP.Vision标定事实，不写旧变量。</summary>
[WorkflowNode("Vision.SolveCalibration", DisplayName = "求解仿射标定", Category = "5.Vision/ImageBuffer")]
public sealed class SolveVisionCalibrationNodeModel : WorkflowNodeModel, IWorkflowNodeConfigurationValidator
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.SolveCalibration";
    /// <summary>至少三个非共线对应点。</summary>
    public List<WorkflowCalibrationSample> Points { get; set; } = new();
    /// <summary>可选目标坐标旋转轨迹，使用TargetX/TargetY。</summary>
    public List<WorkflowCalibrationSample> RotationSamples { get; set; } = new();
    internal DP.Vision.Algorithms.AffineCalibration Solve(CancellationToken token = default) => CalibrationSolver.SolveAffine(
        Points.Select(p => new CalibrationSample(new Coordinate2D(p.SourceX, p.SourceY), new Coordinate2D(p.TargetX, p.TargetY))).ToArray(),
        RotationSamples.Select(p => new Coordinate2D(p.TargetX, p.TargetY)).ToArray(), token);
    /// <inheritdoc/>
    public IReadOnlyList<string> ValidateConfiguration()
    {
        if (Points is null || RotationSamples is null || Points.Any(p => p is null) || RotationSamples.Any(p => p is null)) return new[] { "标定点集合无效。" };
        try { _ = Solve(); return Array.Empty<string>(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return new[] { ex.Message }; }
    }
}

/// <summary>执行独立新版标定。</summary>
public sealed class SolveVisionCalibrationNodeHandler : WorkflowNodeHandler<SolveVisionCalibrationNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(SolveVisionCalibrationNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(NodeExecutionResult.Continue(output: node.Solve(cancellationToken)));
}

/// <summary>强类型标定与数值绑定坐标转换。</summary>
[WorkflowNode("Vision.MapCoordinate", DisplayName = "映射标定坐标", Category = "5.Vision/ImageBuffer")]
public sealed class MapVisionCoordinateNodeModel : WorkflowNodeModel, IWorkflowNodeConfigurationValidator
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.MapCoordinate";
    /// <summary>标定标准输出绑定。</summary>
    public WorkflowInput<AffineCalibration> Calibration { get; set; } = WorkflowInput<AffineCalibration>.FromLiteral(null);
    /// <summary>输入X，可绑定测量结果成员。</summary>
    public WorkflowInput<double> X { get; set; } = WorkflowInput<double>.FromLiteral(0);
    /// <summary>输入Y。</summary>
    public WorkflowInput<double> Y { get; set; } = WorkflowInput<double>.FromLiteral(0);
    /// <summary>绕目标旋转中心的弧度；非零必须存在旋转中心。</summary>
    [WorkflowProperty("旋转角度", "绕标定的目标旋转中心旋转；非零必须存在旋转中心。", Category = "坐标", DisplayRadiansAsDegrees = true)]
    public double RotationRadians { get; set; }
    /// <inheritdoc/>
    public IReadOnlyList<string> ValidateConfiguration() => Calibration is null || Calibration.Source != WorkflowValueSource.Binding
        || Calibration.Binding is null || Calibration.LiteralValue is not null || !double.IsFinite(RotationRadians)
        || X is null || Y is null || X.Validate() is not null || Y.Validate() is not null
        ? new[] { "必须绑定标定结果，坐标输入和旋转弧度必须有效。" } : Array.Empty<string>();
}

/// <summary>执行新版显式单位坐标映射。</summary>
public sealed class MapVisionCoordinateNodeHandler : WorkflowNodeHandler<MapVisionCoordinateNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(MapVisionCoordinateNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var calibration = context.ResolveInput(node.Calibration) ?? throw new InvalidOperationException("标定结果为空。");
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: calibration.TransformWithRotation(
            new Coordinate2D(context.ResolveInput(node.X), context.ResolveInput(node.Y)), node.RotationRadians)));
    }
}
