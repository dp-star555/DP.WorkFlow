using System.ComponentModel;
using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>通用构建方式。</summary>
public enum EVisionCoordinateBuildMode
{
    /// <summary>模板匹配结果的参考点为原点、参考方向为X轴。</summary>
    [Description("模板匹配结果")]
    Template,
    /// <summary>原图中的原点、角度和尺度。</summary>
    [Description("原点+角度")]
    Pose,
    /// <summary>原点及正X方向点，配合已知局部长度。</summary>
    [Description("两点")]
    TwoPoints,
    /// <summary>两直线交点为原点，首线为正X。</summary>
    [Description("两线交点")]
    LineIntersection,
    /// <summary>固定业务到父坐标关系，与父到原图组合。</summary>
    [Description("相对父坐标系")]
    Parent,
    /// <summary>直接指定局部到原图仿射矩阵。</summary>
    [Description("仿射矩阵")]
    Matrix,
    /// <summary>局部到原图对应点，求解仿射标定。</summary>
    [Description("对应点标定")]
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

/// <summary>坐标系下拉的一项：坐标系身份及名称、版本、单位。</summary>
/// <param name="Id">坐标系稳定ID。</param><param name="Name">显示名称。</param><param name="Version">版本。</param><param name="Unit">单位。</param>
public sealed record WorkflowVisionCoordinateChoice(string Id, string Name, int Version, EVisionCoordinateUnit Unit);

/// <summary>定义坐标系（名称、版本、单位）并用本帧来源构建它；不持久化运行矩阵。</summary>
[WorkflowNode("Vision.BuildCoordinateSystem", DisplayName = "构建本帧坐标系", Category = WorkflowVisionCategories.Location)]
public sealed class BuildVisionCoordinateSystemNodeModel : WorkflowVisionGeometryNodeModel, IWorkflowNodeDocumentConfigurationValidator,
    IWorkflowVisionCoordinateProducerNode, IWorkflowDocumentPropertyChoices
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.BuildCoordinateSystem";
    /// <inheritdoc/>
    public override EWorkflowVisionRange RangeCapability => Mode == EVisionCoordinateBuildMode.Parent ? EWorkflowVisionRange.GeometryFacts : EWorkflowVisionRange.None;
    /// <summary>坐标系稳定ID；多个构建节点使用同一ID表示同一坐标系的不同来源。</summary>
    [System.ComponentModel.Browsable(false)]
    public string CoordinateId { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>显示名称。</summary>
    [WorkflowProperty("坐标系名称", "显示用，可随时修改，不影响已绑定的ROI。", Category = "坐标系")]
    public string CoordinateName { get; set; } = "工件坐标";
    /// <summary>坐标系版本。</summary>
    [WorkflowProperty("版本", "改变基准、单位或标定时递增；已绑定的下游ROI需要重新确认。", Category = "坐标系")]
    public int DefinitionVersion { get; set; } = 1;
    /// <summary>局部长度单位。</summary>
    [WorkflowProperty("单位", "毫米需要已知物理长度或有效标定，单位本身不产生标定。", Category = "坐标系")]
    public EVisionCoordinateUnit Unit { get; set; }
    /// <summary>从本文档已有坐标系中选择，或新建。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    [WorkflowProperty("坐标系", "选择本文档已有的坐标系：采用它的名称、版本和单位，本节点成为它的又一个来源；选择“新建坐标系”：新增一个坐标系。", Category = "坐标系")]
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.DocumentChoice)]
    public WorkflowVisionCoordinateChoice CoordinateSystem
    {
        get => new(CoordinateId, CoordinateName, DefinitionVersion, Unit);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            CoordinateId = value.Id; CoordinateName = value.Name; DefinitionVersion = value.Version; Unit = value.Unit;
        }
    }
    /// <inheritdoc/>
    public VisionCoordinateDefinition GetCoordinateDefinition() => new(CoordinateId, CoordinateName, DefinitionVersion, Unit);
    /// <inheritdoc/>
    public IReadOnlyList<KeyValuePair<string, object?>> GetPropertyChoices(string propertyName, IReadOnlyList<IWorkflowNodeModel> documentNodes)
    {
        if (propertyName != nameof(CoordinateSystem)) return [];
        var existing = documentNodes.OfType<BuildVisionCoordinateSystemNodeModel>().Where(n => n.Id != Id).Select(n => n.CoordinateSystem)
            .Prepend(CoordinateSystem).Distinct().ToArray();
        string Label(WorkflowVisionCoordinateChoice c)
        {
            var label = $"{c.Name}（v{c.Version}，{(c.Unit == EVisionCoordinateUnit.Millimeter ? "mm" : "reference-px")}）";
            // 同名的不同坐标系附上ID前缀以便区分。
            return existing.Any(o => o.Name == c.Name && o.Id != c.Id) ? $"{label} [{c.Id[..Math.Min(6, c.Id.Length)]}]" : label;
        }
        var fresh = new WorkflowVisionCoordinateChoice(Guid.NewGuid().ToString("N"), "坐标系" + (existing.Select(c => c.Id).Distinct().Count() + 1), 1, EVisionCoordinateUnit.ReferencePixel);
        return existing.Select(c => new KeyValuePair<string, object?>(Label(c), c))
            .Append(new KeyValuePair<string, object?>("新建坐标系", fresh)).ToArray();
    }
    /// <summary>构建来源。</summary>
    [WorkflowProperty("构建方式", "“模板匹配结果”以匹配参考点和方向为原点和X轴；“相对父坐标系”在上游坐标系上叠加固定关系；其余方式直接构建局部到原图。", Category = "坐标")]
    public EVisionCoordinateBuildMode Mode { get; set; }
    /// <summary>模板匹配结果。</summary>
    [WorkflowProperty("模板匹配结果", "绑定模板匹配节点的输出；原点取模板参考点，X轴取参考方向。模板参考变化后，下游ROI需重新确认。", Category = "模板")]
    [WorkflowPropertyVisibleWhen(nameof(Mode), "Template")]
    public WorkflowInput<TemplatePoseResult> Template { get; set; } = WorkflowInput<TemplatePoseResult>.FromLiteral(null);
    /// <summary>原点X。</summary>
    [WorkflowProperty("原点X", "“原点+角度”方式为原图像素；“相对父坐标系”方式为父坐标单位。", Category = "姿态")]
    [WorkflowPropertyVisibleWhen(nameof(Mode), "Pose", "Parent")]
    public WorkflowInput<double> OriginX { get; set; } = WorkflowInput<double>.FromLiteral(0);
    /// <summary>原点Y。</summary>
    [WorkflowProperty("原点Y", "“原点+角度”方式为原图像素；“相对父坐标系”方式为父坐标单位。", Category = "姿态")]
    [WorkflowPropertyVisibleWhen(nameof(Mode), "Pose", "Parent")]
    public WorkflowInput<double> OriginY { get; set; } = WorkflowInput<double>.FromLiteral(0);
    /// <summary>X轴方向，度，顺时针为正。</summary>
    [WorkflowProperty("角度(°)", "顺时针为正，单位度。“原点+角度”相对原图；“相对父坐标系”相对父坐标。可绑定模板匹配的角度输出。", Category = "姿态")]
    [WorkflowPropertyVisibleWhen(nameof(Mode), "Pose", "Parent")]
    public WorkflowInput<double> Angle { get; set; } = WorkflowInput<double>.FromLiteral(0);
    /// <summary>每局部单位对应的长度。</summary>
    [WorkflowProperty("尺度", "“原点+角度”和直线方式为原图像素每局部单位；“模板匹配结果”为模板像素每局部单位；“相对父坐标系”为父单位每局部单位。", Category = "姿态")]
    [WorkflowPropertyVisibleWhen(nameof(Mode), "Template", "Pose", "Parent", "LineIntersection")]
    public WorkflowInput<double> Scale { get; set; } = WorkflowInput<double>.FromLiteral(1);
    /// <summary>双点原点。</summary>
    [WorkflowProperty("原点输入", "“两点”方式的原点，同帧视觉点。", Category = "双点")]
    [WorkflowPropertyVisibleWhen(nameof(Mode), "TwoPoints")]
    public WorkflowInput<VisionPoint> OriginPoint { get; set; } = WorkflowInput<VisionPoint>.FromLiteral(null);
    /// <summary>双点方向。</summary>
    [WorkflowProperty("方向点输入", "“两点”方式正X方向上的同帧视觉点。", Category = "双点")]
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
    [WorkflowProperty("标定图像宽", "“对应点标定”适用的图像尺寸。", Category = "标定")]
    [WorkflowPropertyVisibleWhen(nameof(Mode), "Correspondences")]
    public int CalibrationImageWidth { get; set; }
    /// <summary>固定标定适用的图像高度。</summary>
    [WorkflowProperty("标定图像高", "尺寸变化会报错；光学条件变化需主动更新定义版本。", Category = "标定")]
    [WorkflowPropertyVisibleWhen(nameof(Mode), "Correspondences")]
    public int CalibrationImageHeight { get; set; }
    /// <summary>最大允许拟合误差。</summary>
    [WorkflowProperty("最大RMS", "“对应点标定”的原图像素误差阈值；不代表独立标定验证精度。", Category = "标定")]
    [WorkflowPropertyVisibleWhen(nameof(Mode), "Correspondences")]
    public double MaximumRms { get; set; } = 1;
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (string.IsNullOrWhiteSpace(CoordinateId) || string.IsNullOrWhiteSpace(CoordinateName) || DefinitionVersion < 1 || !Enum.IsDefined(Unit))
            errors.Add("坐标系名称不能为空，版本必须为正，单位必须有效。");
        if (!Enum.IsDefined(Mode)) errors.Add("坐标构建方式无效。");
        if (Mode == EVisionCoordinateBuildMode.Parent && Coordinates is null) errors.Add("Parent构建必须绑定本帧父坐标。");
        if (Mode != EVisionCoordinateBuildMode.Parent && Coordinates is not null) errors.Add("当前构建方式不使用父坐标，请解除多余坐标绑定。");
        if (Mode == EVisionCoordinateBuildMode.Template && !IsBound(Template)) errors.Add("模板方式需要绑定模板匹配结果。");
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
        if (Mode is EVisionCoordinateBuildMode.Template or EVisionCoordinateBuildMode.Pose or EVisionCoordinateBuildMode.Parent or EVisionCoordinateBuildMode.LineIntersection)
            if (Scale is null || Scale.Source == WorkflowValueSource.Literal && (!double.IsFinite(Scale.LiteralValue) || Scale.LiteralValue < 1e-6 || Scale.LiteralValue > 1e6)) errors.Add("尺度必须在1e-6..1e6。");
        if (Mode == EVisionCoordinateBuildMode.TwoPoints && (ReferenceLength is null || ReferenceLength.Source == WorkflowValueSource.Literal && (!double.IsFinite(ReferenceLength.LiteralValue) || ReferenceLength.LiteralValue <= 0))) errors.Add("参考长度必须有限且为正。");
        if (Mode is EVisionCoordinateBuildMode.Pose or EVisionCoordinateBuildMode.Parent)
        {
            foreach (var number in new[] { OriginX, OriginY, Angle })
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
        if (nodes.OfType<BuildVisionCoordinateSystemNodeModel>().Any(n => n.Id != Id && n.CoordinateId == CoordinateId && n.CoordinateSystem != CoordinateSystem))
            errors.Add($"坐标系“{CoordinateName}”在其它构建节点中的名称、版本或单位不同，请在“坐标系”下拉中重新选择以保持一致。");
        return errors;
    }
    /// <inheritdoc/>
    public VisionCoordinateDefinition? ResolveDefinition(IReadOnlyList<IWorkflowNodeModel> nodes)
    {
        var definition = GetCoordinateDefinition();
        if (Mode != EVisionCoordinateBuildMode.Template) return definition;
        // 已确认的资源模板可静态得到参考签名；动态模板图像的签名只有运行时才知道。
        return Template.Binding is { IsPublicData: false } source && nodes.FirstOrDefault(n => n.Id == source.NodeId) is IWorkflowVisionTemplateNode
            { TemplateSource: EWorkflowVisionTemplateSource.Resource, TemplateReferenceDefinition: { } reference }
            ? reference.Reference().Bind(definition) : null;
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
        var definition = node.GetCoordinateDefinition();
        double Value(WorkflowInput<double> input) => context.ResolveInput(input);
        PointD Origin() => new(Value(node.OriginX), Value(node.OriginY));
        double Radians() => Value(node.Angle) * Math.PI / 180;
        double? rms = null;
        VisionCoordinateSystem system;
        switch (node.Mode)
        {
            case EVisionCoordinateBuildMode.Template:
                var match = context.ResolveInput(node.Template) ?? throw new InvalidOperationException("模板匹配结果为空。");
                if (match.FrameId != frame.FrameId) throw new InvalidOperationException("模板匹配结果不属于本帧。");
                var reference = match.ReferenceToImage ?? throw new InvalidOperationException("模板未找到，不能构建本帧坐标系。");
                system = VisionCoordinateBuilder.FromMatrix(match.Reference.Bind(definition), frame,
                    reference.Multiply(VisionCoordinateBuilder.PoseMatrix(new PointD(0, 0), 0, Value(node.Scale))), "template:" + match.TemplateFrameId);
                break;
            case EVisionCoordinateBuildMode.Pose: system = VisionCoordinateBuilder.FromPose(definition, frame, Origin(), Radians(), Value(node.Scale)); break;
            case EVisionCoordinateBuildMode.Parent: system = VisionCoordinateBuilder.FromParent(definition, frame, parent ?? throw new InvalidOperationException("父坐标缺失。"), VisionCoordinateBuilder.PoseMatrix(Origin(), Radians(), Value(node.Scale))); break;
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
