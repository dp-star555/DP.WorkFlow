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
    public void SetReferencePose_StoresPoseOnNode_AndNotifiesOnlyOnChange()
    {
        var node = new InspectLabelNodeModel(); int changed = 0;
        var model = new LabelInspectionEditorPageModel(node, new OpenCvImageCodec(), () => changed++);
        Assert.Null(node.GetReferencePose());
        var pose = DP.Vision.Algorithms.CoordinateMatrix2D.FromAffine(0.8, -0.6, 1500.5, 0.6, 0.8, 700.25);
        model.SetReferencePose(pose);
        model.SetReferencePose(pose);
        Assert.Equal(1, changed);
        var stored = node.GetReferencePose()!;
        Assert.Equal((0.8, -0.6, 1500.5, 0.6, 0.8, 700.25), (stored.M11, stored.M12, stored.Tx, stored.M21, stored.M22, stored.Ty));
        // 参考位姿随节点配置一起快照/保存。
        var copy = (InspectLabelNodeModel)WorkflowNodeConfigurationSnapshotter.Capture(node);
        Assert.Equal(1500.5, copy.GetReferencePose()!.Tx);
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
            await using var editor = Editor(session, reportPage: false);
            Assert.DoesNotContain(editor.Pages, p => p.RendererKey == LabelInspectionResultPageModel.RendererKey);
            using var dialog = Dialog(editor, rig.Root);
            dialog.Show(parent);
            var control = Descendants(dialog).OfType<DP.LabelInspection.LabelInspectionControl>().Single();
            await WaitUntilAsync(() => control.Regions.Count == 1);
            // 新布局：SDK侧栏隐藏，ROI规则为左侧分页，文件/字库等操作作为按钮出现在参数页。
            Assert.False(control.SidebarVisible);
            var rules = Descendants(dialog).OfType<DP.LabelInspection.RegionRulesControl>().Single();
            Assert.Contains(Descendants(dialog).OfType<TabPage>(), tab => tab.Text == "ROI规则" && Descendants(tab).Contains(rules));
            var page = Page(editor);
            var actions = page.CreateProperties(page.Node).Select(e => e.Name).ToArray();
            Assert.Contains("LabelInspection.Command.GlyphLibraries", actions);
            Assert.Contains("LabelInspection.Command.AnomalyLibraries", actions);
            Assert.DoesNotContain("LabelInspection.Command.GlyphQuickBuilder", actions);
            Assert.Contains("LabelInspection.Command.ImportRecipe", actions);
            Assert.Contains(Descendants(dialog).OfType<ModernUI.WinForms.ModernSelect>(), select => select.Items.Count == control.DrawKinds.Count);
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
    public async Task WinFormsCacheCommands_CleanupProductionCacheWithoutChangingRecipeOrTrialResources()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false);
        Assert.True((await rig.RunAsync()).Success);
        rig.Node.AuthorImagePath = "input.png"; var original = rig.Node.RecipeJson;
        await RunStaAsync(async parent =>
        {
            await using var editor = Editor(new WorkflowDesignerSession(rig.Document, rig.Nodes), reportPage: false);
            using var dialog = new WorkflowNodeEditorDialog(editor,
                new IWorkflowWinFormsNodeEditorPageRenderer[] { new LabelInspectionWorkbenchRenderer(() => rig.Root, cacheRuntime: rig.Runtime) });
            dialog.Show(parent);
            var page = Page(editor);
            var entries = page.CreateProperties(page.Node).ToArray();
            Assert.Contains(entries, p => p.Name == "LabelInspection.Command.CacheStatistics");
            var cleanup = entries.Single(p => p.Name == "LabelInspection.Command.CleanupIdleCache");
            await WaitUntilAsync(() => cleanup.ActionBlockReason.Length == 0);
            Assert.Equal(1, rig.Runtime.CachedResourceCount);
            await cleanup.ExecuteActionAsync();
            Assert.Equal(0, rig.Runtime.CachedResourceCount);
            var control = Descendants(dialog).OfType<DP.LabelInspection.LabelInspectionControl>().Single();
            Assert.Single(control.Regions);
            Assert.Equal(original, rig.Node.RecipeJson);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.FormClosed += (_, _) => closed.TrySetResult(); dialog.Close();
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
            await using var editor = Editor(new WorkflowDesignerSession(rig.Document, rig.Nodes), reportPage: false);
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

    [Fact]
    public async Task LoadCatalogProfile_OnlyChangesIsolatedRecipeDraft_PreservesProductionInputs_AndCanUndo()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false);
        rig.Node.RecipeCatalogPath = "recipes/index.json";
        rig.Node.RecipeKey = WorkflowInput<string>.FromBinding(new("image", "FrameId"));
        var original = rig.Node.RecipeJson; var key = rig.Node.RecipeKey.Binding;
        var session = new WorkflowDesignerSession(rig.Document, rig.Nodes);
        await using var editor = Editor(session); var page = Page(editor);
        var profile = WorkflowLabelRecipeProfile.FromNode(rig.Node, "B", 2) with
        { RecipeJson = original.Replace("\"blank\"", "\"draft-B\""), AuthorImagePath = "input.png", ReferencePose = new(1, 0, 8, 0, 1, 3) };
        page.LoadProfile(profile); page.SetRecipeCatalog("recipes/index.json", "B");
        Assert.Equal(original, rig.Node.RecipeJson); Assert.Equal(key, page.Node.RecipeKey.Binding);
        Assert.Equal("image", page.Node.Frame.Binding!.Value.NodeId); Assert.Equal(rig.Node.ResourceRoot, page.Node.ResourceRoot);
        editor.ApplyChanges(); Assert.Equal("draft-B", page.Serializer.Deserialize(rig.Node.RecipeJson).Name);
        Assert.Equal(key, rig.Node.RecipeKey.Binding); Assert.Equal(8, rig.Node.GetReferencePose()!.Tx);
        Assert.True(session.Undo()); Assert.Equal(original, rig.Node.RecipeJson);
        Assert.Null(rig.Node.GetReferencePose());
    }

    [Fact]
    public async Task WinFormsCatalogReport_UsesActualVersion_NotInvalidModelInDraft_AndDoesNotWriteItBack()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false);
        var profile = WorkflowLabelRecipeProfile.FromNode(rig.Node, "B", 1) with
        { RecipeJson = rig.Node.RecipeJson.Replace("\"blank\"", "\"actual-B\"") };
        await DP.WorkFlow.LabelInspection.WorkflowLabelRecipeCatalogStore.PublishAsync(rig.Root, "recipes/index.json", profile);
        rig.Node.RecipeCatalogPath = "recipes/index.json"; rig.Node.RecipeKey = WorkflowInput<string>.FromLiteral("B");
        rig.Node.RecognitionModelPath = "missing-draft-model.onnx"; var original = rig.Node.RecipeJson;
        Assert.True((await rig.RunAsync()).Success);
        await RunStaAsync(async parent =>
        {
            var session = new WorkflowDesignerSession(rig.Document, rig.Nodes);
            session.NodeOutputProvider = id => rig.Host.Engine!.RunState.NodeOutputs.LastOrDefault(o => o.NodeId == id);
            session.SetRuntimeSnapshot(rig.Host.GetSnapshot());
            await using var editor = new WorkflowNodeEditorModel(session, "image", "inspect",
                new[] { new LabelInspectionEditorPageProvider(new OpenCvImageCodec(), rig.Frames, includeReportPage: false) });
            using var dialog = Dialog(editor, rig.Root); dialog.Show(parent);
            var control = Descendants(dialog).OfType<DP.LabelInspection.LabelInspectionControl>().Single();
            await WaitUntilAsync(() => control.Regions.Count == 1 && !control.Enabled);
            Assert.Equal("actual-B", control.Regions[0].Name);
            Assert.Contains(Descendants(dialog).OfType<System.Windows.Forms.Label>(), label => label.Text.Contains("B@1"));
            editor.ApplyChanges();
            Assert.Equal(original, rig.Node.RecipeJson); Assert.Equal("missing-draft-model.onnx", rig.Node.RecognitionModelPath);
            Assert.Equal(1, rig.Runtime.ResourceLoadCount);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.FormClosed += (_, _) => closed.TrySetResult(); dialog.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        });
    }

    [Fact]
    public async Task ResourcePathPreflight_PreservesProductionBoundary_AndAcceptsRootInternalAbsoluteOrRelativePaths()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false);
        var outside = rig.Root + "-sibling/model.onnx";
        Assert.False(DP.WorkFlow.LabelInspection.WorkflowLabelInspectionResources.TryResolvePath(rig.Root, outside, out _, out var error));
        Assert.Contains(rig.Root, error); Assert.Contains("复制", error);
        Assert.Throws<InvalidDataException>(() => DP.WorkFlow.LabelInspection.WorkflowLabelInspectionResources.ResolvePath(rig.Root, outside));
        Assert.False(DP.WorkFlow.LabelInspection.WorkflowLabelInspectionResources.TryResolvePath(rig.Root, "../model.onnx", out _, out _));
        var expected = Path.Combine(rig.Root, "models", "rec.onnx");
        foreach (var path in new[] { "models/rec.onnx", expected })
        {
            Assert.True(DP.WorkFlow.LabelInspection.WorkflowLabelInspectionResources.TryResolvePath(rig.Root, path, out var resolved, out var diagnostic));
            Assert.Equal(expected, resolved); Assert.Empty(diagnostic);
        }
        var driveRoot = Path.GetPathRoot(rig.Root)!;
        Assert.True(DP.WorkFlow.LabelInspection.WorkflowLabelInspectionResources.TryResolvePath(driveRoot, "models/rec.onnx", out _, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WinFormsReopen_ExternalOcrPath_ShowsRecoveryGuidanceWithoutThrowingPathException(bool withAuthorImage)
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false);
        var outside = Path.Combine(Path.GetDirectoryName(rig.Root)!, "external-ocr-" + Guid.NewGuid().ToString("N") + ".onnx");
        File.WriteAllBytes(outside, [1]);
        int pathExceptions = 0;
        EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> observe = (_, args) =>
        {
            if (args.Exception is InvalidDataException && args.Exception.Message.Contains(outside, StringComparison.Ordinal))
                Interlocked.Increment(ref pathExceptions);
        };
        try
        {
            // 先通过真实隔离编辑提交，再关闭重开，复现用户添加绝对模型路径后的操作。
            var session = new WorkflowDesignerSession(rig.Document, rig.Nodes);
            await using (var edit = Editor(session))
            {
                Page(edit).Node.RecognitionModelPath = outside;
                Page(edit).Node.AuthorImagePath = withAuthorImage ? "input.png" : string.Empty;
                edit.ApplyChanges();
            }
            AppDomain.CurrentDomain.FirstChanceException += observe;
            await RunStaAsync(async parent =>
            {
                await using var editor = Editor(session, reportPage: false);
                using var dialog = Dialog(editor, rig.Root); dialog.Show(parent);
                await WaitUntilAsync(() => Descendants(dialog).OfType<System.Windows.Forms.Label>()
                    .Any(label => label.Text.Contains("资源根目录", StringComparison.Ordinal) && label.Text.Contains(outside, StringComparison.Ordinal)));
                var diagnostic = Descendants(dialog).OfType<System.Windows.Forms.Label>().Single(label => label.Text.Contains(outside, StringComparison.Ordinal)).Text;
                Assert.Equal(0, Volatile.Read(ref pathExceptions)); // VS不能再停在后台预期路径错误的throw行。
                Assert.Contains(rig.Root, diagnostic); Assert.Contains("复制", diagnostic);
                Assert.True(editor.CanApplyChanges); Assert.Equal(outside, Page(editor).Node.RecognitionModelPath);
                // 页面保持可修复：改正草稿路径、重载即可载入，不必退出进程。
                var page = Page(editor); page.Node.RecognitionModelPath = string.Empty; page.Node.AuthorImagePath = "input.png";
                await page.CreateProperties(page.Node).Single(entry => entry.Name == "LabelInspection.Command.ReloadResources").ExecuteActionAsync();
                var workbench = Descendants(dialog).OfType<DP.LabelInspection.LabelInspectionControl>().Single();
                Assert.Single(workbench.Regions); Assert.True(workbench.Enabled);
                Assert.Equal(0, Volatile.Read(ref pathExceptions));
                Assert.Equal(outside, rig.Node.RecognitionModelPath); // 重载不私自改写已提交的配置。
                editor.ApplyChanges(); Assert.Equal(string.Empty, rig.Node.RecognitionModelPath);
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                dialog.FormClosed += (_, _) => closed.TrySetResult(); dialog.Close();
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            });
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= observe; File.Delete(outside); }
    }

    [Fact]
    public async Task WinFormsLibraries_WithoutAuthorImage_ConnectsExtractionService()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false);
        rig.Node.AuthorImagePath = string.Empty;
        await RunStaAsync(async parent =>
        {
            await using var editor = Editor(new WorkflowDesignerSession(rig.Document, rig.Nodes), reportPage: false);
            using var dialog = Dialog(editor, rig.Root);
            dialog.Show(parent);
            var model = Page(editor);
            var command = model.CreateProperties(model.Node).Single(entry => entry.Name == "LabelInspection.Command.GlyphLibraries");
            await WaitUntilAsync(() => command.ActionBlockReason.Length == 0);
            Exception? failure = null;
            bool observed = false;
            using var timer = new System.Windows.Forms.Timer { Interval = 20 };
            timer.Tick += (_, _) =>
            {
                var libraryWindow = System.Windows.Forms.Application.OpenForms.Cast<Form>()
                    .FirstOrDefault(f => f.Text.StartsWith("字库 ·", StringComparison.Ordinal));
                if (libraryWindow is null) return;
                timer.Stop(); observed = true;
                try
                {
                    var builder = Descendants(libraryWindow).OfType<DP.LabelInspection.GlyphQuickBuilderControl>().Single();
                    var pixels = Enumerable.Repeat((byte)255, 64 * 32).ToArray();
                    builder.SetImage(new PixelSnapshot(64, 32, EImagePixelFormat.Gray8, pixels));
                    builder.SetRegion(new DP.Vision.Algorithms.PixelBounds(0, 0, 64, 32));
                    var extract = Descendants(builder).OfType<ModernUI.WinForms.ModernButton>().Single(b => b.Text == "提取当前ROI（OCR）");
                    Assert.True(extract.Enabled, "未载入主工作台样张，仅在制库页载图也必须连接已装配的提取服务。");
                }
                catch (Exception error) { failure = error; }
                finally { libraryWindow.Close(); }
            };
            timer.Start();
            await command.ExecuteActionAsync();
            Assert.True(observed);
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            dialog.Close();
        });
    }

    // WinForms配置页在图像下方显示运行结果，不提供独立报告页；WPF等平台仍使用报告页。
    private static WorkflowNodeEditorModel Editor(WorkflowDesignerSession session, bool reportPage = true) => new(session, "image", "inspect",
        new[] { new LabelInspectionEditorPageProvider(new OpenCvImageCodec(), includeReportPage: reportPage) });
    private static LabelInspectionEditorPageModel Page(WorkflowNodeEditorModel editor) => Assert.IsType<LabelInspectionEditorPageModel>(editor.Pages.Single(p => p.PageId == "LabelInspection").Model);
    private static WorkflowNodeEditorDialog Dialog(WorkflowNodeEditorModel editor, string root) => new(editor,
        new IWorkflowWinFormsNodeEditorPageRenderer[] { new LabelInspectionWorkbenchRenderer(() => root) });
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
