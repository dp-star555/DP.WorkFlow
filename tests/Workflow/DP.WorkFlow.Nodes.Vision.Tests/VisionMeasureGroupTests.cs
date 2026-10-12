using DP.Vision;
using DP.Vision.Algorithms;
using DP.WorkFlow.Persistence.Json;

namespace DP.WorkFlow.Tests;

/// <summary>测量组：同一张图上多个找线/找圆/找矩形项，按失败策略汇总，下游按测量项标识绑定。</summary>
public sealed class VisionMeasureGroupTests
{
    // 亮背景上：暗矩形（中心 (150,120)，140×90，转 10°）和暗圆盘（圆心 (40,40)，半径 20）；右下角是空白。
    private static ImageFrame Scene()
    {
        const int width = 300, height = 240;
        double angle = 10 * Math.PI / 180, cos = Math.Cos(angle), sin = Math.Sin(angle);
        var pixels = new byte[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                double dx = x + .5 - 150, dy = y + .5 - 120, u = dx * cos + dy * sin, v = -dx * sin + dy * cos;
                bool rectangle = Math.Abs(u) <= 70 && Math.Abs(v) <= 45;
                bool disk = (x + .5 - 40) * (x + .5 - 40) + (y + .5 - 40) * (y + .5 - 40) <= 400;
                pixels[y * width + x] = rectangle || disk ? (byte)20 : (byte)220;
            }
        using var image = VisionImage.CopyFrom(new ImageInfo(width, height, EPixelLayout.Gray8), pixels);
        return new ImageFrame("scene", image);
    }

    private static MeasureVisionGroupNodeModel Group(EVisionMeasureFailurePolicy policy = EVisionMeasureFailurePolicy.AllowPartial)
    {
        var frame = VisionMeasureItem.Create(EVisionMeasureKind.Rectangle, "外框");
        frame.Rectangle!.CaliperCount = 8; frame.Rectangle.HalfWidth = 3; frame.Rectangle.SearchLength = 24; frame.Rectangle.Polarity = ECaliperPolarity.Rising;
        frame.Rectangle.Regions = new() { new() { Id = "search", Shape = EWorkflowVisionRoiShape.Rectangle, CenterX = 152, CenterY = 118, Width = 136, Height = 94, Angle = 8 * Math.PI / 180 } };
        var hole = VisionMeasureItem.Create(EVisionMeasureKind.Circle, "孔");
        hole.Circle!.CaliperCount = 12; hole.Circle.SearchLength = 12; hole.Circle.HalfWidth = 2; hole.Circle.Polarity = ECaliperPolarity.Rising;
        hole.Circle.Regions = new() { new() { Id = "search", Shape = EWorkflowVisionRoiShape.Ellipse, CenterX = 41, CenterY = 39, Width = 38, Height = 38 } };
        var empty = VisionMeasureItem.Create(EVisionMeasureKind.Line, "空白边");
        empty.Line!.CaliperCount = 5;
        empty.Line.Regions = new() { new() { Id = "search", Shape = EWorkflowVisionRoiShape.Rectangle, CenterX = 260, CenterY = 215, Width = 50, Height = 20 } };
        var disabled = VisionMeasureItem.Create(EVisionMeasureKind.Line, "停用");
        disabled.Enabled = false;
        return new MeasureVisionGroupNodeModel
        {
            Id = "group", Frame = WorkflowInput<ImageFrame>.FromBinding(WorkflowBindingKey.FromPublicData("frame")),
            FailurePolicy = policy, Items = { frame, hole, empty, disabled }
        };
    }

    private static WorkflowNodeTrialRunner Runner() => new(new WorkflowNodeHandlerCatalog().Register(new MeasureVisionGroupNodeHandler()),
        new WorkflowContext(new WorkflowServiceProvider().Add<ICaliperMeasurer>(new CaliperMeasurer())
            .Add<IRobustLineFitter>(new RobustLineFitter()).Add<IRobustCircleFitter>(new RobustCircleFitter())));

    private static Dictionary<object, object?> Input(MeasureVisionGroupNodeModel node, ImageFrame frame) =>
        new(ReferenceEqualityComparer.Instance) { [node.Frame] = frame };

