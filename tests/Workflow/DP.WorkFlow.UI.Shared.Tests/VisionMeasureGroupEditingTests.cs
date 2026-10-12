using DP.Vision;
using DP.Vision.Algorithms;
using DP.WorkFlow.Vision.UI;

namespace DP.WorkFlow.Tests;

/// <summary>测量组的编辑模型：测量项增删改名、选中项的图上编辑器与参数页条目、运行结果回显。</summary>
public sealed class VisionMeasureGroupEditingTests
{
    private static MeasureVisionGroupNodeModel Group()
    {
        var line = VisionMeasureItem.Create(EVisionMeasureKind.Line, "左边");
        var rectangle = VisionMeasureItem.Create(EVisionMeasureKind.Rectangle, "外框");
        return new MeasureVisionGroupNodeModel
        {
            Id = "group", Frame = WorkflowInput<ImageFrame>.FromBinding(new WorkflowBindingKey("file", "$")),
            FailurePolicy = EVisionMeasureFailurePolicy.AllowPartial, Items = { line, rectangle }
        };
    }

    [Fact]
    public void Items_AddSelectRenameRemove()
    {
        var group = Group();
        using var page = new VisionFrameEditorPageModel(group, enableTemplateEditing: false);
        Assert.Equal(group.Items[0].Id, page.SelectedMeasureItemId);
        Assert.IsType<VisionFindShapeGizmo>(page.Caliper);
        Assert.Equal(VisionCaliperLayoutChoice.All, page.CaliperLayouts);
        Assert.Equal(EVisionCaliperLayout.Line, page.CaliperLayout!.Layout);
        Assert.False(page.CanSwitchCaliperLayout);
        Assert.False(page.ShowsRegionTools(false));
        Assert.False(new VisionRoiListModel(page).IsAvailable);

        int changes = 0;
        page.MeasureItemsChanged += (_, _) => changes++;
        var circle = page.AddMeasureItem(EVisionMeasureKind.Circle);
        Assert.Equal("找圆1", circle.Name);
        Assert.Equal(circle.Id, page.SelectedMeasureItemId);
        Assert.Equal(EVisionCaliperLayout.Annulus, page.CaliperLayout!.Layout);
        Assert.True(changes > 0);
        Assert.Equal(new[] { "左边", "外框", "找圆1" }, page.MeasureItems.Select(r => r.Name));
        Assert.Equal("找圆", page.MeasureItems[2].Kind);

        Assert.Throws<ArgumentException>(() => page.RenameMeasureItem(circle.Id, "左边"));
        page.RenameMeasureItem(circle.Id, "孔");
        Assert.Equal("孔", circle.Name);
        page.SetMeasureItemEnabled(group.Items[1].Id, false);
        Assert.False(page.MeasureItems[1].Enabled);

        page.RemoveSelectedMeasureItem();
        Assert.Equal(2, group.Items.Count);
        Assert.Equal(group.Items[1].Id, page.SelectedMeasureItemId); // 删除后选中相邻项。
        Assert.Empty(group.ValidateConfiguration());
    }

    [Fact]
    public void Properties_ShowOnlyTheSelectedItemsOwnParameters()
    {
        var group = Group();
        using var page = new VisionFrameEditorPageModel(group, enableTemplateEditing: false);
        page.SelectMeasureItem(group.Items[1].Id);
        var entries = page.CreateProperties(group).ToArray();
        Assert.All(entries, e => Assert.Equal("测量项：外框", e.Category));
        var names = entries.Select(e => e.Name).ToArray();
        Assert.Contains("MeasureItem.Name", names);
        Assert.Contains("MeasureItem.Path", names);
        Assert.Contains(nameof(FindVisionRectangleNodeModel.SearchLength), names);
        Assert.Contains(nameof(FindVisionShapeNodeModel.CaliperCount), names);
        Assert.DoesNotContain(nameof(AnalyzeVisionFrameNodeModel.Frame), names);
        Assert.DoesNotContain(nameof(AnalyzeVisionFrameNodeModel.FullImage), names);
        Assert.Equal($"Items.{group.Items[1].Id}", entries.Single(e => e.Name == "MeasureItem.Path").Value);

        entries.Single(e => e.Name == "MeasureItem.Name").SetValue("框");
        Assert.Equal("框", group.Items[1].Name);
        entries.Single(e => e.Name == nameof(FindVisionShapeNodeModel.CaliperCount)).SetValue(6);
        Assert.Equal(6, group.Items[1].Rectangle!.CaliperCount);
        Assert.Empty(page.CreateProperties(new FindVisionLineNodeModel { Id = "other" }));
    }

    [Fact]
    public async Task RunOnce_ShowsPerItemResultsInTheList()
    {
        var directory = Directory.CreateTempSubdirectory("measure-group-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "a.png"), "");
            var group = Group();
            group.Items[0].Line!.Regions[0] = new WorkflowVisionRoi { Id = "search", Shape = EWorkflowVisionRoiShape.Rectangle, CenterX = 40, CenterY = 30, Width = 40, Height = 20 };
            group.Items[1].Rectangle!.Regions[0] = new WorkflowVisionRoi { Id = "search", Shape = EWorkflowVisionRoiShape.Rectangle, CenterX = 40, CenterY = 30, Width = 40, Height = 30 };
            group.Items[1].Rectangle!.SearchLength = 10;
            var services = new WorkflowServiceProvider().Add<ICaliperMeasurer>(new CaliperMeasurer()).Add<IRobustLineFitter>(new RobustLineFitter());
            var runner = new WorkflowNodeTrialRunner(new WorkflowNodeHandlerCatalog().Register(new MeasureVisionGroupNodeHandler()), new WorkflowContext(services));
            using var page = new VisionFrameEditorPageModel(group, reader: new HalfReader(), enableTemplateEditing: false, runner: runner);
            await page.LoadImageFileAsync(Path.Combine(directory.FullName, "a.png"));
            await page.RunOnceAsync();
            using (var result = page.Capture(1)) Assert.NotNull(result);
            // 上暗下亮的图：找线（横向边）找到；均匀区域里的矩形找不到边。
            Assert.Equal("已找到", page.MeasureItems[0].Status);
            Assert.StartsWith("未找到：找矩形失败", page.MeasureItems[1].Status);
        }
        finally { directory.Delete(true); }
    }

    // 80×60：上半暗、下半亮，水平边在 y=30。
    private sealed class HalfReader : IImageFileReader
    {
        public Task<IImageSource> ReadAsync(string path, CancellationToken token = default) =>
            Task.FromResult<IImageSource>(VisionImage.CopyFrom(new ImageInfo(80, 60, EPixelLayout.Gray8),
                Enumerable.Range(0, 80 * 60).Select(i => i / 80 < 30 ? (byte)20 : (byte)220).ToArray()));
    }
}
