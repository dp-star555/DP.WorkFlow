using DP.LabelInspection.Contracts;
using DP.LabelInspection.Runtime;
using DP.WorkFlow.LabelInspection.UI;
using DP.WorkFlow.LabelInspection.UI.WinForms;
using DP.WorkFlow.UI;
using DP.WorkFlow.UI.WinForms;

namespace DP.WorkFlow.Tests;

public sealed class LabelInspectionEditorTests
{
    [Fact]
    public void PrepareCommit_WithoutRecipe_AllowsApplyingParameters_AndInvalidRecipeIsValidationError()
    {
        var node = new InspectLabelNodeModel();
        var page = new LabelInspectionEditorPageModel(node, new OpenCvImageCodec(), () => { });
        // 新建节点还没有配方：只改参数点“应用”不能抛未处理异常。
        page.PrepareCommit();
        node.RecipeJson = "{\"name\":\"broken\",\"width\":0}";
        var error = Assert.Throws<InvalidOperationException>(page.PrepareCommit);
        Assert.Contains("标签配方无效", error.Message);
    }

    [Fact]
    public void ImportIntoRoot_CopiesOutsideSampleIntoSamplesFolder_WithoutOverwriting()
    {
        var root = Path.Combine(Path.GetTempPath(), "label-root-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "label-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "inside.png"), new byte[] { 1 });
            Assert.Equal("inside.png", LabelInspectionEditorPageModel.ImportIntoRoot(root, Path.Combine(root, "inside.png")));

