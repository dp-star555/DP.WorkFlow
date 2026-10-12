using DP.Vision;
using DP.Vision.Algorithms;
using DP.WorkFlow.Vision.UI;

namespace DP.WorkFlow.Tests;

/// <summary>画布工具栏的模型：加载文件夹图像、运行本节点一次（结果只在窗口里显示）、卡尺布局与ROI组的显隐。</summary>
public sealed class VisionFrameEditorToolbarTests
{
    [Fact]
    public async Task FolderImages_RunOnce_ShowsResultWithoutFlowRun()
    {
        var directory = Directory.CreateTempSubdirectory("vision-toolbar-");
        try
        {
            foreach (var name in new[] { "10.png", "20.bmp", "notes.txt" }) File.WriteAllText(Path.Combine(directory.FullName, name), "");
            var services = new WorkflowServiceProvider().Add<IColorAnalyzer>(new RgbColorAnalyzer());
            var handlers = new WorkflowNodeHandlerCatalog().Register(new AnalyzeVisionColorNodeHandler());
            using var page = new VisionFrameEditorPageModel(new AnalyzeVisionColorNodeModel { Id = "color" }, reader: new ValueReader(),
                enableTemplateEditing: false, runner: new WorkflowNodeTrialRunner(handlers, new WorkflowContext(services)));
            Assert.True(page.CanRun); Assert.True(page.CanLoadFiles);
            Assert.Null(page.Capture(0)); // 没有上游运行也没有手动图像。

            await page.LoadFolderAsync(directory.FullName);
            Assert.Equal(EVisionEditorImageSource.File, page.ImageSource);
            Assert.Equal("1/2 10.png", page.ImageSourceText);
            Assert.True(page.CanStepFolder);
            await page.StepFolderAsync(-1);
            Assert.Equal("2/2 20.bmp", page.ImageSourceText);
            using (var input = page.Capture(0)) Assert.NotNull(input);

            await page.RunOnceAsync();
            using (var result = page.Capture(1)) Assert.NotNull(result);
            Assert.StartsWith("已运行本节点", page.Status);
            Assert.Contains("RGB=(20.000,20.000,20.000)", page.Status);

            page.UseUpstreamImage();
            Assert.Null(page.Capture(0));
            Assert.Null(page.Capture(1)); // 换回上游图像后，本窗口的运行结果作废。
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void CaliperLayouts_FollowNodeAndHideRoiGroup()
    {
        using var caliper = new VisionFrameEditorPageModel(new MeasureVisionCaliperNodeModel());
        Assert.Equal(new[] { EVisionCaliperLayout.Line, EVisionCaliperLayout.Annulus }, caliper.CaliperLayouts.Select(c => c.Layout));
        Assert.Equal(EVisionCaliperLayout.Line, caliper.CaliperLayout!.Layout);
        Assert.True(caliper.SetCaliperLayout(EVisionCaliperLayout.Annulus));
        Assert.Equal(EVisionCaliperLayout.Annulus, caliper.CaliperLayout!.Layout);
        Assert.False(caliper.SetCaliperLayout(EVisionCaliperLayout.Rectangle));
        Assert.False(caliper.ShowsRegionTools(false));

        using var circle = new VisionFrameEditorPageModel(new FindVisionCircleNodeModel());
        Assert.Equal(EVisionCaliperLayout.Annulus, Assert.Single(circle.CaliperLayouts).Layout);
        Assert.False(circle.SetCaliperLayout(EVisionCaliperLayout.Line));
        using var line = new VisionFrameEditorPageModel(new FindVisionLineNodeModel());
        Assert.Equal(EVisionCaliperLayout.Line, Assert.Single(line.CaliperLayouts).Layout);

        using var color = new VisionFrameEditorPageModel(new AnalyzeVisionColorNodeModel());
        Assert.Empty(color.CaliperLayouts);
        Assert.True(color.ShowsRegionTools(false));
        Assert.False(color.CanRun); // 宿主没有提供试运行器。
    }

    // 文件名即灰度值，内容不读。
    private sealed class ValueReader : IImageFileReader
    {
        public Task<IImageSource> ReadAsync(string path, CancellationToken token = default) =>
            Task.FromResult<IImageSource>(VisionImage.CopyFrom(new ImageInfo(4, 4, EPixelLayout.Gray8),
                Enumerable.Repeat(byte.Parse(Path.GetFileNameWithoutExtension(path)), 16).ToArray()));
    }
}