    [Fact]
    public async Task AllowPartial_MeasuresEachItemAndMarksTheFailedOne()
    {
        using var frame = Scene();
        var node = Group();
        Assert.Empty(node.ValidateConfiguration());

        var output = await Runner().RunAsync(node, Input(node, frame));

        Assert.True(output.Success, output.Message);
        var result = Assert.IsType<VisionMeasureGroupResult>(output.Output);
        Assert.Equal(3, result.Items.Count); // 停用项不测量。
        Assert.Equal((2, 1, "空白边"), (result.FoundCount, result.FailedCount, result.FailedNames));
        Assert.False(result.AllFound);
        var rectangle = Assert.IsType<VisionFindRectangleResult>(result.Items.Find(node.Items[0].Id)!.Value);
        Assert.InRange(rectangle.Width, 139.5, 140.5);
        var circle = Assert.IsType<VisionFindCircleResult>(result.Items.Find(node.Items[1].Id)!.Value);
        Assert.InRange(circle.Radius, 19.5, 20.5);
        var failed = result.Items.Find(node.Items[2].Id)!;
        Assert.False(failed.Found);
        Assert.Contains("找线失败", failed.Error);
        // 每项的主结果标签带项名；失败项的卡尺画成 NG 并标“未找到”。
        Assert.Contains(result.DisplayItems, d => d.Label == FormattableString.Invariant($"外框 {rectangle.Width:F2}×{rectangle.Height:F2}"));
        Assert.Contains(result.DisplayItems, d => d.Label == "空白边 未找到" && d.Tone == EVisionDisplayTone.Ng);
        Assert.Equal(5, result.DisplayItems.Count(d => d.Tone == EVisionDisplayTone.Ng && d.Role == EVisionDisplayRole.Input));
        Assert.Contains("失败：空白边", result.Summary);
        // 测量项配置不保存输入图像：测量结束后恢复为空。
        Assert.Null(node.Items[0].Rectangle!.Frame.Binding);
    }

