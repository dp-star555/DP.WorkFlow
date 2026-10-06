using DP.Vision;
using DP.Vision.Algorithms;
using DP.Vision.UI;

namespace DP.WorkFlow.Vision.UI;

/// <summary>
/// 切换节点的坐标系绑定，并按本帧坐标系换算已保存的范围：面积ROI、卡尺端点（圆弧卡尺为圆心、半径与起始角）及间隔、鲁棒直线距离阈值、找线找圆的长度参数与起始角。
/// 换算经过同一张原图：旧局部（或原图）→ 原图 → 新局部（或原图），图上位置保持不变。
/// 需要节点输入图像和坐标来源已在本轮运行；几何点保持其显式输入空间，不改写数值。
/// </summary>
public static class VisionCoordinateRebinding
{
    /// <summary>绑定或更换坐标来源；同一坐标系的不同来源只改绑定，局部数值保持。</summary>
    /// <param name="node">要修改的节点（属性面板或编辑窗口的副本）。</param>
    /// <param name="sourceNodeId">已成功运行的坐标来源节点。</param>
    /// <param name="frames">本轮运行预览。</param>
    public static void Bind(AnalyzeVisionFrameNodeModel node, string sourceNodeId, IWorkflowVisionPreviewSource frames)
    {
        ArgumentNullException.ThrowIfNull(node); ArgumentNullException.ThrowIfNull(frames);
        if (!node.SupportsCoordinates) throw new InvalidOperationException("此节点不支持坐标系。");
        if (string.IsNullOrWhiteSpace(sourceNodeId) || sourceNodeId == node.Id) throw new InvalidOperationException("请选择其它节点构建的坐标系。");
        using var input = CaptureInput(node, frames);
        using var preview = frames.Capture(sourceNodeId);
        var target = (preview?.Facts as IVisionCoordinateResult)?.CoordinateSystem
            ?? throw new InvalidOperationException("所选坐标系还没有本帧结果，请先运行流程（坐标来源需成功构建）。");
        target.ValidateFrame(input.Frame);
        var current = node.Coordinates is { } binding ? Current(binding, input.Frame, frames) : null;
        if (current is null && node.RangeCapability == EWorkflowVisionRange.Region && !node.Regions.Any(r => r.Enabled && !r.Exclude))
            throw new InvalidOperationException("请先在“图像与测量范围”页绘制包含ROI，再选择坐标系。");
        // 同一坐标系定义的另一来源：局部表达含义相同，只换绑定。
        bool sameDefinition = current is not null && current.Definition.Signature == target.Definition.Signature;
        if (!sameDefinition) Apply(node, current, target);
        node.Coordinates = WorkflowVisionCoordinateBinding.Capture(sourceNodeId, target);
        node.FullImage = true;
    }

    /// <summary>解除坐标系，范围换算回当前原图表达。</summary>
    /// <param name="node">要修改的节点。</param>
    /// <param name="frames">本轮运行预览。</param>
    public static void Unbind(AnalyzeVisionFrameNodeModel node, IWorkflowVisionPreviewSource frames)
    {
        ArgumentNullException.ThrowIfNull(node); ArgumentNullException.ThrowIfNull(frames);
        if (node.Coordinates is not { } binding) return;
        using var input = CaptureInput(node, frames);
        Apply(node, Current(binding, input.Frame, frames), null);
        node.Coordinates = null; node.FullImage = true;
    }