            var first = Path.Combine(outside, "a.png"); File.WriteAllBytes(first, new byte[] { 1, 2 });
            var copied = LabelInspectionEditorPageModel.ImportIntoRoot(root, first);
            Assert.Equal(Path.Combine("samples", "a.png"), copied);
            Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(Path.Combine(root, copied)));
            // 同名同内容复用，同名不同内容追加序号，不覆盖已有样张。
            Assert.Equal(copied, LabelInspectionEditorPageModel.ImportIntoRoot(root, first));
            File.WriteAllBytes(first, new byte[] { 3 });
            Assert.Equal(Path.Combine("samples", "a-1.png"), LabelInspectionEditorPageModel.ImportIntoRoot(root, first));
            Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(Path.Combine(root, copied)));
        }
        finally { Directory.Delete(root, true); Directory.Delete(outside, true); }
    }

    [Fact]
    public async Task ApplyChanges_CapturesEntireRecipeIntoDraft_AndCanUndoAsOneEdit()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false);
        var session = new WorkflowDesignerSession(rig.Document, rig.Nodes);
        var original = rig.Node.RecipeJson;
        await using var editor = Editor(session);
        var model = Page(editor);
        var region = new InspectionRegion("renamed", ERegionKind.Blank, new DP.Vision.Algorithms.PixelBounds(8, 6, 40, 32))
            .WithTasks(new RoiInspectionTasks(false, false));
        model.AttachWorkbench(() => new InspectionRecipe("custom-name", 64, 48, EInspectionMode.Free,
            EAlignmentMode.AssumeAligned, new[] { region }, LabelInspectionPipelineTests.Rig.Options), () => false, () => ValueTask.CompletedTask);
        Assert.Equal(original, rig.Node.RecipeJson);
        editor.ApplyChanges();
        var saved = model.Serializer.Deserialize(rig.Node.RecipeJson);
        Assert.Equal("custom-name", saved.Name);
        Assert.False(saved.Regions[0].Tasks.CheckQuality);
        Assert.Equal("renamed", saved.Regions[0].Name);
        Assert.Equal("image", rig.Node.Frame.Binding!.Value.NodeId);
        Assert.True(session.Undo());
        Assert.Equal(original, rig.Node.RecipeJson);
    }

    [Fact]
    public async Task DisposeAsync_CancelsEditingWithoutCaptureOrWriteback()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false);
        var original = rig.Node.RecipeJson;
        var editor = Editor(new WorkflowDesignerSession(rig.Document, rig.Nodes));
        var model = Page(editor);
        model.ImportRecipe(rig.Node.RecipeJson.Replace("\"blank\"", "\"edited\""));
        bool released = false;
        model.AttachWorkbench(() => throw new InvalidOperationException("Dispose must not capture"), () => false,
            () => { released = true; return ValueTask.CompletedTask; });
        await editor.DisposeAsync();
        Assert.True(released);
        Assert.Equal(original, rig.Node.RecipeJson);
    }

    [Fact]
    public async Task ApplyChanges_WhileRunning_RejectsCommitWithoutChangingProductionConfiguration()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false);
        var original = rig.Node.RecipeJson;
        await using var editor = Editor(new WorkflowDesignerSession(rig.Document, rig.Nodes));
        var model = Page(editor);
        model.AttachWorkbench(() => throw new Exception("Must not capture while running"), () => true, () => ValueTask.CompletedTask);
        Assert.False(editor.CanApplyChanges);
        Assert.Throws<InvalidOperationException>(editor.ApplyChanges);
        Assert.Equal(original, rig.Node.RecipeJson);
    }

    [Fact]
    public async Task ImportRecipe_WithoutImagePreview_IsEditableAndPreservesFullRecipe()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false);
        await using var editor = Editor(new WorkflowDesignerSession(rig.Document, rig.Nodes));
        var model = Page(editor);
        model.ImportRecipe(rig.Node.RecipeJson.Replace("\"blank\"", "\"offline\""));
        editor.ApplyChanges();
        Assert.Equal("offline", model.Serializer.Deserialize(rig.Node.RecipeJson).Name);
        Assert.Null(rig.Frames.Capture("image"));
        Assert.Contains(editor.Pages, p => p.RendererKey == LabelInspectionResultPageModel.RendererKey);
    }

    [Fact]
    public async Task WinFormsWorkbench_EditingRegions_CommitsFullRecipeAndPreservesName()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false);
        rig.Node.AuthorImagePath = "input.png";
        await RunStaAsync(async parent =>
        {
            var session = new WorkflowDesignerSession(rig.Document, rig.Nodes);
            await using var editor = Editor(session);
            using var dialog = Dialog(editor, rig.Root);
            dialog.Show(parent);
            var control = Descendants(dialog).OfType<DP.LabelInspection.LabelInspectionControl>().Single();
            await WaitUntilAsync(() => control.Regions.Count == 1);
            control.SetRegions(new[] { new InspectionRegion("changed", ERegionKind.Blank, new DP.Vision.Algorithms.PixelBounds(8, 8, 24, 24))
                .WithTasks(new RoiInspectionTasks(false, false)) });
            editor.ApplyChanges();
            var recipe = Page(editor).Serializer.Deserialize(rig.Node.RecipeJson);
            Assert.Equal("blank", recipe.Name); // CreateRequest的固定WinForms名称不得覆盖配方名称。
            Assert.Equal("changed", recipe.Regions[0].Name);
            Assert.False(recipe.Regions[0].Tasks.CheckQuality);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.FormClosed += (_, _) => closed.TrySetResult();
            dialog.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        });
    }

    [Fact]
    public async Task WinFormsDialog_CloseDuringTrial_CancelsAndWaitsWithoutDeadlockingUi()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false);
        rig.Node.AuthorImagePath = "input.png";
        var original = rig.Node.RecipeJson;
        await RunStaAsync(async parent =>
        {
            await using var editor = Editor(new WorkflowDesignerSession(rig.Document, rig.Nodes));
            using var dialog = Dialog(editor, rig.Root);
            dialog.Show(parent);
            var control = Descendants(dialog).OfType<DP.LabelInspection.LabelInspectionControl>().Single();
            await WaitUntilAsync(() => control.Regions.Count == 1);
            var engine = new CancellableEngine(); control.AttachEngine(engine);
            var running = control.RunInspectionAsync();
            Assert.True(control.IsInspectionRunning);
            Assert.False(editor.CanApplyChanges);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.FormClosed += (_, _) => closed.TrySetResult();
            dialog.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
            Assert.True(engine.Cancelled);
            Assert.Equal(original, rig.Node.RecipeJson);
            Assert.Null(rig.Frames.Capture("inspect"));
        });
    }

    private static WorkflowNodeEditorModel Editor(WorkflowDesignerSession session) => new(session, "image", "inspect",
        new[] { new LabelInspectionEditorPageProvider(new OpenCvImageCodec()) });
    private static LabelInspectionEditorPageModel Page(WorkflowNodeEditorModel editor) => Assert.IsType<LabelInspectionEditorPageModel>(editor.Pages.Single(p => p.PageId == "LabelInspection").Model);
    private static WorkflowNodeEditorDialog Dialog(WorkflowNodeEditorModel editor, string root) => new(editor,
        new IWorkflowWinFormsNodeEditorPageRenderer[] { new LabelInspectionWorkbenchRenderer(() => root), new LabelInspectionReportRenderer() });
    private static IEnumerable<System.Windows.Forms.Control> Descendants(System.Windows.Forms.Control control)
    { foreach (System.Windows.Forms.Control child in control.Controls) { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; } }
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (!condition()) { if (DateTime.UtcNow > timeout) throw new TimeoutException("Workbench did not load"); await Task.Delay(20); }
    }
    private static Task RunStaAsync(Func<System.Windows.Forms.Form, Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var parent = new System.Windows.Forms.Form { ShowInTaskbar = false, Width = 1100, Height = 700 };
            parent.Shown += async (_, _) =>
            {
                try { await action(parent); completion.TrySetResult(); }
                catch (Exception error) { completion.TrySetException(error); }
                finally { parent.Close(); }
            };
            System.Windows.Forms.Application.Run(parent);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
    private sealed class CancellableEngine : IInspectionEngine
    {
        public bool Cancelled;
        public EInspectionCapabilities Capabilities => default;
        public async Task<InspectionReport> InspectAsync(InspectionRequest request, CancellationToken cancellationToken = default)
        {
            try { await Task.Delay(Timeout.Infinite, cancellationToken); throw new InvalidOperationException("Must cancel"); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
        }
    }
}
