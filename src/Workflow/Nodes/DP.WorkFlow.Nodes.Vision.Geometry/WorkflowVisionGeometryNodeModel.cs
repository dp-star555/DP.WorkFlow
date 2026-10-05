using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>几何节点共享同帧输入及显式定位绑定，不使用面积ROI。</summary>
public abstract class WorkflowVisionGeometryNodeModel : AnalyzeVisionFrameNodeModel
{
    /// <inheritdoc/>
    public override EWorkflowVisionRange RangeCapability => EWorkflowVisionRange.GeometryFacts;
    /// <summary>几何事实只接受强类型绑定，不能持久化运行对象。</summary>
    protected static bool IsBound<T>(WorkflowInput<T>? input) => input is { Source: WorkflowValueSource.Binding, Binding: not null, LiteralValue: null };
}

/// <summary>几何计算节点通过本轮工厂绑定选择算法。</summary>
public abstract class WorkflowVisionGeometryAlgorithmNodeModel : WorkflowVisionGeometryNodeModel, IWorkflowVisionAlgorithmNode
{
    /// <summary>纯托管默认实现，也可选择同能力其他引擎。</summary>
    [System.ComponentModel.Browsable(false)]
    public VisionAlgorithmSelection Algorithm { get; set; } = new() { ImplementationId = "managed.geometry" };
    /// <inheritdoc/>
    public IReadOnlyList<WorkflowVisionAlgorithmSlot> GetAlgorithmSlots() => [new("algorithm", typeof(IGeometryMeasurer), Algorithm)];
}

internal static class WorkflowVisionGeometryExecution
{
    internal static (ImageFrame Frame, VisionCoordinateSystem? Coordinates) Resolve(AnalyzeVisionFrameNodeModel node, IWorkflowNodeExecutionContext context)
    {
        var frame = context.ResolveInput(node.Frame) ?? throw new InvalidOperationException("输入原图为空。");
        return (frame, node.ResolveCoordinates(frame, context));
    }
    internal static VisionPoint Align(VisionPoint? point, ImageFrame frame, VisionCoordinateSystem? coordinates)
    {
        if (point == null || point.FrameId != frame.FrameId) throw new InvalidOperationException("几何点为空或不属于当前帧。");
        if (point.CoordinateSystem is { } source) source.ValidateFrame(frame);
        return coordinates is null ? point : point.InCoordinates(coordinates);
    }
    internal static VisionLine Align(VisionLine? line, ImageFrame frame, VisionCoordinateSystem? coordinates)
    {
        if (line == null) throw new InvalidOperationException("直线输入为空，圆模型不提供直线。");
        return new VisionLine(Align(line.A, frame, coordinates), Align(line.B, frame, coordinates));
    }
    internal static ValueTask<NodeExecutionResult> Output(IWorkflowNodeExecutionContext context, ImageFrame frame, IVisionGeometryFact fact) =>
        ValueTask.FromResult(NodeExecutionResult.Continue(output: fact, projection: WorkflowVisionFrameScope.Stage(context, frame, fact)));
    internal static T Invoke<T>(IWorkflowNodeExecutionContext context, Func<IGeometryMeasurer, T> operation, CancellationToken token) =>
        context.GetRequiredCapability<IWorkflowVisionAlgorithmBindings>().Invoke(context, "algorithm", operation, token);
}
