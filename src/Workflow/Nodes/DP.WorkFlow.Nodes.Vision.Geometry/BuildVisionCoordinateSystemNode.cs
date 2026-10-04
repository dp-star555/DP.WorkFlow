using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>通用构建方式；模板定位可以作为父坐标输入。</summary>
public enum EVisionCoordinateBuildMode
{
    /// <summary>原图中的原点、角度和尺度。</summary>
    Pose,
    /// <summary>原点及正X方向点，配合已知局部长度。</summary>
    TwoPoints,
    /// <summary>两直线交点为原点，首线为正X。</summary>
    LineIntersection,
    /// <summary>固定业务到父坐标关系，与父到原图组合。</summary>
    Parent,
    /// <summary>直接指定局部到原图仿射矩阵。</summary>
    Matrix,
    /// <summary>局部到原图对应点，求解仿射标定。</summary>
    Correspondences
}

/// <summary>可保存的标定点对。</summary>
public sealed class WorkflowVisionCoordinateSample
{
    /// <summary>局部X。</summary>
    public double LocalX { get; set; }
    /// <summary>局部Y。</summary>
    public double LocalY { get; set; }
    /// <summary>原图X。</summary>
    public double ImageX { get; set; }
    /// <summary>原图Y。</summary>
    public double ImageY { get; set; }
}