    // 先算出全部新值再一次写入；任何一项失败都不留下半改的配置。
    private static void Apply(AnalyzeVisionFrameNodeModel node, VisionCoordinateSystem? from, VisionCoordinateSystem? to)
    {
        double fromScale = from?.SimilarityScaleOrOne(node) ?? 1, toScale = to?.SimilarityScaleOrOne(node) ?? 1;
        Coordinate2D Point(double x, double y)
        {
            var image = from?.LocalToImage.Map(new Coordinate2D(x, y)) ?? new Coordinate2D(x, y);
            return to?.ImageToLocal.Map(image) ?? image;
        }
        List<WorkflowVisionRoi>? regions = node.RangeCapability != EWorkflowVisionRange.Region ? null : node.Regions.Select(r =>
        {
            var geometry = r.ToGeometry();
            var image = from?.ToImageGeometry(geometry) ?? geometry;
            return MapRegion(new RoiDefinition(r.Id, to?.ToLocalGeometry(image) ?? image, r.Exclude ? ERoiPurpose.Exclude : ERoiPurpose.Include, r.Enabled));
        }).ToList();
        if (node is MeasureVisionCaliperNodeModel { Shape: EVisionCaliperShape.Arc } arc)
        {
            double ratio = fromScale / toScale;
            double fromRotation = from is null ? 0 : from.RotationRadians * 180 / Math.PI, toRotation = to is null ? 0 : to.RotationRadians * 180 / Math.PI;
            // 在原图表达中校验圆弧采样带，避免换算后才发现参数无效。
            var imageCenter = from?.LocalToImage.Map(new Coordinate2D(arc.CenterX, arc.CenterY)) ?? new Coordinate2D(arc.CenterX, arc.CenterY);
            _ = new VisionArcCaliperOptions(new PointD(imageCenter.X, imageCenter.Y), arc.Radius * fromScale, arc.StartAngle + fromRotation, arc.SweepAngle,
                arc.HalfWidth, arc.MinimumGradient, arc.Polarity, arc.MinimumSeparation * fromScale, arc.BandSampleStep * fromScale);
            var center = Point(arc.CenterX, arc.CenterY);
            arc.CenterX = center.X; arc.CenterY = center.Y; arc.Radius *= ratio;
            arc.StartAngle += fromRotation - toRotation;
            arc.MinimumSeparation *= ratio; arc.BandSampleStep *= ratio; arc.ScanStep *= ratio; arc.FitDistanceThreshold *= ratio;
        }
        else if (node is MeasureVisionCaliperNodeModel caliper)
        {
            var start = Point(caliper.StartX, caliper.StartY); var end = Point(caliper.EndX, caliper.EndY);
            double ratio = fromScale / toScale;
            // 在原图表达中校验采样带，避免换算后才发现参数无效。
            var imageStart = from?.LocalToImage.Map(new Coordinate2D(caliper.StartX, caliper.StartY)) ?? new Coordinate2D(caliper.StartX, caliper.StartY);
            var imageEnd = from?.LocalToImage.Map(new Coordinate2D(caliper.EndX, caliper.EndY)) ?? new Coordinate2D(caliper.EndX, caliper.EndY);
            _ = new CaliperOptions(new PointD(imageStart.X, imageStart.Y), new PointD(imageEnd.X, imageEnd.Y), caliper.HalfWidth, caliper.MinimumGradient,
                caliper.Polarity, caliper.MinimumSeparation * fromScale, caliper.BandSampleStep * fromScale);
            caliper.StartX = start.X; caliper.StartY = start.Y; caliper.EndX = end.X; caliper.EndY = end.Y;
            caliper.MinimumSeparation *= ratio; caliper.BandSampleStep *= ratio; caliper.ScanStep *= ratio; caliper.FitDistanceThreshold *= ratio;
        }
        if (node is FitVisionRobustLineNodeModel fit) fit.DistanceThreshold *= fromScale / toScale;
        if (node is FindVisionShapeNodeModel shape)
        {
            // 搜索ROI随 Regions 换算；这里换算卡尺与拟合的长度参数，找圆的起始角随坐标系方向调整。
            double ratio = fromScale / toScale;
            shape.BandSampleStep *= ratio; shape.MinimumSeparation *= ratio; shape.DistanceThreshold *= ratio;
            if (shape is FindVisionCircleNodeModel circle)
            {
                circle.SearchLength *= ratio;
                circle.StartAngle += ((from?.RotationRadians ?? 0) - (to?.RotationRadians ?? 0)) * 180 / Math.PI;
            }
        }
        if (regions is not null) node.Regions = regions;
    }

    // 卡尺、鲁棒拟合与找线找圆用单一尺度换算距离参数，要求相似变换；其它节点不需要尺度。
    private static double SimilarityScaleOrOne(this VisionCoordinateSystem system, AnalyzeVisionFrameNodeModel node) =>
        node is MeasureVisionCaliperNodeModel or FitVisionRobustLineNodeModel or FindVisionShapeNodeModel ? system.SimilarityScale : 1;

    private static WorkflowVisionPreview CaptureInput(AnalyzeVisionFrameNodeModel node, IWorkflowVisionPreviewSource frames)
    {
        if (node.Frame.Binding is not { IsPublicData: false } binding)
            throw new InvalidOperationException("换算范围需要节点图像绑定上游节点；公共数据绑定可运行，但无法确定预览图像。");
        return frames.Capture(binding.NodeId) ?? throw new InvalidOperationException("尚无输入图像结果，请先运行流程。");
    }

    private static VisionCoordinateSystem Current(WorkflowVisionCoordinateBinding binding, ImageFrame frame, IWorkflowVisionPreviewSource frames)
    {
        if (binding.System.Binding is not { IsPublicData: false } source || source.MemberPath != "CoordinateSystem")
            throw new InvalidOperationException("当前坐标系绑定不是直接来自坐标来源节点，无法换算。");
        using var preview = frames.Capture(source.NodeId);
        var system = (preview?.Facts as IVisionCoordinateResult)?.CoordinateSystem
            ?? throw new InvalidOperationException("当前坐标系本帧没有结果，无法换算范围；请先运行流程。");
        binding.Validate(system, frame);
        return system;
    }

    /// <summary>图上ROI转成节点保存的ROI配置。</summary>
    /// <param name="roi">ROI定义。</param><returns>配置项。</returns>
    internal static WorkflowVisionRoi MapRegion(RoiDefinition roi)
    {
        var value = new WorkflowVisionRoi { Id = roi.Id, Enabled = roi.Enabled, Exclude = roi.Purpose == ERoiPurpose.Exclude };
        switch (roi.Shape)
        {
            case RectangleGeometry r:
                value.Shape = EWorkflowVisionRoiShape.Rectangle; value.CenterX = r.Center.X; value.CenterY = r.Center.Y;
                value.Width = r.Width; value.Height = r.Height; value.Angle = r.Angle; break;
            case EllipseGeometry e:
                value.Shape = EWorkflowVisionRoiShape.Ellipse; value.CenterX = e.Center.X; value.CenterY = e.Center.Y;
                value.Width = e.RadiusX * 2; value.Height = e.RadiusY * 2; value.Angle = e.Angle; break;
            case ContourGeometry { Closed: true, Filled: true } c:
                value.Shape = EWorkflowVisionRoiShape.Polygon;
                value.Points = c.Points.Select(p => new WorkflowVisionRoiPoint { X = p.X, Y = p.Y }).ToList(); break;
            default: throw new ArgumentException("开放轮廓或点不能作为面积ROI，未修改节点参数。");
        }
        return value;
    }
}