    [Fact]
    public async Task RequireAll_FailsWithTheItemName()
    {
        using var frame = Scene();
        var node = Group(EVisionMeasureFailurePolicy.RequireAll);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Runner().RunAsync(node, Input(node, frame)));
        Assert.StartsWith("测量项 空白边：找线失败", error.Message);
    }

    [Fact]
    public async Task Binding_ReadsItemMembersByIdAndExplainsFailedItems()
    {
        using var frame = Scene();
        var node = Group();
        var output = await Runner().RunAsync(node, Input(node, frame));
        var context = new WorkflowContext();
        // 用编译引擎跑一个产出该结果的节点，再按绑定路径读取。
        var document = new WorkflowDocument { Name = "测量组绑定" };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new ProduceNode { Id = "group" } });
        document.EntryNodeId = "group";
        var run = await new WorkflowEngine(new WorkflowCompiler(validateBindings: false).Compile(document),
            new WorkflowNodeHandlerCatalog().Register(new ProduceHandler(output.Output!)), context).RunAsync();
        Assert.True(run.Success, run.Message);

        string rectangle = node.Items[0].Id, empty = node.Items[2].Id;
        Assert.InRange(WorkflowBindingResolver.ResolveLatest<double>(context, new("group", $"Items.{rectangle}.Width")), 139.5, 140.5);
        Assert.IsType<VisionPoint>(WorkflowBindingResolver.ResolveLatest<VisionPoint>(context, new("group", $"Items.{rectangle}.RectangleCenter")));
        Assert.True(WorkflowBindingResolver.ResolveLatest<bool>(context, new("group", $"Items.{rectangle}.Found")));
        Assert.False(WorkflowBindingResolver.ResolveLatest<bool>(context, new("group", $"Items.{empty}.Found")));
        Assert.Equal(1, WorkflowBindingResolver.ResolveLatest<int>(context, new("group", "FailedCount")));
        var failed = Assert.Throws<WorkflowBindingException>(() => WorkflowBindingResolver.ResolveLatest<VisionLine>(context, new("group", $"Items.{empty}.MeasuredLine")));
        Assert.Contains("测量项 空白边 未找到", failed.Message);
        var deleted = Assert.Throws<WorkflowBindingException>(() => WorkflowBindingResolver.ResolveLatest<double>(context, new("group", "Items.m00000000.Width")));
        Assert.Contains("不存在", deleted.Message);
    }

    [Fact]
    public void DescribeOutputs_ListsEnabledItemsWithTheirOwnMembers()
    {
        var node = Group();
        var members = node.DescribeOutputs().ToDictionary(m => m.Path);
        string rectangle = node.Items[0].Id, hole = node.Items[1].Id, line = node.Items[2].Id;
        Assert.Equal(typeof(double), members[$"Items.{rectangle}.Width"].Type);
        Assert.Equal("外框 · 宽度", members[$"Items.{rectangle}.Width"].DisplayName);
        Assert.Equal("孔 · 是否找到", members[$"Items.{hole}.Found"].DisplayName);
        Assert.Equal(typeof(VisionLine), members[$"Items.{line}.MeasuredLine"].Type);
        Assert.Equal(typeof(bool), members["AllFound"].Type);
        Assert.DoesNotContain(members.Keys, p => p.Contains(node.Items[3].Id, StringComparison.Ordinal)); // 停用项。
        Assert.DoesNotContain(members.Keys, p => p.EndsWith(".Value", StringComparison.Ordinal));
        // 说明之外更深的路径按成员类型继续解析；改了类型的绑定解析为新类型的成员（找不到则为空）。
        Assert.Equal(typeof(double), WorkflowOutputMembers.Resolve(node, typeof(VisionMeasureGroupResult), $"Items.{line}.MeasuredLine.A.ImagePosition.X"));
        Assert.Null(WorkflowOutputMembers.Resolve(node, typeof(VisionMeasureGroupResult), $"Items.{hole}.MeasuredLine"));
    }

    [Fact]
    public void Validation_ReportsItemProblemsByName()
    {
        var node = Group();
        node.Items[1].Name = "外框";
        node.Items[2].Id = "bad id";
        node.Items[0].Rectangle!.SearchLength = 500;
        node.Items[1].Kind = EVisionMeasureKind.Line; // 类型与配置不符：没有找线配置。
        var errors = node.ValidateConfiguration();
        Assert.Contains(errors, e => e.Contains("名称不能为空或重复", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("标识缺失、格式无效或重复", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("测量项 外框：搜索长度必须小于期望矩形的短边", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("缺少找线配置", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, e => e.Contains("输入图像", StringComparison.Ordinal)); // 组的输入图像已绑定，测量项不重复报。
        Assert.Contains(new MeasureVisionGroupNodeModel().ValidateConfiguration(), e => e.Contains("1..64", StringComparison.Ordinal));
    }

    [Fact]
    public void Json_RoundTripKeepsItemsAndIds()
    {
        var catalog = new WorkflowNodeCatalog().RegisterImageNodes();
        var node = Group();
        var document = new WorkflowDocument { Name = "测量组" };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        document.EntryNodeId = node.Id;
        var store = new WorkflowDocumentJsonStore(catalog);
        var loaded = (MeasureVisionGroupNodeModel)store.Deserialize(store.Serialize(document)).Document.CanvasProjection.Nodes.Single().Node;
        Assert.Equal(node.Items.Select(i => (i.Id, i.Name, i.Kind, i.Enabled)), loaded.Items.Select(i => (i.Id, i.Name, i.Kind, i.Enabled)));
        Assert.Equal(24, loaded.Items[0].Rectangle!.SearchLength);
        Assert.Equal(38, loaded.Items[1].Circle!.Regions[0].Width);
        Assert.Empty(loaded.ValidateConfiguration());
    }

    private sealed class ProduceNode : WorkflowNodeModel { public override string NodeType => "Test.Produce"; }

    private sealed class ProduceHandler(object output) : WorkflowNodeHandler<ProduceNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(ProduceNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(NodeExecutionResult.Continue(output: output));
    }
}