/// <summary>将独立业务定义与本帧来源组合；不持久化运行矩阵。</summary>
[WorkflowNode("Vision.BuildCoordinateSystem", DisplayName = "构建本帧坐标系", Category = "5.Vision/Coordinates")]
public sealed class BuildVisionCoordinateSystemNodeModel : WorkflowVisionGeometryNodeModel, IWorkflowNodeDocumentConfigurationValidator,
    IWorkflowVisionCoordinateProducerNode
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.BuildCoordinateSystem";
    /// <inheritdoc/>
    public override EWorkflowVisionRange RangeCapability => Mode == EVisionCoordinateBuildMode.Parent ? EWorkflowVisionRange.GeometryFacts : EWorkflowVisionRange.None;
    /// <summary>稳定定义的直接输出。</summary>
    [WorkflowProperty("坐标定义", "绑定同文档的定义坐标系节点。", Category = "坐标")]
    public WorkflowInput<VisionCoordinateDefinition> Definition { get; set; } = WorkflowInput<VisionCoordinateDefinition>.FromLiteral(null);
    /// <summary>构建来源。</summary>
    [WorkflowProperty("构建方式", "Parent使用上游坐标绑定，其余方式直接构建局部到原图。", Category = "坐标")]
    public EVisionCoordinateBuildMode Mode { get; set; }
    /// <summary>原点X。</summary>
    [WorkflowProperty("原点X", "Pose为原图像素；Parent为父坐标单位。", Category = "姿态")]
    [WorkflowPropertyVisibleWhen(nameof(Mode), "Pose", "Parent")]
    public WorkflowInput<double> OriginX { get; set; } = WorkflowInput<double>.FromLiteral(0);
    /// <summary>原点Y。</summary>
    [WorkflowProperty("原点Y", "Pose为原图像素；Parent为父坐标单位。", Category = "姿态")]
    [WorkflowPropertyVisibleWhen(nameof(Mode), "Pose", "Parent")]
    public WorkflowInput<double> OriginY { get; set; } = WorkflowInput<double>.FromLiteral(0);
    /// <summary>顺时针角度。</summary>
    [WorkflowProperty("角度(弧度)", "Pose相对原图；Parent相对父坐标。", Category = "姿态")]
    [WorkflowPropertyVisibleWhen(nameof(Mode), "Pose", "Parent")]
    public WorkflowInput<double> AngleRadians { get; set; } = WorkflowInput<double>.FromLiteral(0);
    /// <summary>父坐标或原图单位/局部单位。</summary>
    [WorkflowProperty("尺度", "Pose/直线方式为原图像素每局部单位；Parent为父单位每局部单位。", Category = "姿态")]
    [WorkflowPropertyVisibleWhen(nameof(Mode), "Pose", "Parent", "LineIntersection")]
    public WorkflowInput<double> Scale { get; set; } = WorkflowInput<double>.FromLiteral(1);
    /// <summary>双点原点。</summary>
    [WorkflowProperty("原点输入", "TwoPoints模式的同帧视觉点。", Category = "双点")]
    [WorkflowPropertyVisibleWhen(nameof(Mode), "TwoPoints")]
    public WorkflowInput<VisionPoint> OriginPoint { get; set; } = WorkflowInput<VisionPoint>.FromLiteral(null);
    /// <summary>双点方向。</summary>
    [WorkflowProperty("方向点输入", "TwoPoints模式正X方向的同帧视觉点。", Category = "双点")]
    [WorkflowPropertyVisibleWhen(nameof(Mode), "TwoPoints")]
    public WorkflowInput<VisionPoint> DirectionPoint { get; set; } = WorkflowInput<VisionPoint>.FromLiteral(null);
    /// <summary>已知参考长度。</summary>
    [WorkflowProperty("参考长度", "双点在局部单位中的已知距离；毫米必须使用实际测得的长度。", Category = "双点")]
    [WorkflowPropertyVisibleWhen(nameof(Mode), "TwoPoints")]
    public WorkflowInput<double> ReferenceLength { get; set; } = WorkflowInput<double>.FromLiteral(1);
    /// <summary>正X基准线。</summary>
    [WorkflowProperty("X轴线输入", "首线A至B的方向为正X。", Category = "直线")]
    [WorkflowPropertyVisibleWhen(nameof(Mode), "LineIntersection")]
    public WorkflowInput<VisionLine> AxisLine { get; set; } = WorkflowInput<VisionLine>.FromLiteral(null);
    /// <summary>与X轴相交的基准线。</summary>
    [WorkflowProperty("交线输入", "两线延长线交点为原点，近平行拒绝构建。", Category = "直线")]
    [WorkflowPropertyVisibleWhen(nameof(Mode), "LineIntersection")]
    public WorkflowInput<VisionLine> CrossLine { get; set; } = WorkflowInput<VisionLine>.FromLiteral(null);
    /// <summary>矩阵第一行。</summary>
    [WorkflowPropertyVisibleWhen(nameof(Mode), "Matrix")]
    public WorkflowInput<double> M11 { get; set; } = WorkflowInput<double>.FromLiteral(1);
    /// <summary>矩阵第一行第二列。</summary>
    [WorkflowPropertyVisibleWhen(nameof(Mode), "Matrix")]
    public WorkflowInput<double> M12 { get; set; } = WorkflowInput<double>.FromLiteral(0);
    /// <summary>原图平移X。</summary>
    [WorkflowPropertyVisibleWhen(nameof(Mode), "Matrix")]
    public WorkflowInput<double> Tx { get; set; } = WorkflowInput<double>.FromLiteral(0);
    /// <summary>矩阵第二行第一列。</summary>
    [WorkflowPropertyVisibleWhen(nameof(Mode), "Matrix")]
    public WorkflowInput<double> M21 { get; set; } = WorkflowInput<double>.FromLiteral(0);
    /// <summary>矩阵第二行第二列。</summary>
    [WorkflowPropertyVisibleWhen(nameof(Mode), "Matrix")]
    public WorkflowInput<double> M22 { get; set; } = WorkflowInput<double>.FromLiteral(1);
    /// <summary>原图平移Y。</summary>
    [WorkflowPropertyVisibleWhen(nameof(Mode), "Matrix")]
    public WorkflowInput<double> Ty { get; set; } = WorkflowInput<double>.FromLiteral(0);
    /// <summary>至少三个不共线的局部/原图对应点。</summary>
    [WorkflowPropertyVisibleWhen(nameof(Mode), "Correspondences")]
    public List<WorkflowVisionCoordinateSample> Samples { get; set; } = [];
    /// <summary>固定标定适用的图像宽度。</summary>
    [WorkflowProperty("标定图像宽", "Correspondences固定标定适用的图像尺寸。", Category = "标定")]
    [WorkflowPropertyVisibleWhen(nameof(Mode), "Correspondences")]
    public int CalibrationImageWidth { get; set; }
    /// <summary>固定标定适用的图像高度。</summary>
    [WorkflowProperty("标定图像高", "尺寸变化会报错；光学条件变化需主动更新定义版本。", Category = "标定")]
    [WorkflowPropertyVisibleWhen(nameof(Mode), "Correspondences")]
    public int CalibrationImageHeight { get; set; }
    /// <summary>最大允许拟合误差。</summary>
    [WorkflowProperty("最大RMS", "Correspondences原图像素误差阈值；不代表独立标定验证精度。", Category = "标定")]
    [WorkflowPropertyVisibleWhen(nameof(Mode), "Correspondences")]
    public double MaximumRms { get; set; } = 1;
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (!IsBound(Definition) || Definition.Binding is not { IsPublicData: false } definitionBinding || definitionBinding.MemberPath is not ("" or "$")) errors.Add("坐标定义必须直接绑定本文档的定义节点。");
        if (!Enum.IsDefined(Mode)) errors.Add("坐标构建方式无效。");
        if (Mode == EVisionCoordinateBuildMode.Parent && Coordinates is null) errors.Add("Parent构建必须绑定本帧父坐标。");
        if (Mode != EVisionCoordinateBuildMode.Parent && Coordinates is not null) errors.Add("当前构建方式不使用父坐标，请解除多余坐标绑定。");
        if (Mode == EVisionCoordinateBuildMode.TwoPoints && (!IsBound(OriginPoint) || !IsBound(DirectionPoint))) errors.Add("双点构建需要绑定两个视觉点。");
        if (Mode == EVisionCoordinateBuildMode.LineIntersection && (!IsBound(AxisLine) || !IsBound(CrossLine))) errors.Add("交线构建需要绑定两条视觉线。");
        if (Mode == EVisionCoordinateBuildMode.Correspondences)
        {
            if (Samples is null || Samples.Count < 3 || Samples.Count > 1024 || Samples.Any(s => s is null || !double.IsFinite(s.LocalX) || !double.IsFinite(s.LocalY) || !double.IsFinite(s.ImageX) || !double.IsFinite(s.ImageY))) errors.Add("标定必须有3..1024个有限的对应点。");
            else try
            {
                var solved = CalibrationSolver.SolveAffine(Samples.Select(s => new CalibrationSample(new Coordinate2D(s.LocalX, s.LocalY), new Coordinate2D(s.ImageX, s.ImageY))).ToArray());
                _ = CoordinateMatrix2D.FromAffine(solved.M11, solved.M12, solved.Tx, solved.M21, solved.M22, solved.Ty).Inverse();
                if (solved.RmsError > MaximumRms) errors.Add("标定拟合误差超过最大RMS。");
            }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { errors.Add(ex.Message); }
            if (CalibrationImageWidth < 1 || CalibrationImageHeight < 1 || !double.IsFinite(MaximumRms) || MaximumRms < 0) errors.Add("标定适用尺寸必须为正，RMS阈值必须有限非负。");
        }
        if (Mode is EVisionCoordinateBuildMode.Pose or EVisionCoordinateBuildMode.Parent or EVisionCoordinateBuildMode.LineIntersection)
            if (Scale is null || Scale.Source == WorkflowValueSource.Literal && (!double.IsFinite(Scale.LiteralValue) || Scale.LiteralValue < 1e-6 || Scale.LiteralValue > 1e6)) errors.Add("尺度必须在1e-6..1e6。");
        if (Mode == EVisionCoordinateBuildMode.TwoPoints && (ReferenceLength is null || ReferenceLength.Source == WorkflowValueSource.Literal && (!double.IsFinite(ReferenceLength.LiteralValue) || ReferenceLength.LiteralValue <= 0))) errors.Add("参考长度必须有限且为正。");
        if (Mode is EVisionCoordinateBuildMode.Pose or EVisionCoordinateBuildMode.Parent)
        {
            foreach (var number in new[] { OriginX, OriginY, AngleRadians })
                if (number is null || number.Source == WorkflowValueSource.Literal && (!double.IsFinite(number.LiteralValue) || Math.Abs(number.LiteralValue) > 1e9)) errors.Add("姿态分量必须有限且不超过正负十亿。");
        }
        if (Mode == EVisionCoordinateBuildMode.Matrix)
        {
            var numbers = new[] { M11, M12, Tx, M21, M22, Ty };
            if (numbers.Any(n => n is null || n.Source == WorkflowValueSource.Literal && !double.IsFinite(n.LiteralValue))) errors.Add("矩阵分量必须有限。");
            else if (numbers.All(n => n.Source == WorkflowValueSource.Literal))
                try { _ = CoordinateMatrix2D.FromAffine(M11.LiteralValue, M12.LiteralValue, Tx.LiteralValue, M21.LiteralValue, M22.LiteralValue, Ty.LiteralValue).Inverse(); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { errors.Add(ex.Message); }
        }
        return errors;
    }
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateDocumentConfiguration(IReadOnlyList<IWorkflowNodeModel> nodes)
    {
        var errors = base.ValidateDocumentConfiguration(nodes).ToList();
        try { _ = WorkflowVisionCoordinateCatalog.ResolveDefinition(nodes, Definition); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { errors.Add(ex.Message); }
        return errors;
    }
}

/// <summary>构建同帧坐标，检查退化/残差，再发布有身份的矩阵。</summary>
public sealed class BuildVisionCoordinateSystemNodeHandler : WorkflowNodeHandler<BuildVisionCoordinateSystemNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(BuildVisionCoordinateSystemNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (frame, parent) = WorkflowVisionGeometryExecution.Resolve(node, context);
        var definition = context.ResolveInput(node.Definition) ?? throw new InvalidOperationException("坐标定义不存在。");
        double Value(WorkflowInput<double> input) => context.ResolveInput(input);
        PointD Origin() => new(Value(node.OriginX), Value(node.OriginY));
        double? rms = null;
        VisionCoordinateSystem system;
        switch (node.Mode)
        {
            case EVisionCoordinateBuildMode.Pose: system = VisionCoordinateBuilder.FromPose(definition, frame, Origin(), Value(node.AngleRadians), Value(node.Scale)); break;
            case EVisionCoordinateBuildMode.Parent: system = VisionCoordinateBuilder.FromParent(definition, frame, parent ?? throw new InvalidOperationException("父坐标缺失。"), VisionCoordinateBuilder.PoseMatrix(Origin(), Value(node.AngleRadians), Value(node.Scale))); break;
            case EVisionCoordinateBuildMode.TwoPoints: system = VisionCoordinateBuilder.FromTwoPoints(definition, frame, context.ResolveInput(node.OriginPoint) ?? throw new InvalidOperationException("原点为空。"), context.ResolveInput(node.DirectionPoint) ?? throw new InvalidOperationException("方向点为空。"), Value(node.ReferenceLength)); break;
            case EVisionCoordinateBuildMode.LineIntersection: system = VisionCoordinateBuilder.FromLines(definition, frame, context.ResolveInput(node.AxisLine) ?? throw new InvalidOperationException("X轴线为空。"), context.ResolveInput(node.CrossLine) ?? throw new InvalidOperationException("交线为空。"), Value(node.Scale)); break;
            case EVisionCoordinateBuildMode.Matrix: system = VisionCoordinateBuilder.FromMatrix(definition, frame, CoordinateMatrix2D.FromAffine(Value(node.M11), Value(node.M12), Value(node.Tx), Value(node.M21), Value(node.M22), Value(node.Ty))); break;
            case EVisionCoordinateBuildMode.Correspondences:
                if (frame.Image.Info.Width != node.CalibrationImageWidth || frame.Image.Info.Height != node.CalibrationImageHeight) throw new InvalidOperationException("当前图像尺寸不符合标定适用范围。");
                system = VisionCoordinateBuilder.FromCorrespondences(definition, frame, node.Samples.Select(s => new CalibrationSample(new Coordinate2D(s.LocalX, s.LocalY), new Coordinate2D(s.ImageX, s.ImageY))), out double error, cancellationToken);
                rms = error;
                if (error > node.MaximumRms) throw new InvalidOperationException("标定拟合误差超过最大RMS。");
                break;
            default: throw new InvalidOperationException("坐标构建方式无效。");
        }
        var result = new VisionCoordinateSystemResult(system, rms);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result, projection: WorkflowVisionFrameScope.Stage(context, frame, result)));
    }
}
