using DP.Vision;
using DP.Vision.Algorithms;
using DP.Vision.OpenCv;
using DP.Vision.UI;
using DP.WorkFlow.Persistence.Json;
using DP.WorkFlow.UI;
using DP.WorkFlow.Vision.UI;

namespace DP.WorkFlow.Tests;

public sealed class VisionTemplateAuthoringTests
{
    [HalconSdkTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadingHalconTestImage_ExplainsBlockedSearchBesideTestAction_AndCoveredRangeCanTest(bool wpf)
    {
        using var fixture = new Fixture(includeHalcon: true);
        var pixels = new byte[128 * 96];
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            pixels[(y + 20) * 128 + x + 12] = (byte)((x >= 5 && x <= 10 && y >= 4 && y <= 27 || y >= 22 && y <= 27 && x >= 5 && x <= 25) ? 230
                : (x - 23) * (x - 23) + (y - 9) * (y - 9) < 20 ? 180 : 30);
        File.WriteAllText(fixture.SamplePath, "P2\n128 96\n255\n" + string.Join(" ", pixels) + "\n");
        var node = new LocateVisionTemplatePoseNodeModel();
        using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(node, reader: fixture.Reader,
            templates: fixture.Editing, templateEditorOnly: true));
        page.Draft.ImplementationId = "halcon.template-shape-model";
        await page.Draft.ReadSourceAsync(fixture.SamplePath);
        page.Draft.Editor.Load(new RoiDocument(new[] { new RoiDefinition("template", new RectangleGeometry(new PointD(28, 36), 32, 32)) }));
        page.Properties().Single(p => p.Name == "Build.levels").SetValue(1);
        await page.Draft.BuildAsync(); page.PrepareCommit();
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.Load);
        Assert.True(page.Draft.IsBuilt); Assert.Null(page.Draft.BuildVerificationResult);
        page.Properties().Single(p => p.Name == "TestMinimumAngle").SetValue(-90d);
        page.Properties().Single(p => p.Name == "TestMaximumAngle").SetValue(90d);
        page.Properties().Single(p => p.Name == "TestMinimumScale").SetValue(.95);
        page.Properties().Single(p => p.Name == "TestMaximumScale").SetValue(1.05);
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.ReadTestImage, fixture.SamplePath);
        Assert.Equal(EVisionTemplateTestSource.TestImage, page.TestSource); Assert.True(page.Draft.IsBuilt);
        var reason = page.CommandBlockReason(EVisionTemplateAuthoringCommand.Test);
        Assert.Contains("角度", reason);
        var readiness = page.Properties().Single(p => p.Name == "TestReadiness");
        Assert.Contains(reason, Assert.IsType<string>(readiness.Value));
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                var descriptor = new WorkflowNodeEditorPageDescriptor("Template", "模板制作", WorkflowNodeEditorPageKind.Custom, 0, page);
                if (wpf)
                {
                    var root = new DP.WorkFlow.Vision.UI.Wpf.VisionTemplateAuthoringRenderer().CreateElement(descriptor);
                    try
                    {
                        var state = WpfDescendants(root).OfType<System.Windows.Controls.TextBlock>().Single(c => c.Name == "TemplateTestState");
                        Assert.Contains(reason, state.Text);
                        Assert.False(WpfDescendants(root).OfType<System.Windows.Controls.Button>().Single(c => Equals(c.Content, "测试匹配")).IsEnabled);
                        Assert.Contains(WpfDescendants(root).OfType<System.Windows.Controls.TextBlock>(), c => c.Text == readiness.Value as string);
                        AllowRange(); Refresh(root);
                        Assert.True(WpfDescendants(root).OfType<System.Windows.Controls.Button>().Single(c => Equals(c.Content, "测试匹配")).IsEnabled);
                        Assert.Contains(WpfDescendants(root).OfType<System.Windows.Controls.TextBlock>(), c => c.Text == readiness.Value as string);
                    }
                    finally { ((IDisposable)root).Dispose(); }
                }
                else
                {
                    using var root = new DP.WorkFlow.Vision.UI.WinForms.VisionTemplateAuthoringRenderer().CreateControl(descriptor);
                    Assert.Contains(reason, FormsDescendants(root).OfType<System.Windows.Forms.Label>().Single(c => c.Name == "TemplateTestState").Text);
                    Assert.False(FormsDescendants(root).Single(c => c.Text == "测试匹配").Enabled);
                    Assert.Contains(FormsDescendants(root).OfType<System.Windows.Forms.Label>(), c => c.Text == readiness.Value as string);
                    AllowRange(); Refresh(root);
                    Assert.True(FormsDescendants(root).Single(c => c.Text == "测试匹配").Enabled);
                    Assert.Contains(FormsDescendants(root).OfType<System.Windows.Forms.Label>(), c => c.Text == readiness.Value as string);
                }
                Assert.Equal("", page.CommandBlockReason(EVisionTemplateAuthoringCommand.Test));
                Assert.Contains("可以测试", Assert.IsType<string>(readiness.Value));
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(UiTestThread.JoinBudgetSeconds)));
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.Test); Assert.True(page.Draft.TrialResult!.Found);
        void AllowRange()
        {
            page.Properties().Single(p => p.Name == "TestMinimumAngle").SetValue(0d);
            page.Properties().Single(p => p.Name == "TestMaximumAngle").SetValue(0d);
        }
        static void Refresh(object root) => root.GetType().GetMethod("RefreshState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(root, null);
    }

    [Fact]
    public async Task MakingMask_RejectsAllDisabledRois_AndPublishedMaskMatchesPreviewHole()
    {
        using var fixture = new Fixture();
        var node = new LocateVisionTemplateNodeModel();
        using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(node, reader: fixture.Reader,
            templates: fixture.Editing, templateEditorOnly: true));
        await page.Draft.ReadSourceAsync(fixture.SamplePath);
        page.Draft.Editor.Load(new RoiDocument(new[] { new RoiDefinition("disabled", new RectangleGeometry(new PointD(2, 2), 4, 4), enabled: false) }));
        Assert.False(page.Draft.CanBuild); Assert.Contains("全部ROI已禁用", page.Draft.BuildBlockReason);
        await Assert.ThrowsAsync<InvalidOperationException>(() => page.Draft.BuildAsync());
        page.Draft.Editor.Load(new RoiDocument(new[]
        {
            new RoiDefinition("include", new RectangleGeometry(new PointD(2, 2), 4, 4)),
            new RoiDefinition("hole", new RectangleGeometry(new PointD(1.5, 1.5), 1, 1), ERoiPurpose.Exclude)
        }));
        await page.Draft.BuildAsync(); Assert.True(page.Draft.IsBuilt);
        Assert.Contains("15像素", Assert.IsType<string>(page.Properties().Single(e => e.Name == "MakingMask").Value));
        await page.TestAsync();
        using (var trial = page.Draft.Capture(true))
        {
            Assert.NotNull(trial);
            var effective = Assert.IsType<RegionGeometry>(Assert.Single(trial.Overlay!.Layers.Single(l => l.Id == "effective-mask").Visuals).Geometry);
            Assert.Equal(16, effective.AreaPixels); // 制作区域自检的搜索域是4×4；不是把制作掩膜当搜索掩膜。
        }
        page.Draft.ResetTrial(); Assert.Null(page.Draft.Capture(true));
        page.PrepareCommit();
        var snapshot = VisionTemplateStore.Capture(Path.Combine(fixture.Root, node.TemplateResourcePath));
        using var mask = VisionTemplateSource.Decode(snapshot.Read("source/mask.bin"));
        var bytes = new byte[mask.Info.ByteLength]; mask.CopyTo(0, bytes, 0, bytes.Length);
        Assert.Equal(15, bytes.Count(b => b == 255)); Assert.Equal(0, bytes[1 * mask.Info.Stride + 1]);
    }

    [Fact]
    public async Task ResourceChoices_GroupRevisionsByIdentity_KeepCurrentOldRevision_AndDistinguishSameNames()
    {
        using var fixture = new Fixture();
        var node = new LocateVisionTemplateNodeModel();
        using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(node, reader: fixture.Reader, templates: fixture.Editing, templateEditorOnly: true));
        page.Draft.DisplayName = "标签定位";
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.ReadSample, fixture.SamplePath);
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.Build); page.PrepareCommit();
        var oldReference = node.TemplateResourcePath;
        File.SetLastWriteTimeUtc(Path.Combine(fixture.Root, oldReference), new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await page.Draft.RefreshResourcesAsync(); // 让内存列表与测试指定的磁盘保存时间一致。
        page.Draft.SetParameter(page.Draft.Parameters.Single(p => p.Id == "blurKernel"), "3");
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.Build); page.PrepareCommit();
        var latestReference = node.TemplateResourcePath;
        Assert.Equal(latestReference, page.SelectedResource);
        Assert.Single(Choices()); // 应用后立即回填列表，不必先刷新才收起旧版本。
        var other = new LocateVisionTemplateNodeModel();
        using (var maker = new VisionTemplateEditorModel(other, fixture.Editing, () => throw new InvalidOperationException(), fixture.Reader))
        {
            maker.DisplayName = "标签定位";
            await maker.ReadSourceAsync(fixture.SamplePath); await maker.BuildAsync(); maker.PrepareCommit();
        }
        await page.Draft.RefreshResourcesAsync();
        var currentChoices = Choices();
        Assert.Equal(2, currentChoices.Length);
        Assert.DoesNotContain(currentChoices, c => Equals(c.Value, oldReference));
        Assert.Contains(currentChoices, c => Equals(c.Value, latestReference));
        Assert.Equal(2, currentChoices.Select(c => c.Label).Distinct().Count());
        page.Properties().Single(p => p.Name == "ShowHistory").SetValue(true);
        Assert.Equal(3, Choices().Length);
        page.ShowHistory = false; page.SelectedResource = oldReference;
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.Load);
        Assert.Equal(oldReference, node.TemplateResourcePath);
        Assert.Equal(3, Choices().Length); // 当前旧修订与最新修订并列，不能静默升级。
        Assert.Contains(Choices(), c => Equals(c.Value, oldReference) && c.Label.StartsWith("[当前] ", StringComparison.Ordinal));
        WorkflowPropertyChoice[] Choices() => page.Properties().Single(p => p.Name == "TemplateResource").Choices.Where(c => !Equals(c.Value, "")).ToArray();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewTemplateApply_SelectsPublishedRevision_AndReopenKeepsSelection(bool wpf)
    {
        using var fixture = new Fixture();
        var node = new LocateVisionTemplateNodeModel { Id = "locate" };
        var document = new WorkflowDocument { EntryNodeId = node.Id };
        document.CanvasProjection.Nodes.Add(new() { Node = node });
        var session = new WorkflowDesignerSession(document, new WorkflowNodeCatalog().RegisterImageNodes());
        await using var parent = new WorkflowNodeEditorModel(session, node.Id, node.Id,
            new[] { new VisionFrameEditorPageProvider(reader: fixture.Reader, templates: fixture.Editing) });
        string reference;
        await using (var child = parent.CreatePropertyEditor(WorkflowPropertyEditorKeys.VisionTemplateEditor))
        {
            var page = child.Pages.Select(p => p.Model).OfType<VisionTemplateAuthoringPageModel>().Single();
            await page.OpenAsync();
            await page.ExecuteAsync(EVisionTemplateAuthoringCommand.New);
            await page.ExecuteAsync(EVisionTemplateAuthoringCommand.ReadSample, fixture.SamplePath);
            await page.ExecuteAsync(EVisionTemplateAuthoringCommand.Build);
            child.ApplyChanges();
            reference = ((IWorkflowVisionTemplateNode)parent.EditingNode).ModelAlgorithm.Settings["templatePath"];
            Assert.NotEmpty(reference);
            Assert.Equal(reference, page.SelectedResource);
            Assert.Contains(page.Properties().Single(p => p.Name == "TemplateResource").Choices, c => Equals(c.Value, reference));
            child.ApplyChanges(); // 应用后再确认，不应重复发布。
            Assert.Single(Directory.GetFiles(fixture.Root, "manifest.json", SearchOption.AllDirectories));
            page.Draft.DisplayName = "标签定位（修改）";
            child.ApplyChanges();
            reference = ((IWorkflowVisionTemplateNode)parent.EditingNode).ModelAlgorithm.Settings["templatePath"];
            Assert.Equal(reference, page.SelectedResource);
            Assert.Equal(2, Directory.GetFiles(fixture.Root, "manifest.json", SearchOption.AllDirectories).Length);
        }
        parent.ApplyChanges();
        Assert.Equal(reference, node.TemplateResourcePath);
        await using var reopenedParent = new WorkflowNodeEditorModel(session, node.Id, node.Id,
            new[] { new VisionFrameEditorPageProvider(reader: fixture.Reader, templates: fixture.Editing) });
        await using var reopened = reopenedParent.CreatePropertyEditor(WorkflowPropertyEditorKeys.VisionTemplateEditor);
        var mounted = reopened.Pages.Select(p => p.Model).OfType<VisionTemplateAuthoringPageModel>().Single();
        await mounted.OpenAsync();
        Assert.Equal(reference, mounted.SelectedResource);
        Assert.Equal("标签定位（修改）", mounted.Draft.DisplayName);
        Assert.True(mounted.Draft.IsBuilt);
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                var descriptor = reopened.Pages.Single(p => ReferenceEquals(p.Model, mounted));
                if (wpf)
                {
                    var view = new DP.WorkFlow.Vision.UI.Wpf.VisionTemplateAuthoringRenderer().CreateElement(descriptor);
                    try
                    {
                        view.Measure(new System.Windows.Size(1000, 700)); view.Arrange(new System.Windows.Rect(0, 0, 1000, 700)); view.UpdateLayout();
                        var combo = WpfDescendants(view).OfType<System.Windows.Controls.ComboBox>().Single(c => c.Items.OfType<WorkflowPropertyChoice>().Any(choice => Equals(choice.Value, reference)));
                        Assert.Equal(reference, Assert.IsType<WorkflowPropertyChoice>(combo.SelectedItem).Value);
                    }
                    finally { ((IDisposable)view).Dispose(); }
                }
                else
                {
                    using var view = new DP.WorkFlow.Vision.UI.WinForms.VisionTemplateAuthoringRenderer().CreateControl(descriptor);
                    view.Size = new System.Drawing.Size(1000, 700); view.CreateControl();
                    var combo = FormsDescendants(view).OfType<ModernUI.WinForms.ModernSelect>().Single(c => c.Items.OfType<WorkflowPropertyChoice>().Any(choice => Equals(choice.Value, reference)));
                    Assert.Equal(reference, Assert.IsType<WorkflowPropertyChoice>(combo.SelectedItem).Value);
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Theory]
    [InlineData(EVisionTemplateTestSource.TestImage)]
    [InlineData(EVisionTemplateTestSource.Input)]
    [InlineData(EVisionTemplateTestSource.Sample)]
    public async Task LoadingResource_ResetsTestSourceToSampleSelfCheck_WithoutChangingRuntimeRange(EVisionTemplateTestSource source)
    {
        using var fixture = new Fixture();
        var node = new LocateVisionTemplatePoseNodeModel();
        using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(node, reader: fixture.Reader, templates: fixture.Editing, templateEditorOnly: true));
        await page.Draft.ReadSourceAsync(fixture.SamplePath);
        var parameters = page.Properties();
        parameters.Single(p => p.Name == "Build.minimumAngle").SetValue(-20d);
        parameters.Single(p => p.Name == "Build.maximumAngle").SetValue(20d);
        await page.Draft.BuildAsync(); page.PrepareCommit();
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.ReadTestImage, fixture.SamplePath);
        parameters.Single(p => p.Name == "TestMinimumAngle").SetValue(90d);
        parameters.Single(p => p.Name == "TestMaximumAngle").SetValue(90d);
        page.TestSource = source;
        Assert.NotEmpty(page.TestBlockReason);
        page.SelectedResource = node.TemplateResourcePath;
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.Load);
        Assert.Equal(EVisionTemplateTestSource.SampleRegion, page.TestSource);
        Assert.True(page.Draft.IsBuilt);
        Assert.Equal("", page.CommandBlockReason(EVisionTemplateAuthoringCommand.Test));
        Assert.Equal(Math.PI / 2, node.MinimumAngleRadians);
        Assert.Equal(Math.PI / 2, node.MaximumAngleRadians);
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.Test);
        Assert.True(page.Draft.TrialResult!.Found);
        Assert.Contains("角度范围 [0°, 0°]", page.Draft.TrialSearchSummary);
        page.TestSource = source;
        Assert.NotEmpty(page.TestBlockReason); // 运行候选仍应按已加载模型校验，不能静默改成0°。
    }

    [Fact]
    public async Task TemplateName_PublishesHumanNameWithoutRebuildingAndKeepsResourceIdentity()
    {
        using var fixture = new Fixture();
        var node = new LocateVisionTemplateNodeModel { Id = "locate" };
        using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(node, reader: fixture.Reader, templates: fixture.Editing, templateEditorOnly: true));
        var name = page.Properties().Single(p => p.Name == "TemplateName");
        name.SetValue("标签定位");
        await page.Draft.ReadSourceAsync(fixture.SamplePath); await page.Draft.BuildAsync();
        page.PrepareCommit();
        var firstPath = node.TemplateResourcePath;
        var first = VisionTemplateStore.Capture(Path.Combine(fixture.Root, firstPath));
        Assert.Equal("标签定位", first.Manifest.DisplayName);
        var definition = page.Draft.BuiltDefinition!.GeometrySignature();
        var verification = page.Draft.BuildVerificationResult;
        name.SetValue("标签定位（复核）");
        Assert.True(page.Draft.IsBuilt); Assert.True(page.CanCommit);
        Assert.Equal(definition, page.Draft.BuiltDefinition!.GeometrySignature());
        Assert.Same(verification, page.Draft.BuildVerificationResult);
        page.PrepareCommit();
        var renamed = VisionTemplateStore.Capture(Path.Combine(fixture.Root, node.TemplateResourcePath));
        Assert.NotEqual(firstPath, node.TemplateResourcePath);
        Assert.Equal(first.Manifest.TemplateId, renamed.Manifest.TemplateId);
        Assert.Equal(first.Identity, renamed.Identity);
        Assert.Equal("标签定位（复核）", renamed.Manifest.DisplayName);
        await page.Draft.RefreshResourcesAsync();
        Assert.Contains(page.Draft.Resources, r => r.Reference == node.TemplateResourcePath && r.Label.StartsWith("标签定位（复核）", StringComparison.Ordinal));
        await page.Draft.LoadResourceAsync(node.TemplateResourcePath);
        Assert.Equal("标签定位（复核）", page.Properties().Single(p => p.Name == "TemplateName").Value);
        Assert.True(page.Draft.CanTest);
        page.PrepareCommit();
        Assert.Equal(2, Directory.GetFiles(fixture.Root, "manifest.json", SearchOption.AllDirectories).Length);
    }

    [HalconSdkTheory]
    [InlineData("halcon.template-shape-model")]
    [InlineData("halcon.template-ncc-model")]
    public async Task HalconSwitchResources_LoadedModelCanTestWithoutRebuilding(string implementation)
    {
        using var fixture = new Fixture(includeHalcon: true);
        var pixels = new byte[129 * 97];
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            pixels[(y + 20) * 129 + x + 12] = (byte)((x >= 5 && x <= 10 && y >= 4 && y <= 27 || y >= 22 && y <= 27 && x >= 5 && x <= 25) ? 230
                : (x - 23) * (x - 23) + (y - 9) * (y - 9) < 20 ? 180 : 30);
        File.WriteAllText(fixture.SamplePath, "P2\n129 97\n255\n" + string.Join(" ", pixels) + "\n");
        var node = new LocateVisionTemplatePoseNodeModel { Id = "locate", ModelAlgorithm = new() { ImplementationId = implementation }, MinimumScore = .8 };
        var references = new List<string>();
        using (var maker = new VisionTemplateEditorModel(node, fixture.Editing, () => throw new InvalidOperationException(), fixture.Reader))
        {
            await maker.ReadSourceAsync(fixture.SamplePath);
            maker.Editor.Load(new RoiDocument(new[] { new RoiDefinition("template", new RectangleGeometry(new PointD(28, 36), 32, 32)) }));
            foreach (int levels in new[] { 0, 2 })
            {
                maker.SetParameter(maker.Parameters.Single(p => p.Id == "levels"), levels.ToString());
                await maker.BuildAsync();
                Assert.True(maker.BuildVerificationResult!.Found, maker.BuildVerificationSummary);
                maker.PrepareCommit(); references.Add(node.TemplateResourcePath);
            }
        }
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(node, reader: fixture.Reader, templates: fixture.Editing, templateEditorOnly: true)) { ShowHistory = true };
                var descriptor = new WorkflowNodeEditorPageDescriptor("Template", "模板制作", WorkflowNodeEditorPageKind.Custom, 0, page);
                using var root = new DP.WorkFlow.Vision.UI.WinForms.VisionTemplateAuthoringRenderer().CreateControl(descriptor);
                root.Dock = System.Windows.Forms.DockStyle.Fill;
                using var dialog = new System.Windows.Forms.Form { ClientSize = new System.Drawing.Size(1040, 680), ShowInTaskbar = false,
                    StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-3000, -3000) };
                dialog.Controls.Add(root);
                dialog.Shown += async (_, _) =>
                {
                    try
                    {
                        await Settled();
                        for (int visit = 0; visit < 16; visit++)
                        {
                            var reference = references[visit % 2];
                            var combo = FormsDescendants(root).OfType<ModernUI.WinForms.ModernSelect>().Single(c => c.Items.OfType<WorkflowPropertyChoice>().Any(choice => Equals(choice.Value, reference)));
                            combo.SelectedItem = combo.Items.OfType<WorkflowPropertyChoice>().Single(choice => Equals(choice.Value, reference));
                            var load = FormsDescendants(root).OfType<ModernUI.WinForms.ModernButton>().Single(b => b.Text == "读取所选模板");
                            load.PerformClick(); await Settled();
                            Assert.True(page.Draft.IsBuilt, page.Draft.Failure + " / " + page.Draft.TestBlockReason);
                            var test = FormsDescendants(root).OfType<ModernUI.WinForms.ModernButton>().Single(b => b.Text == "测试匹配");
                            Assert.True(test.Enabled, page.TestBlockReason);
                            test.PerformClick(); await Settled();
                            Assert.True(page.Draft.TrialResult!.Found, page.Draft.Failure + " / " + page.Draft.TestSummary);
                            using var bitmap = new System.Drawing.Bitmap(dialog.Width, dialog.Height);
                            dialog.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height));
                            GC.Collect(); GC.WaitForPendingFinalizers();
                        }
                    }
                    catch (Exception error) { failure = error; }
                    finally { dialog.Close(); }
                    async Task Settled()
                    {
                        var deadline = DateTime.UtcNow.AddSeconds(5);
                        while (page.IsOperating || page.Draft.IsBusy || !page.Draft.IsBuilt)
                        {
                            if (DateTime.UtcNow > deadline) throw new TimeoutException(page.Draft.Failure + " / " + page.TestBlockReason);
                            await Task.Delay(10);
                        }
                        await Task.Delay(20);
                    }
                };
                dialog.ShowDialog();
            }
            catch (Exception error) { failure = error; }
        });
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "资源切换测试未完成。");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [HalconSdkTheory]
    [InlineData("halcon.template-shape-model")]
    public async Task HalconNestedTemplateCommit_90DegreesRunsAfterApplyAndJsonRoundTrip(string implementation)
    {
        using var fixture = new Fixture(includeHalcon: true);
        var pixels = new byte[128 * 96];
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            pixels[(y + 20) * 128 + x + 12] = (byte)((x >= 5 && x <= 10 && y >= 4 && y <= 27 || y >= 22 && y <= 27 && x >= 5 && x <= 25) ? 230
                : (x - 23) * (x - 23) + (y - 9) * (y - 9) < 20 ? 180 : 30);
        File.WriteAllText(fixture.SamplePath, "P2\n128 96\n255\n" + string.Join(" ", pixels) + "\n");
        var node = new LocateVisionTemplatePoseNodeModel { Id = "locate", Frame = Input<ImageFrame>("source"),
            ModelAlgorithm = new() { ImplementationId = implementation }, MinimumScore = .8 };
        using (var initial = new VisionTemplateEditorModel(node, fixture.Editing, () => throw new InvalidOperationException(), fixture.Reader))
        {
            await initial.ReadSourceAsync(fixture.SamplePath);
            initial.Editor.Load(new RoiDocument(new[] { new RoiDefinition("template", new RectangleGeometry(new PointD(28, 36), 32, 32)) }));
            await initial.BuildAsync(); initial.PrepareCommit();
        }
        var oldReference = node.TemplateResourcePath;
        var nodes = new WorkflowNodeCatalog().RegisterImageNodes();
        var document = new WorkflowDocument { EntryNodeId = "source" };
        document.CanvasProjection.Nodes.Add(new() { Node = new AcquireVisionImageNodeModel { Id = "source", FilePath = fixture.SamplePath } });
        document.CanvasProjection.Nodes.Add(new() { Node = node });
        document.CanvasProjection.Connections.Add(new() { FromNodeId = "source", FromPort = WorkflowPorts.Success, ToNodeId = node.Id, ToPort = WorkflowPorts.Input });
        var session = new WorkflowDesignerSession(document, nodes) { SelectedNodeId = node.Id };
        using var frames = new WorkflowVisionFrameScope();
        using var bindings = new WorkflowVisionAlgorithmBindings(fixture.Runtime, frames, () => new VisionAlgorithmResourceContext(fixture.Root));
        var services = new WorkflowServiceProvider().Add<IWorkflowVisionFrameScope>(frames).Add<IWorkflowVisionAlgorithmBindings>(bindings)
            .Add<IWorkflowNodeCapabilityProvider>(bindings).Add<IWorkflowRunPreparationService>(bindings).Add<IWorkflowRunResourceOwner>(frames);
        using var host = new WorkflowRuntimeHost(nodes, new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
        host.Configure(document, new WorkflowContext(services));
        Assert.True((await host.RunAsync()).Success);
        Assert.True(Assert.IsType<TemplatePoseResult>(host.Engine!.RunState.NodeOutputs.Single(o => o.NodeId == node.Id).Value).Found);

        await using var parent = new WorkflowNodeEditorModel(session, "source", node.Id,
            new[] { new VisionFrameEditorPageProvider(reader: fixture.Reader, templates: fixture.Editing) });
        await using (var child = parent.CreatePropertyEditor(WorkflowPropertyEditorKeys.VisionTemplateEditor))
        {
            var maker = child.Pages.Select(p => p.Model).OfType<VisionTemplateAuthoringPageModel>().Single();
            await maker.OpenAsync();
            var parameters = maker.Properties();
            parameters.Single(p => p.Name == "Build.minimumAngle").SetValue(-180d);
            parameters.Single(p => p.Name == "Build.maximumAngle").SetValue(180d);
            parameters.Single(p => p.Name == "Build.levels").SetValue(2);
            parameters.Single(p => p.Name == "TestMinimumAngle").SetValue(90d);
            parameters.Single(p => p.Name == "TestMaximumAngle").SetValue(90d);
            await maker.ExecuteAsync(EVisionTemplateAuthoringCommand.Build);
            var rotated = new byte[pixels.Length];
            for (int y = 0; y < 96; y++) for (int x = 0; x < 128; x++) rotated[x * 96 + 95 - y] = pixels[y * 128 + x];
            File.WriteAllText(fixture.SamplePath, "P2\n96 128\n255\n" + string.Join(" ", rotated) + "\n");
            await maker.ExecuteAsync(EVisionTemplateAuthoringCommand.ReadTestImage, fixture.SamplePath);
            await maker.ExecuteAsync(EVisionTemplateAuthoringCommand.Test);
            Assert.True(maker.Draft.TrialResult!.Found, maker.Draft.TestSummary);
            child.ApplyChanges();
        }
        // 模板窗口应用后直接提交到正式节点，不需要再应用节点窗口。
        var applied = node.TemplateResourcePath;
        Assert.NotEqual(oldReference, applied);
        parent.ApplyChanges();
        Assert.Equal(applied, node.TemplateResourcePath);
        Exception? editingFailure = null;
        var editingThread = UiTestThread.Create(() =>
        {
            try
            {
                using var window = new System.Windows.Forms.Form { ClientSize = new System.Drawing.Size(400, 600), ShowInTaskbar = false,
                    StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-3000, -3000) };
                using var panel = new DP.WorkFlow.UI.WinForms.WorkflowPropertyPanel { Session = session, EntryNodeId = "source", Dock = System.Windows.Forms.DockStyle.Fill };
                var errors = new List<string>(); panel.EditError += (_, message) => errors.Add(message);
                window.Controls.Add(panel); window.Show(); System.Windows.Forms.Application.DoEvents();
                var inspector = (WorkflowPropertyInspectorModel)typeof(DP.WorkFlow.UI.WinForms.WorkflowPropertyPanel)
                    .GetField("_model", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(panel)!;
                inspector.SetValue(inspector.Entries.Single(e => e.Name == nameof(node.MinimumAngleRadians)), 85d);
                inspector.SetValue(inspector.Entries.Single(e => e.Name == nameof(node.MaximumAngleRadians)), 95d);
                Assert.Empty(errors);
                Assert.Equal(85 * Math.PI / 180, node.MinimumAngleRadians, 10);
                Assert.Equal(95 * Math.PI / 180, node.MaximumAngleRadians, 10);
            }
            catch (Exception error) { editingFailure = error; }
        });
        editingThread.Start(); Assert.True(editingThread.Join(TimeSpan.FromSeconds(30)), "角度区间编辑未完成。");
        if (editingFailure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(editingFailure).Throw();
        Assert.Equal(95 * Math.PI / 180, node.MaximumAngleRadians, 10);
        var store = new WorkflowDocumentJsonStore(nodes);
        document = store.Deserialize(store.Serialize(document)).Document;
        host.Configure(document, new WorkflowContext(services));
        var run = await host.RunAsync(); Assert.True(run.Success, run.Message);
        var result = Assert.IsType<TemplatePoseResult>(host.Engine!.RunState.NodeOutputs.Single(o => o.NodeId == node.Id).Value);
        Assert.True(result.Found);
        Assert.InRange(result.Transform!.AngleRadians, Math.PI / 2 - .01, Math.PI / 2 + .01);
        Assert.InRange(result.Transform.Center.X, 59.8, 60.2);
        Assert.InRange(result.Transform.Center.Y, 27.8, 28.2);
    }

    [HalconSdkTheory]
    [InlineData("halcon.template-shape-model")]
    public async Task HalconPublishedRange_ValidatesDegreeRangeAgainstSavedRevision(string implementation)
    {
        using var fixture = new Fixture(includeHalcon: true);
        var pixels = new byte[128 * 96];
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            pixels[(y + 20) * 128 + x + 12] = (byte)((x >= 5 && x <= 10 && y >= 4 && y <= 27 || y >= 22 && y <= 27 && x >= 5 && x <= 25) ? 230
                : (x - 23) * (x - 23) + (y - 9) * (y - 9) < 20 ? 180 : 30);
        File.WriteAllText(fixture.SamplePath, "P2\n128 96\n255\n" + string.Join(" ", pixels) + "\n");
        var node = new LocateVisionTemplatePoseNodeModel { Id = "pose", ModelAlgorithm = new() { ImplementationId = implementation } };
        using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(node, reader: fixture.Reader, templates: fixture.Editing, templateEditorOnly: true));
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.ReadSample, fixture.SamplePath);
        page.Draft.Editor.Load(new RoiDocument(new[] { new RoiDefinition("template", new RectangleGeometry(new PointD(28, 36), 32, 32)) }));
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.Build);
        page.PrepareCommit();
        var originalPath = node.TemplateResourcePath;

        var document = new WorkflowDocument { EntryNodeId = node.Id };
        document.CanvasProjection.Nodes.Add(new() { Node = node });
        var session = new WorkflowDesignerSession(document, new WorkflowNodeCatalog().RegisterImageNodes()) { SelectedNodeId = node.Id };
        using var inspector = new WorkflowPropertyInspectorModel(session, node.Id);
        inspector.SetValue(inspector.Entries.Single(e => e.Name == nameof(node.MinimumAngleRadians)), 90d);
        inspector.SetValue(inspector.Entries.Single(e => e.Name == nameof(node.MaximumAngleRadians)), 90d);
        var factory = new DP.Vision.Halcon.HalconTemplateModelFactory(true);
        IReadOnlyList<string> Issues()
        {
            var manifest = VisionTemplateStore.Inspect(Path.Combine(fixture.Root, node.TemplateResourcePath));
            return factory.ValidateSearch(manifest.Definition, manifest.BuildSettings, new PixelBounds(0, 0, 128, 96),
                node.OptionsForPreview());
        }
        var issue = Assert.Single(Issues());
        Assert.Contains("90°", issue);
        Assert.Contains("-20.0535°", issue);
        Assert.Contains("20.0535°", issue);
        var parameters = page.Properties();
        parameters.Single(p => p.Name == "Build.minimumAngle").SetValue(-180d);
        parameters.Single(p => p.Name == "Build.maximumAngle").SetValue(180d);
        Assert.Equal(originalPath, node.TemplateResourcePath);
        Assert.Single(Issues()); // 草稿设置未生成、未应用时，运行节点仍然引用原版本。
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.Build);
        Assert.True(page.Draft.BuildVerificationResult!.Found, page.Draft.BuildVerificationSummary);
        page.PrepareCommit();
        Assert.NotEqual(originalPath, node.TemplateResourcePath);
        Assert.Empty(Issues());
    }

    [HalconSdkTheory]
    [InlineData("halcon.template-shape-model")]
    public async Task HalconRotatedTestImage_ModelRangeDoesNotReplaceNodeSearchRange(string implementation)
    {
        using var fixture = new Fixture(includeHalcon: true);
        var pixels = new byte[128 * 96];
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            pixels[(y + 20) * 128 + x + 12] = (byte)((x >= 5 && x <= 10 && y >= 4 && y <= 27 || y >= 22 && y <= 27 && x >= 5 && x <= 25) ? 230
                : (x - 23) * (x - 23) + (y - 9) * (y - 9) < 20 ? 180 : 30);
        File.WriteAllText(fixture.SamplePath, "P2\n128 96\n255\n" + string.Join(" ", pixels) + "\n");
        var node = new LocateVisionTemplatePoseNodeModel { ModelAlgorithm = new() { ImplementationId = implementation }, MinimumScore = .8 };
        using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(node, reader: fixture.Reader, templates: fixture.Editing, templateEditorOnly: true));
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.ReadSample, fixture.SamplePath);
        page.Draft.Editor.Load(new RoiDocument(new[] { new RoiDefinition("template", new RectangleGeometry(new PointD(28, 36), 32, 32)) }));
        var parameters = page.Properties();
        parameters.Single(p => p.Name == "Build.minimumAngle").SetValue(-180d);
        parameters.Single(p => p.Name == "Build.maximumAngle").SetValue(180d);
        parameters.Single(p => p.Name == "Build.levels").SetValue(2);
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.Build);
        Assert.True(page.Draft.BuildVerificationResult!.Found, page.Draft.BuildVerificationSummary);
        var rotated = new byte[pixels.Length];
        for (int y = 0; y < 96; y++) for (int x = 0; x < 128; x++) rotated[x * 96 + 95 - y] = pixels[y * 128 + x];
        File.WriteAllText(fixture.SamplePath, "P2\n96 128\n255\n" + string.Join(" ", rotated) + "\n");
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.ReadTestImage, fixture.SamplePath);
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.Test);
        Assert.False(page.Draft.TrialResult!.Found);
        Assert.Contains("角度范围 [0°, 0°]", page.Draft.TrialSearchSummary);
        var builtCheck = page.Draft.BuildVerificationResult;
        parameters.Single(p => p.Name == "TestMinimumAngle").SetValue(90d);
        parameters.Single(p => p.Name == "TestMaximumAngle").SetValue(90d);
        Assert.True(page.Draft.IsBuilt);
        Assert.Same(builtCheck, page.Draft.BuildVerificationResult);
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.Test);
        Assert.True(page.Draft.TrialResult!.Found, page.Draft.TrialSearchSummary + "\n" + page.Draft.TestSummary);
        Assert.Contains("角度范围 [90°, 90°]", page.Draft.TrialSearchSummary);
        Assert.InRange(page.Draft.TrialResult.Transform!.AngleRadians, Math.PI / 2 - .01, Math.PI / 2 + .01);
        Assert.InRange(page.Draft.TrialResult.Transform!.Center.X, 59.8, 60.2);
        Assert.InRange(page.Draft.TrialResult.Transform.Center.Y, 27.8, 28.2);
    }

    [Theory]
    [InlineData("opencv.template-model")]
    [InlineData("opencv.template-pose-model")]
    [InlineData("halcon.template-ncc-model")]
    [InlineData("halcon.template-shape-model")]
    public void TemplateAngles_DisplayDegrees_AndKeepModelRangeSeparateFromMatchingRange(string implementation)
    {
        using var fixture = new Fixture(includeHalcon: true);
        var node = new LocateVisionTemplatePoseNodeModel { ModelAlgorithm = new() { ImplementationId = implementation } };
        // 平移模型使用对应平移节点；姿态模型使用旋转尺度节点。
        using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(
            implementation == "opencv.template-model" ? new LocateVisionTemplateNodeModel { ModelAlgorithm = node.ModelAlgorithm } : node,
            reader: fixture.Reader, templates: fixture.Editing, templateEditorOnly: true));
        var properties = page.Properties();
        var minimum = properties.Single(p => p.Name == "Build.minimumAngle");
        var maximum = properties.Single(p => p.Name == "Build.maximumAngle");
        Assert.Equal("模型最小角度（°）", minimum.DisplayName);
        Assert.Equal(-180, minimum.NumberMinimum); Assert.Equal(180, maximum.NumberMaximum);
        Assert.Equal(implementation.StartsWith("halcon") ? -0.35 * 180 / Math.PI : -180d, Assert.IsType<double>(minimum.Value), 10);
        minimum.SetValue(-30d); maximum.SetValue(45d);
        Assert.Equal(-Math.PI / 6, double.Parse(page.Draft.ParameterValue(page.Draft.Parameters.Single(p => p.Id == "minimumAngle")), System.Globalization.CultureInfo.InvariantCulture), 12);
        Assert.Equal(Math.PI / 4, double.Parse(page.Draft.ParameterValue(page.Draft.Parameters.Single(p => p.Id == "maximumAngle")), System.Globalization.CultureInfo.InvariantCulture), 12);
        Assert.Equal(0d, node.MinimumAngleRadians); Assert.Equal(0d, node.MaximumAngleRadians);
        properties.Single(p => p.Name == "Direction").SetValue(90d); Assert.Equal(Math.PI / 2, page.Draft.AxisAngleRadians, 12);
        if (implementation.StartsWith("halcon"))
        {
            var step = properties.Single(p => p.Name == "Build.angleStep");
            Assert.Equal("模型角度步长（°）", step.DisplayName);
            Assert.Equal(.001 * 180 / Math.PI, step.NumberMinimum!.Value, 12);
            step.SetValue(1d);
            Assert.Equal(Math.PI / 180, double.Parse(page.Draft.ParameterValue(page.Draft.Parameters.Single(p => p.Id == "angleStep")), System.Globalization.CultureInfo.InvariantCulture), 12);
        }
        if (implementation != "opencv.template-model")
        {
            properties.Single(p => p.Name == "TestMinimumAngle").SetValue(-15d); properties.Single(p => p.Name == "TestMaximumAngle").SetValue(15d);
            Assert.Equal(Math.PI / 12, node.MaximumAngleRadians, 12);
            Assert.Equal(-30d, Assert.IsType<double>(minimum.Value), 10);
            Assert.Equal(45d, Assert.IsType<double>(maximum.Value), 10);
        }
    }

    [HalconSdkTheory]
    [InlineData("halcon.template-shape-model")]
    [InlineData("halcon.template-ncc-model")]
    public void HalconTemplatePropertyButton_CanReopenAfterBuildingAndCancelling(string implementation)
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var fixture = new Fixture(includeHalcon: true);
                var pixels = new byte[128 * 96];
                for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                    pixels[(y + 20) * 128 + x + 12] = (byte)((x >= 5 && x <= 10 && y >= 4 && y <= 27 || y >= 22 && y <= 27 && x >= 5 && x <= 25) ? 230
                        : (x - 23) * (x - 23) + (y - 9) * (y - 9) < 20 ? 180 : 30);
                File.WriteAllText(fixture.SamplePath, "P2\n128 96\n255\n" + string.Join(" ", pixels) + "\n");
                var node = new LocateVisionTemplatePoseNodeModel { Id = "locate", ModelAlgorithm = new() { ImplementationId = implementation } };
                var document = new WorkflowDocument { EntryNodeId = node.Id };
                document.CanvasProjection.Nodes.Add(new() { Node = node });
                var session = new WorkflowDesignerSession(document, new WorkflowNodeCatalog().RegisterImageNodes()) { SelectedNodeId = node.Id };
                using var host = new System.Windows.Forms.Form { ClientSize = new System.Drawing.Size(500, 700), ShowInTaskbar = false,
                    StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-3000, -3000) };
                using var panel = new DP.WorkFlow.UI.WinForms.WorkflowPropertyPanel { Session = session, EntryNodeId = node.Id, Dock = System.Windows.Forms.DockStyle.Fill };
                host.Controls.Add(panel); host.Show(); System.Windows.Forms.Application.DoEvents();
                var visits = 0;
                panel.PropertyActionRequested += (_, _) =>
                {
                    var editor = new WorkflowNodeEditorModel(session, node.Id, node.Id,
                        new[] { new VisionFrameEditorPageProvider(reader: fixture.Reader, templates: fixture.Editing) },
                        propertyEditorKey: WorkflowPropertyEditorKeys.VisionTemplateEditor);
                    try
                    {
                        var page = editor.Pages.Select(p => p.Model).OfType<VisionTemplateAuthoringPageModel>().Single();
                        Assert.False(page.Draft.IsBuilt);
                        using var dialog = new DP.WorkFlow.UI.WinForms.WorkflowNodeEditorDialog(editor,
                            new DP.WorkFlow.UI.WinForms.IWorkflowWinFormsNodeEditorPageRenderer[] { new DP.WorkFlow.Vision.UI.WinForms.VisionFrameEditorRenderer(), new DP.WorkFlow.Vision.UI.WinForms.VisionTemplateAuthoringRenderer() });
                        dialog.ShowInTaskbar = false;
                        dialog.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                        dialog.Location = new System.Drawing.Point(-3000, -3000);
                        dialog.Shown += async (_, _) =>
                        {
                            try
                            {
                                await page.Draft.ReadSourceAsync(fixture.SamplePath);
                                page.Draft.Editor.Load(new RoiDocument(new[] { new RoiDefinition("template", new RectangleGeometry(new PointD(28, 36), 32, 32)) }));
                                await page.ExecuteAsync(EVisionTemplateAuthoringCommand.Build);
                                Assert.True(page.Draft.BuildVerificationResult!.Found, page.Draft.BuildVerificationSummary);
                                var frame = FormsDescendants(dialog).Single(c => c.GetType().Name == "VisionFrameEditorControl");
                                frame.GetType().GetMethod("RefreshPreview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(frame, null);
                                var canvas = FormsDescendants(dialog).OfType<DP.Vision.UI.IVisionCanvas>().Single();
                                Assert.NotNull(canvas.DisplayedFrameId);
                                using var screenshot = new System.Drawing.Bitmap(dialog.Width, dialog.Height);
                                dialog.DrawToBitmap(screenshot, new System.Drawing.Rectangle(0, 0, screenshot.Width, screenshot.Height));
                                visits++;
                            }
                            catch (Exception ex) { failure = ex; }
                            finally { dialog.DialogResult = System.Windows.Forms.DialogResult.Cancel; dialog.Close(); }
                        };
                        Assert.Equal(System.Windows.Forms.DialogResult.Cancel, dialog.ShowDialog(host));
                    }
                    finally { editor.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                };
                for (int i = 0; i < 8 && failure == null; i++)
                {
                    FormsDescendants(panel).OfType<ModernUI.WinForms.ModernButton>().Single(b => b.Text.Contains("制作/选择")).PerformClick();
                    Assert.Empty(node.ModelAlgorithm.Settings);
                    Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Resources")));
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    System.Windows.Forms.Application.DoEvents();
                }
                Assert.Equal(8, visits);
            }
            catch (Exception ex) { failure ??= ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(UiTestThread.JoinBudgetSeconds))); Assert.Null(failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadingTestImage_AfterMakingPreview_ActuallyReplacesDisplayedImage(bool wpf)
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var fixture = new Fixture();
                using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(new LocateVisionTemplateNodeModel { TemplateSource = EWorkflowVisionTemplateSource.Resource }, reader: fixture.Reader, templates: fixture.Editing, templateEditorOnly: true));
                page.Draft.ReadSourceAsync(fixture.SamplePath).GetAwaiter().GetResult();
                page.Draft.BuildAsync().GetAwaiter().GetResult();
                var descriptor = new WorkflowNodeEditorPageDescriptor("Template", "模板制作", WorkflowNodeEditorPageKind.Custom, 0, page);
                if (wpf)
                {
                    var root = new DP.WorkFlow.Vision.UI.Wpf.VisionTemplateAuthoringRenderer().CreateElement(descriptor);
                    var canvas = WpfDescendants(root).OfType<DP.Vision.UI.IVisionCanvas>().Single();
                    var frame = WpfDescendants(root).Single(c => c.GetType().Name == "VisionFrameEditorControl");
                    VerifySwitch(frame, canvas);
                }
                else
                {
                    using var root = new DP.WorkFlow.Vision.UI.WinForms.VisionTemplateAuthoringRenderer().CreateControl(descriptor);
                    var canvas = FormsDescendants(root).OfType<DP.Vision.UI.IVisionCanvas>().Single();
                    var frame = FormsDescendants(root).Single(c => c.GetType().Name == "VisionFrameEditorControl");
                    VerifySwitch(frame, canvas);
                }
                void VerifySwitch(object frame, DP.Vision.UI.IVisionCanvas canvas)
                {
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    var refresh = frame.GetType().GetMethod("RefreshPreview", flags)!;
                    var switchView = frame.GetType().GetMethod("SetView", flags)!;
                    for (int i = 0; i < 10; i++) refresh.Invoke(frame, null);
                    var original = canvas.DisplayedFrameId; Assert.NotNull(original);
                    File.WriteAllText(fixture.SamplePath, "P2\n8 6\n255\n" + string.Join(" ", Enumerable.Repeat(220, 48)) + "\n");
                    var read = page.ExecuteAsync(EVisionTemplateAuthoringCommand.ReadTestImage, fixture.SamplePath);
                    var deadline = System.Diagnostics.Stopwatch.StartNew();
                    while (!read.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(10)) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(1); }
                    Assert.True(read.IsCompleted); read.GetAwaiter().GetResult();
                    switchView.Invoke(frame, new object[] { 3 });
                    Assert.NotEqual(original, canvas.DisplayedFrameId);
                    Assert.NotNull(canvas.DisplayedFrameId);
                    var testImage = canvas.DisplayedFrameId;
                    switchView.Invoke(frame, new object[] { 4 });
                    Assert.Equal(original, canvas.DisplayedFrameId);
                    switchView.Invoke(frame, new object[] { 3 });
                    Assert.Equal(testImage, canvas.DisplayedFrameId);
                    switchView.Invoke(frame, new object[] { 4 });
                    switchView.Invoke(frame, new object[] { 2 });
                    Assert.Equal(original, canvas.DisplayedFrameId);
                    switchView.Invoke(frame, new object[] { 3 });
                    Assert.Equal(testImage, canvas.DisplayedFrameId);
                    switchView.Invoke(frame, new object[] { 5 });
                    Assert.Null(canvas.DisplayedFrameId); // 换测试图时已清空旧试匹配，不显示过期结果。
                    switchView.Invoke(frame, new object[] { 3 });
                    Assert.Equal(testImage, canvas.DisplayedFrameId);
                    File.WriteAllText(fixture.SamplePath, "P2\n6 8\n255\n" + string.Join(" ", Enumerable.Repeat(40, 48)) + "\n");
                    read = page.ExecuteAsync(EVisionTemplateAuthoringCommand.ReadTestImage, fixture.SamplePath);
                    deadline.Restart();
                    while (!read.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(10)) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(1); }
                    Assert.True(read.IsCompleted); read.GetAwaiter().GetResult();
                    switchView.Invoke(frame, new object[] { 3 });
                    Assert.NotNull(canvas.DisplayedFrameId);
                    Assert.NotEqual(testImage, canvas.DisplayedFrameId);
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(UiTestThread.JoinBudgetSeconds))); Assert.Null(failure);
    }

    [Fact]
    public async Task ReadingAnotherTestImage_ClearsPreviousDetection_AndKeepsBuildVerification()
    {
        using var fixture = new Fixture();
        using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(new LocateVisionTemplateNodeModel(), reader: fixture.Reader, templates: fixture.Editing, templateEditorOnly: true));
        await page.Draft.ReadSourceAsync(fixture.SamplePath);
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.Build);
        var buildCheck = page.Draft.BuildVerificationResult;
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.ReadTestImage, fixture.SamplePath);
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.Test);
        Assert.True(page.Draft.TrialResult!.Found);
        File.WriteAllText(fixture.SamplePath, "P2\n4 4\n255\n" + string.Join(" ", new int[16]) + "\n");
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.ReadTestImage, fixture.SamplePath);
        Assert.Null(page.Draft.TrialResult);
        Assert.Same(buildCheck, page.Draft.BuildVerificationResult);
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.Test);
        Assert.False(page.Draft.TrialResult!.Found);
    }

    [Fact]
    public async Task Build_ImmediatelyVerifiesAndShowsDetectionOnMakingImage()
    {
        using var fixture = new Fixture();
        using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(new LocateVisionTemplateNodeModel(), reader: fixture.Reader, templates: fixture.Editing, templateEditorOnly: true));
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.ReadSample, fixture.SamplePath);
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.Build);
        Assert.True(page.Draft.BuildVerificationResult!.Found);
        Assert.Contains("自检通过", page.Draft.BuildVerificationState);
        Assert.Contains("角度范围 [0°, 0°]；尺度范围 [1, 1]", page.Draft.BuildVerificationSummary);
        Assert.Null(page.Draft.TrialResult);
        using var canvas = page.Frame.Capture(4);
        Assert.Contains(canvas!.Overlay!.Layers.SelectMany(l => l.Visuals), v => v.Id == "build-check" && v.Caption!.Contains("分数"));
    }

    [HalconSdkTheory]
    [InlineData("halcon.template-ncc-model")]
    [InlineData("halcon.template-shape-model")]
    public async Task HalconBuild_VerifiesNativeModel_AndPreservesVerificationAfterUnmatchedTest(string implementation)
    {
        using var fixture = new Fixture(includeHalcon: true);
        var pixels = new byte[128 * 96];
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            pixels[(y + 20) * 128 + x + 12] = (byte)((x >= 5 && x <= 10 && y >= 4 && y <= 27 || y >= 22 && y <= 27 && x >= 5 && x <= 25) ? 230
                : (x - 23) * (x - 23) + (y - 9) * (y - 9) < 20 ? 180 : 30);
        File.WriteAllText(fixture.SamplePath, "P2\n128 96\n255\n" + string.Join(" ", pixels) + "\n");
        using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(new LocateVisionTemplatePoseNodeModel(), reader: fixture.Reader, templates: fixture.Editing, templateEditorOnly: true));
        page.Draft.ImplementationId = implementation;
        await page.Draft.ReadSourceAsync(fixture.SamplePath);
        page.Draft.Editor.Load(new RoiDocument(new[] { new RoiDefinition("template", new RectangleGeometry(new PointD(28, 36), 32, 32)) }));
        var parameters = page.Properties();
        parameters.Single(p => p.Name == "Build.minimumAngle").SetValue(-30d);
        parameters.Single(p => p.Name == "Build.maximumAngle").SetValue(30d);
        parameters.Single(p => p.Name == "Build.angleStep").SetValue(1d);
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.Build);
        var check = page.Draft.BuildVerificationResult;
        Assert.True(check!.Found, page.Draft.BuildVerificationSummary);
        Assert.InRange(check.Transform!.Center.X, 27.8, 28.2);
        Assert.InRange(check.Transform.Center.Y, 35.8, 36.2);
        parameters.Single(p => p.Name == "TestMinimumAngle").SetValue(15d); parameters.Single(p => p.Name == "TestMaximumAngle").SetValue(15d);
        Assert.True(page.Draft.IsBuilt); Assert.Same(check, page.Draft.BuildVerificationResult);
        using var image = VisionImage.CopyFrom(new ImageInfo(128, 96, EPixelLayout.Gray8), new byte[128 * 96]);
        using var unmatched = new ImageFrame("unmatched", image);
        await page.Draft.TryMatchAsync(unmatched, new PixelBounds(0, 0, 128, 96), new TemplatePoseOptions(0d, 0d, 1d, 1d, .9));
        Assert.False(page.Draft.TrialResult!.Found);
        Assert.Same(check, page.Draft.BuildVerificationResult);
        using var canvas = page.Frame.Capture(4);
        Assert.Equal(check.FrameId, canvas!.FrameId);
        Assert.Contains(canvas.Overlay!.Layers.SelectMany(l => l.Visuals), v => v.Id == "build-check");
        page.Draft.OriginX += 1;
        Assert.Null(page.Draft.BuildVerificationResult);
        using var edited = page.Frame.Capture(4);
        Assert.DoesNotContain(edited!.Overlay!.Layers.SelectMany(l => l.Visuals), v => v.Id == "build-check");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuildVerification_ShowsUnmatchedOrFailedCheck_WithoutClaimingGenerationFailed(bool fail)
    {
        using var fixture = new Fixture();
        var module = new VerificationModule(fail);
        var catalog = VisionAlgorithmCatalog.Compose(new IVisionAlgorithmModule[] { module });
        using var runtime = new VisionAlgorithmRuntime(catalog);
        var editing = new VisionTemplateEditingRuntime(catalog, runtime, () => new VisionAlgorithmResourceContext(fixture.Root));
        using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(new LocateVisionTemplateNodeModel
            { ModelAlgorithm = new() { ImplementationId = "test.verification-model" } }, reader: fixture.Reader, templates: editing, templateEditorOnly: true));
        await page.Draft.ReadSourceAsync(fixture.SamplePath);
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.Build);
        Assert.True(page.Draft.IsBuilt);
        Assert.Contains("模型已就绪", page.Draft.BuildState);
        Assert.Contains(fail ? "自检失败" : "自检未找到", page.Draft.BuildVerificationState);
        if (fail) Assert.Contains("原生自检错误", page.Draft.BuildVerificationSummary);
        else { Assert.False(page.Draft.BuildVerificationResult!.Found); Assert.Contains("未返回达标候选", page.Draft.BuildVerificationSummary); }
        using var canvas = page.Frame.Capture(4);
        Assert.DoesNotContain(canvas!.Overlay!.Layers.SelectMany(l => l.Visuals), v => v.Id == "build-check");
        Assert.Null(page.Draft.TrialResult);
    }

    [Fact]
    public async Task BuildVerificationBudgetFailure_IsVisible_AndDoesNotPublishOrDiscardGeneratedModel()
    {
        using var fixture = new Fixture();
        using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(new LocateVisionTemplatePoseNodeModel { MaximumWork = 1 }, reader: fixture.Reader, templates: fixture.Editing, templateEditorOnly: true));
        await page.Draft.ReadSourceAsync(fixture.SamplePath);
        await page.ExecuteAsync(EVisionTemplateAuthoringCommand.Build);
        Assert.True(page.Draft.IsBuilt);
        Assert.Contains("自检失败", page.Draft.BuildVerificationState);
        Assert.Contains("预算", page.Draft.BuildVerificationSummary);
        Assert.Contains("预算", page.Draft.Failure);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Resources")));
    }

    [Fact]
    public async Task LateBuildVerification_DoesNotShowResultAfterDraftChanges()
    {
        using var fixture = new Fixture();
        using var gate = new ManualResetEventSlim();
        var module = new VerificationModule(false, gate);
        var catalog = VisionAlgorithmCatalog.Compose(new IVisionAlgorithmModule[] { module });
        using var runtime = new VisionAlgorithmRuntime(catalog);
        var editing = new VisionTemplateEditingRuntime(catalog, runtime, () => null);
        using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(new LocateVisionTemplateNodeModel
            { ModelAlgorithm = new() { ImplementationId = "test.verification-model" } }, reader: fixture.Reader, templates: editing, templateEditorOnly: true));
        await page.Draft.ReadSourceAsync(fixture.SamplePath);
        var task = page.Draft.BuildAsync();
        try
        {
            await module.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains("正在制作自检", page.Draft.BuildVerificationState);
            page.Draft.OriginX += 1;
        }
        finally { gate.Set(); }
        await task;
        Assert.False(page.Draft.IsBuilt);
        Assert.Null(page.Draft.BuildVerificationResult);
        Assert.Equal("等待模型生成", page.Draft.BuildVerificationState);
    }

    [Fact]
    public async Task ClosingDuringBuildVerification_KeepsNativeCallResourcesUntilCompletion_AndCanReopen()
    {
        using var fixture = new Fixture();
        using var gate = new ManualResetEventSlim();
        var module = new VerificationModule(false, gate, ignoreCancellation: true);
        var catalog = VisionAlgorithmCatalog.Compose(new IVisionAlgorithmModule[] { module });
        using var runtime = new VisionAlgorithmRuntime(catalog);
        var editing = new VisionTemplateEditingRuntime(catalog, runtime, () => null);
        VisionTemplateAuthoringPageModel Open() => new(new VisionFrameEditorPageModel(new LocateVisionTemplateNodeModel
            { ModelAlgorithm = new() { ImplementationId = "test.verification-model" } }, reader: fixture.Reader, templates: editing, templateEditorOnly: true));
        using var page = Open();
        await page.Draft.ReadSourceAsync(fixture.SamplePath);
        var build = page.Draft.BuildAsync();
        try
        {
            await module.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            page.Dispose();
            Assert.True(page.Draft.IsDisposed);
            Assert.Equal(0, module.DisposedMatchers);
        }
        finally { gate.Set(); }
        await build;
        Assert.Equal(1, module.DisposedMatchers);
        Assert.Null(page.Draft.BuildVerificationResult);
        using var reopened = Open();
        await reopened.Draft.ReadSourceAsync(fixture.SamplePath);
        await reopened.Draft.BuildAsync();
        Assert.NotNull(reopened.Draft.BuildVerificationResult);
        Assert.Equal(2, module.DisposedMatchers);
    }

    [Fact]
    public void HalconLevels_CorrectingInvalidInputToAutomaticZero_ClearsFailure()
    {
        var catalog = VisionAlgorithmCatalog.Compose(new IVisionAlgorithmModule[] { new DP.Vision.Halcon.HalconVisionAlgorithmModule() });
        using var runtime = new VisionAlgorithmRuntime(catalog);
        var editing = new VisionTemplateEditingRuntime(catalog, runtime, () => null);
        using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(new LocateVisionTemplatePoseNodeModel(), templates: editing, templateEditorOnly: true));
        page.Draft.ImplementationId = "halcon.template-ncc-model";
        var levels = page.Properties().Single(p => p.Name == "Build.levels");
        var revision = page.Draft.EditRevision;
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => levels.SetValue(-1));
        page.Draft.ReportFailure(error);
        levels.SetValue(0);
        Assert.Equal("", page.Draft.Failure);
        Assert.Equal(revision, page.Draft.EditRevision);
    }

    [Theory]
    [InlineData("halcon.template-ncc-model", 0, false)]
    [InlineData("halcon.template-ncc-model", 6, true)]
    [InlineData("halcon.template-shape-model", 0, false)]
    [InlineData("halcon.template-shape-model", 6, true)]
    public void HalconLevels_SteppingAtBoundary_DoesNotReportInvalidInput(string implementation, int value, bool increase)
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                var catalog = VisionAlgorithmCatalog.Compose(new IVisionAlgorithmModule[] { new DP.Vision.Halcon.HalconVisionAlgorithmModule() });
                using var runtime = new VisionAlgorithmRuntime(catalog);
                var editing = new VisionTemplateEditingRuntime(catalog, runtime, () => null);
                using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(new LocateVisionTemplatePoseNodeModel(), templates: editing, templateEditorOnly: true));
                page.Draft.ImplementationId = implementation;
                var levels = page.Properties().Single(p => p.Name == "Build.levels");
                levels.SetValue(value);
                var descriptor = new WorkflowNodeEditorPageDescriptor("Template", "模板制作", WorkflowNodeEditorPageKind.Custom, 0, page);
                using var root = new DP.WorkFlow.Vision.UI.WinForms.VisionTemplateAuthoringRenderer().CreateControl(descriptor);
                var grid = FormsDescendants(root).OfType<ModernPropertyGrid.WinForms.ModernPropertyGrid>().Single();
                string issue = "";
                grid.ValidationFailed += (_, e) => issue = e.Exception.Message;
                var number = FormsDescendants(root).OfType<ModernUI.WinForms.ModernInputNumber>().Single(c => c.AccessibleName == "金字塔层数");
                typeof(ModernUI.WinForms.ModernInputNumber).GetMethod("StepBy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(number, new object[] { increase });
                Assert.Equal("", issue);
                Assert.Equal(value, levels.Value);
                Assert.Equal((decimal)value, number.Value);
                Assert.Equal(0m, number.Minimum);
                Assert.Equal(6m, number.Maximum);
                Assert.False(number.Controls.OfType<ModernUI.WinForms.ModernButton>().Single(b => b.AccessibleName == (increase ? "增加" : "减少")).Enabled);
                var angle = FormsDescendants(root).OfType<ModernUI.WinForms.ModernInputNumber>().Single(c => c.AccessibleName == "模型最小角度（°）");
                Assert.True(angle.Minimum < 0);
                Assert.Equal(-0.35 * 180 / Math.PI, (double)angle.Value, 10);
                var step = FormsDescendants(root).OfType<ModernUI.WinForms.ModernInputNumber>().Single(c => c.AccessibleName == "模型角度步长（°）");
                Assert.Equal(0.001 * 180 / Math.PI, (double)step.Minimum, 10);
                Assert.Equal(0.2 * 180 / Math.PI, (double)step.Maximum, 10);
                Assert.Throws<ArgumentOutOfRangeException>(() => levels.SetValue(-1));
                Assert.Throws<ArgumentOutOfRangeException>(() => levels.SetValue(7));
                Assert.Equal(value, levels.Value);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(UiTestThread.JoinBudgetSeconds))); Assert.Null(failure);
    }

    [Fact]
    public void HalconNcc_IsSelectableForRotation_AndScalingRequiresScaleFeature()
    {
        var catalog = VisionAlgorithmCatalog.Compose(new IVisionAlgorithmModule[] { new OpenCvVisionAlgorithmModule(), new DP.Vision.Halcon.HalconVisionAlgorithmModule() });
        using var runtime = new VisionAlgorithmRuntime(catalog);
        var editing = new VisionTemplateEditingRuntime(catalog, runtime, () => null);
        Assert.Contains(editing.Choices(true), d => d.ImplementationId == "halcon.template-ncc-model");
        Assert.Contains(editing.Choices(true), d => d.ImplementationId == "halcon.template-shape-model");
        var node = new LocateVisionTemplatePoseNodeModel { TemplateSource = EWorkflowVisionTemplateSource.Resource };
        Assert.DoesNotContain("scale", node.GetAlgorithmSlots().Single().RequiredFeatures!);
        node.MinimumScale = 1; node.MaximumScale = 1.1;
        Assert.Contains("scale", node.GetAlgorithmSlots().Single().RequiredFeatures!);
    }

    [Fact]
    public async Task RoiTemplate_DefaultSelfTest_DoesNotSearchTheWholeSample()
    {
        using var fixture = new Fixture();
        var pixels = Enumerable.Range(0, 64 * 64).Select(i => ((i * 37 + i / 64 * 17) % 256).ToString());
        File.WriteAllText(fixture.SamplePath, "P2\n64 64\n255\n" + string.Join(" ", pixels) + "\n");
        var node = new LocateVisionTemplatePoseNodeModel { MaximumWork = 2000 };
        using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(node, reader: fixture.Reader, templates: fixture.Editing, templateEditorOnly: true));
        await page.Draft.ReadSourceAsync(fixture.SamplePath);
        page.Draft.Editor.Load(new RoiDocument(new[] { new RoiDefinition("template", new RectangleGeometry(new PointD(24, 24), 16, 16)) }));
        await page.Draft.BuildAsync();
        await page.TestAsync();
        Assert.True(page.Draft.TrialResult!.Found);
        page.TestSource = EVisionTemplateTestSource.Sample;
        Assert.Contains("预算", page.TestBlockReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TemplateWorkspace_KeepsPropertiesBesideCanvas_AndBuildStateVisible(bool wpf)
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var fixture = new Fixture();
                using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(new LocateVisionTemplateNodeModel(),
                    reader: fixture.Reader, templates: fixture.Editing, templateEditorOnly: true));
                page.Draft.ReadSourceAsync(fixture.SamplePath).GetAwaiter().GetResult();
                page.Draft.BuildAsync().GetAwaiter().GetResult();
                page.TestAsync().GetAwaiter().GetResult();
                var descriptor = new WorkflowNodeEditorPageDescriptor("Template", "模板制作", WorkflowNodeEditorPageKind.Custom, 0, page);
                if (wpf)
                {
                    var root = new DP.WorkFlow.Vision.UI.Wpf.VisionTemplateAuthoringRenderer().CreateElement(descriptor);
                    var layout = Assert.IsAssignableFrom<System.Windows.Controls.Grid>(root);
                    Assert.Equal(3, layout.ColumnDefinitions.Count);
                    var action = WpfDescendants(root).OfType<System.Windows.Controls.Button>().Single(c => Equals(c.Content, "新建空白模板"));
                    Assert.Contains(WpfDescendants(root).OfType<System.Windows.Controls.TextBlock>(), c => c.Name == "TemplateBuildState");
                    Assert.Contains(WpfDescendants(root).OfType<System.Windows.Controls.TextBlock>(), c => c.Name == "TemplateBuildVerification" && c.Text.Contains("自检通过"));
                    root.Measure(new System.Windows.Size(1040, 680)); root.Arrange(new System.Windows.Rect(0, 0, 1040, 680)); root.UpdateLayout();
                    Assert.Contains(WpfAncestors(action), c => c is System.Windows.Controls.Expander);
                    Assert.Contains(WpfDescendants(root).OfType<System.Windows.Controls.TextBlock>(), c => c.Name == "TemplateTestState" && c.Text.Contains("已找到目标"));
                    var canvas = WpfDescendants(root).OfType<DP.Vision.UI.IVisionCanvas>().Single();
                    var frame = WpfDescendants(root).Single(c => c.GetType().Name == "VisionFrameEditorControl");
                    frame.GetType().GetMethod("RefreshPreview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(frame, null);
                    CaptureWpf(root, "template-workspace-wpf.png");
                }
                else
                {
                    using var root = new DP.WorkFlow.Vision.UI.WinForms.VisionTemplateAuthoringRenderer().CreateControl(descriptor);
                    var split = Assert.Single(FormsDescendants(root).OfType<System.Windows.Forms.SplitContainer>(), s => s.FixedPanel == System.Windows.Forms.FixedPanel.Panel1);
                    Assert.Equal(System.Windows.Forms.FixedPanel.Panel1, split.FixedPanel);
                    Assert.Contains(FormsDescendants(split.Panel1), c => c.GetType().Name == "ModernPropertyGrid");
                    var action = FormsDescendants(root).Single(c => c.Text == "新建空白模板");
                    Assert.Contains(FormsAncestors(action), c => c.GetType().Name == "PropertyRowPanel");
                    Assert.Contains(FormsDescendants(root).OfType<System.Windows.Forms.Label>(), c => c.Name == "TemplateBuildState");
                    Assert.Contains(FormsDescendants(root).OfType<System.Windows.Forms.Label>(), c => c.Name == "TemplateBuildVerification" && c.Text.Contains("自检通过"));
                    using var host = new System.Windows.Forms.Form { ClientSize = new System.Drawing.Size(1040, 680), ShowInTaskbar = false,
                        StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-3000, -3000) };
                    root.Dock = System.Windows.Forms.DockStyle.Fill; host.Controls.Add(root); host.Show(); System.Windows.Forms.Application.DoEvents();
                    Assert.InRange(split.SplitterDistance, 300, 420);
                    Assert.True(split.Panel2.Width > split.Panel1.Width);
                    Assert.Contains(FormsDescendants(root).OfType<System.Windows.Forms.Label>(), c => c.Name == "TemplateTestState" && c.Text.Contains("已找到目标"));
                    WorkflowPropertyPanelIntegrationTests.Capture(root, "template-workspace-winforms.png");
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(UiTestThread.JoinBudgetSeconds))); Assert.Null(failure);
    }

    private static void CaptureWpf(System.Windows.FrameworkElement root, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("DP_WORKFLOW_PROPERTY_CAPTURE");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(root);
        var pending = new System.Windows.Threading.DispatcherFrame();
        var settle = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        settle.Tick += (_, _) => { settle.Stop(); pending.Continue = false; }; settle.Start();
        System.Windows.Threading.Dispatcher.PushFrame(pending); root.UpdateLayout(); bitmap.Render(root);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, fileName)); png.Save(stream);
    }

    [Fact]
    public async Task TemplatePropertyActions_CreateBuildTestAndClearTheDraft()
    {
        using var fixture = new Fixture();
        using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(new LocateVisionTemplateNodeModel(),
            reader: fixture.Reader, templates: fixture.Editing, templateEditorOnly: true));
        var entries = page.Properties(command => page.ExecuteAsync(command,
            command == EVisionTemplateAuthoringCommand.ReadSample ? fixture.SamplePath : null));
        WorkflowPropertyEntry Action(EVisionTemplateAuthoringCommand command) => entries.Single(e => e.Name == "Command." + command);
        Assert.NotEmpty(Action(EVisionTemplateAuthoringCommand.Build).ActionBlockReason);
        await Action(EVisionTemplateAuthoringCommand.ReadSample).ExecuteActionAsync();
        Assert.Empty(Action(EVisionTemplateAuthoringCommand.Build).ActionBlockReason);
        await Action(EVisionTemplateAuthoringCommand.Build).ExecuteActionAsync();
        Assert.True(page.Draft.IsBuilt);
        await Action(EVisionTemplateAuthoringCommand.Test).ExecuteActionAsync();
        Assert.True(page.Draft.TrialResult!.Found);
        page.IsOperating = true;
        await Assert.ThrowsAsync<InvalidOperationException>(Action(EVisionTemplateAuthoringCommand.New).ExecuteActionAsync);
        Assert.True(page.Draft.IsBuilt); page.IsOperating = false;
        await Action(EVisionTemplateAuthoringCommand.New).ExecuteActionAsync();
        Assert.False(page.Draft.HasSample); Assert.False(page.Draft.IsBuilt); Assert.Null(page.Draft.TrialResult);
    }

    [Fact]
    public async Task TemplateReadiness_TracksEmptyBuiltEditedAndFailedDraft_AndUnmatchedTest()
    {
        using var fixture = new Fixture();
        using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(new LocateVisionTemplateNodeModel(),
            reader: fixture.Reader, templates: fixture.Editing, templateEditorOnly: true));
        Assert.False(page.Draft.CanBuild); Assert.False(page.Draft.CanTest); Assert.False(page.CanCommit);
        await page.Draft.ReadSourceAsync(fixture.SamplePath); Assert.True(page.Draft.CanBuild); Assert.False(page.Draft.CanTest);
        await page.Draft.BuildAsync(); Assert.True(page.Draft.CanTest); Assert.True(page.CanCommit);
        await page.TestAsync(); Assert.True(page.Draft.TrialResult!.Found); Assert.Contains("分数", page.Draft.TestSummary);
        page.TestSource = EVisionTemplateTestSource.Input; Assert.NotEmpty(page.TestBlockReason); Assert.Null(page.Draft.TrialResult);
        page.TestSource = EVisionTemplateTestSource.Sample;
        page.Draft.ReportFailure(new InvalidOperationException("明确的测试失败原因"));
        using var preview = page.Frame.Capture(4); Assert.Equal("明确的测试失败原因", page.Draft.Failure);
        page.Draft.OriginX += 1; Assert.False(page.Draft.CanTest); Assert.False(page.CanCommit); Assert.Empty(page.Draft.Failure);
        await page.Draft.BuildAsync(); Assert.True(page.CanCommit);
        page.IsOperating = true; Assert.False(page.CanCommit); page.IsOperating = false;
        var score = page.Properties().Single(p => p.Name == "Score"); score.SetValue(1d);
        using var zero = VisionImage.CopyFrom(new ImageInfo(4, 4, EPixelLayout.Gray8), new byte[16]); using var frame = new ImageFrame("unmatched", zero);
        await page.Draft.TryMatchAsync(frame, new PixelBounds(0, 0, 4, 4), new TemplatePoseOptions(0d, 0d, 1d, 1d, 1d));
        Assert.False(page.Draft.TrialResult!.Found); Assert.Equal("未找到目标", page.Draft.TestState); Assert.True(page.CanCommit);
        page.Draft.Editor.Load(new RoiDocument(new[] { new RoiDefinition("exclude-all", new RectangleGeometry(new PointD(2, 2), 4, 4), ERoiPurpose.Exclude) }));
        Assert.False(page.Draft.CanBuild); Assert.Contains("为空", page.Draft.BuildBlockReason);
    }

    [Fact]
    public async Task LoadedTemplate_CanBeTestedWithoutRebuilding()
    {
        using var fixture = new Fixture();
        var node = new LocateVisionTemplateNodeModel();
        using (var maker = new VisionFrameEditorPageModel(node, reader: fixture.Reader, templates: fixture.Editing))
        { await maker.Template!.ReadSourceAsync(fixture.SamplePath); await maker.Template.BuildAsync(); maker.PrepareCommit(); }
        using var mounted = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(node, reader: fixture.Reader,
            templates: fixture.Editing, templateEditorOnly: true));
        await mounted.OpenAsync();
        Assert.True(mounted.Draft.IsBuilt);
        using var image = await fixture.Reader.ReadAsync(fixture.SamplePath);
        using var frame = new ImageFrame("trial", image);
        await mounted.Draft.TryMatchAsync(frame, new PixelBounds(0, 0, 4, 4), new TemplatePoseOptions(0d, 0d, 1d, 1d, 0.1));
        Assert.True(mounted.Draft.TrialResult!.Found);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MountedTemplateEditor_IsIsolatedFromParent_AndPreservesSearchRegion(bool pose)
    {
        using var fixture = new Fixture();
        var node = pose ? (AnalyzeVisionFrameNodeModel)new LocateVisionTemplatePoseNodeModel { Id = "locate" }
            : new LocateVisionTemplateNodeModel { Id = "locate" };
        node.FullImage = false; node.X = 1; node.Y = 1; node.Width = 2; node.Height = 2;
        var document = new WorkflowDocument { EntryNodeId = node.Id };
        document.CanvasProjection.Nodes.Add(new() { Node = node });
        var session = new WorkflowDesignerSession(document, new WorkflowNodeCatalog().RegisterImageNodes());
        var providers = new[] { new VisionFrameEditorPageProvider(reader: fixture.Reader, templates: fixture.Editing) };
        await using var parent = new WorkflowNodeEditorModel(session, node.Id, node.Id, providers);
        Assert.Null(parent.Pages.Select(p => p.Model).OfType<VisionFrameEditorPageModel>().Single().Template);
        Assert.DoesNotContain(parent.Pages, p => p.PropertyEditorKey != null);
        using var inspector = new WorkflowPropertyInspectorModel(parent.EditingSession, node.Id);
        var entry = Assert.Single(inspector.Entries, e => e.EditorKey == WorkflowPropertyEditorKeys.VisionTemplateEditor);
        Assert.Equal(WorkflowPropertyEditorKind.Action, entry.EditorKind);
        Assert.Throws<InvalidOperationException>(() => inspector.SetValue(entry, "cannot assign"));
        var original = ((IWorkflowVisionTemplateNode)node).TemplateResourceId;
        await using (var cancelled = parent.CreatePropertyEditor(entry.EditorKey!))
        {
            var draft = cancelled.Pages.Select(p => p.Model).OfType<VisionTemplateAuthoringPageModel>().Single();
            await draft.OpenAsync(); Assert.Null(draft.Frame.Capture(4));
            await draft.Draft.ReadSourceAsync(fixture.SamplePath); await draft.Draft.BuildAsync();
        }
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Resources")));
        Assert.Equal(EWorkflowVisionTemplateSource.ImageBinding, ((IWorkflowVisionTemplateNode)parent.EditingNode).TemplateSource);
        await using (var accepted = parent.CreatePropertyEditor(entry.EditorKey!))
        {
            var page = accepted.Pages.Select(p => p.Model).OfType<VisionTemplateAuthoringPageModel>().Single();
            await page.Draft.ReadSourceAsync(fixture.SamplePath);
            page.Draft.Editor.Load(new RoiDocument(new[] { new RoiDefinition("template", new RectangleGeometry(new PointD(2, 2), 2, 2)) }));
            await page.Draft.BuildAsync(); accepted.ApplyChanges();
        }
        // 模板窗口应用即提交到正式节点；节点窗口副本同步更新。
        Assert.Equal(EWorkflowVisionTemplateSource.Resource, ((IWorkflowVisionTemplateNode)node).TemplateSource);
        Assert.Equal(EWorkflowVisionTemplateSource.Resource, ((IWorkflowVisionTemplateNode)parent.EditingNode).TemplateSource);
        parent.ApplyChanges();
        Assert.Equal(EWorkflowVisionTemplateSource.Resource, ((IWorkflowVisionTemplateNode)node).TemplateSource);
        Assert.Equal(original, ((IWorkflowVisionTemplateNode)node).TemplateResourceId);
        Assert.False(node.FullImage); Assert.Equal(1, node.X); Assert.Equal(2, node.Width);
        // 两次提交（模板窗口、节点窗口）各一步撤销。
        Assert.True(session.Undo()); Assert.True(session.Undo());
        Assert.Equal(EWorkflowVisionTemplateSource.ImageBinding, ((IWorkflowVisionTemplateNode)node).TemplateSource);
        Assert.True(session.Redo()); Assert.True(session.Redo());
    }

    [Fact]
    public async Task MountedTemplateEditor_LoadsCurrentTemplate_ListsVersions_AndCanStartBlank()
    {
        using var fixture = new Fixture();
        var node = new LocateVisionTemplateNodeModel { Id = "locate" };
        using (var first = new VisionFrameEditorPageModel(node, reader: fixture.Reader, templates: fixture.Editing))
        {
            await first.Template!.ReadSourceAsync(fixture.SamplePath); await first.Template.BuildAsync(); first.PrepareCommit();
        }
        var reference = node.TemplateResourcePath;
        using var mounted = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(node, reader: fixture.Reader,
            templates: fixture.Editing, templateEditorOnly: true));
        await mounted.OpenAsync(); using var sample = mounted.Frame.Capture(4);
        Assert.NotNull(sample); Assert.Single(mounted.Draft.Resources);
        Assert.Equal(reference, mounted.Draft.Resources[0].Reference);
        mounted.PrepareCommit(); Assert.Equal(reference, node.TemplateResourcePath);
        var damaged = Path.Combine(fixture.Root, "Resources", "Templates", Guid.NewGuid().ToString("N"), "revisions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(damaged); File.WriteAllText(Path.Combine(damaged, "manifest.json"), "broken");
        await mounted.Draft.RefreshResourcesAsync(); Assert.Equal(2, mounted.Draft.Resources.Count);
        Assert.Contains(mounted.Draft.Resources, r => r.Label.Contains("损坏"));
        await Assert.ThrowsAnyAsync<Exception>(() => mounted.Draft.LoadResourceAsync(Path.Combine(damaged, "manifest.json")));
        Assert.Equal(reference, node.TemplateResourcePath);
        mounted.Draft.StartNewTemplate(); Assert.Null(mounted.Frame.Capture(4)); Assert.Empty(node.ModelAlgorithm.Settings);
        Assert.Throws<InvalidOperationException>(mounted.PrepareCommit);
        await mounted.Draft.LoadResourceAsync(reference); Assert.Equal(reference, node.TemplateResourcePath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PropertyGridActionButton_RequestsTemplateEditor_AndDedicatedWindowBuilds(bool wpf)
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var fixture = new Fixture();
                var node = new LocateVisionTemplateNodeModel { Id = "locate" };
                var document = new WorkflowDocument { EntryNodeId = node.Id };
                document.CanvasProjection.Nodes.Add(new() { Node = node });
                var session = new WorkflowDesignerSession(document, new WorkflowNodeCatalog().RegisterImageNodes()) { SelectedNodeId = node.Id };
                WorkflowPropertyActionRequest? requested = null;
                var editor = new WorkflowNodeEditorModel(session, node.Id, node.Id,
                    new[] { new VisionFrameEditorPageProvider(reader: fixture.Reader, templates: fixture.Editing) },
                    propertyEditorKey: WorkflowPropertyEditorKeys.VisionTemplateEditor);
                try
                {
                    var mounted = editor.Pages.Select(p => p.Model).OfType<VisionTemplateAuthoringPageModel>().Single();
                    mounted.Draft.ReadSourceAsync(fixture.SamplePath).GetAwaiter().GetResult();
                    if (wpf)
                    {
                        var panel = new DP.WorkFlow.UI.Wpf.WorkflowPropertyPanel { Session = session, EntryNodeId = node.Id };
                        panel.PropertyActionRequested += (_, request) => requested = request;
                        var button = WpfDescendants(panel).OfType<System.Windows.Controls.Button>().Single(b => b.Content is string text && text.Contains("制作/选择"));
                        button.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                        var dialog = new DP.WorkFlow.UI.Wpf.WorkflowNodeEditorWindow(editor,
                            new DP.WorkFlow.UI.Wpf.IWorkflowWpfNodeEditorPageRenderer[] { new DP.WorkFlow.Vision.UI.Wpf.VisionFrameEditorRenderer(), new DP.WorkFlow.Vision.UI.Wpf.VisionTemplateAuthoringRenderer() });
                        Assert.Contains("模板制作/选择", dialog.Title);
                        Assert.Contains(WpfDescendants(dialog).OfType<System.Windows.Controls.Expander>(), e => Equals(e.Header, "3. 制作参数") && e.IsExpanded);
                        var canvas = WpfDescendants(dialog).OfType<DP.Vision.UI.IVisionCanvas>().Single();
                        var frameControl = WpfDescendants(dialog).Single(c => c.GetType().Name == "VisionFrameEditorControl");
                        CheckBlank(canvas, frameControl, mounted.Draft);
                        dialog.Close();
                    }
                    else
                    {
                        using var host = new System.Windows.Forms.Form { ClientSize = new System.Drawing.Size(500, 700), ShowInTaskbar = false,
                            StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-3000, -3000) };
                        using var panel = new DP.WorkFlow.UI.WinForms.WorkflowPropertyPanel { Session = session, EntryNodeId = node.Id, Dock = System.Windows.Forms.DockStyle.Fill };
                        panel.PropertyActionRequested += (_, request) => requested = request;
                        host.Controls.Add(panel); host.Show(); System.Windows.Forms.Application.DoEvents();
                        var button = FormsDescendants(panel).OfType<ModernUI.WinForms.ModernButton>().Single(b => b.Text.Contains("制作/选择"));
                        Assert.True(button.Enabled); button.PerformClick();
                        using var dialog = new DP.WorkFlow.UI.WinForms.WorkflowNodeEditorDialog(editor,
                            new DP.WorkFlow.UI.WinForms.IWorkflowWinFormsNodeEditorPageRenderer[] { new DP.WorkFlow.Vision.UI.WinForms.VisionFrameEditorRenderer(), new DP.WorkFlow.Vision.UI.WinForms.VisionTemplateAuthoringRenderer() });
                        Assert.Contains("模板制作/选择", dialog.Text);
                        Assert.Single(FormsDescendants(dialog).OfType<System.Windows.Forms.SplitContainer>(), s => s.FixedPanel == System.Windows.Forms.FixedPanel.Panel1);
                        var canvas = FormsDescendants(dialog).OfType<DP.Vision.UI.IVisionCanvas>().Single();
                        var frameControl = FormsDescendants(dialog).Single(c => c.GetType().Name == "VisionFrameEditorControl");
                        CheckBlank(canvas, frameControl, mounted.Draft);
                    }
                    Assert.Equal(new WorkflowPropertyActionRequest(node.Id, WorkflowPropertyEditorKeys.VisionTemplateEditor), requested);
                    Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Resources")));
                }
                finally { editor.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(UiTestThread.JoinBudgetSeconds))); Assert.Null(failure);
    }

    private static void CheckBlank(DP.Vision.UI.IVisionCanvas canvas, object control, VisionTemplateEditorModel draft)
    {
        var refresh = control.GetType().GetMethod("RefreshPreview", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        refresh.Invoke(control, null); Assert.NotNull(canvas.DisplayedFrameId);
        draft.StartNewTemplate(); refresh.Invoke(control, null); Assert.Null(canvas.DisplayedFrameId);
    }

    private static IEnumerable<System.Windows.Forms.Control> FormsAncestors(System.Windows.Forms.Control root)
    {
        for (var p = root.Parent; p != null; p = p.Parent) yield return p;
    }
    private static IEnumerable<System.Windows.DependencyObject> WpfAncestors(System.Windows.DependencyObject root)
    {
        for (var p = System.Windows.Media.VisualTreeHelper.GetParent(root); p != null; p = System.Windows.Media.VisualTreeHelper.GetParent(p)) yield return p;
    }
    private static IEnumerable<System.Windows.Forms.Control> FormsDescendants(System.Windows.Forms.Control root)
    {
        foreach (System.Windows.Forms.Control child in root.Controls) { yield return child; foreach (var nested in FormsDescendants(child)) yield return nested; }
    }
    private static IEnumerable<System.Windows.DependencyObject> WpfDescendants(System.Windows.DependencyObject root)
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
        { yield return child; foreach (var nested in WpfDescendants(child)) yield return nested; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NodeEditor_AppliesResource_RoundTrips_RunsWithoutTemplateInput_AndUndoRestoresOldMode(bool pose)
    {
        using var fixture = new Fixture();
        var node = pose ? (AnalyzeVisionFrameNodeModel)new LocateVisionTemplatePoseNodeModel { Id = "locate", Frame = Input<ImageFrame>("source") }
            : new LocateVisionTemplateNodeModel { Id = "locate", Frame = Input<ImageFrame>("source") };
        var source = new AcquireVisionImageNodeModel { Id = "source", FilePath = fixture.SamplePath };
        var nodes = new WorkflowNodeCatalog().RegisterImageNodes();
        var document = new WorkflowDocument { EntryNodeId = "source" };
        foreach (var n in new IWorkflowNodeModel[] { source, node }) document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = n });
        document.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = "source", FromPort = WorkflowPorts.Success, ToNodeId = "locate", ToPort = WorkflowPorts.Input });
        if (pose)
        {
            document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new MapVisionPoseCoordinateNodeModel { Id = "map", Pose = Input<TemplatePoseResult>("locate") } });
            document.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = "locate", FromPort = WorkflowPorts.Success, ToNodeId = "map", ToPort = WorkflowPorts.Input });
        }
        var session = new WorkflowDesignerSession(document, nodes);
        var editor = new WorkflowNodeEditorModel(session, "source", node.Id, new[] { new VisionFrameEditorPageProvider(reader: fixture.Reader, templates: fixture.Editing) }, propertyEditorKey: WorkflowPropertyEditorKeys.VisionTemplateEditor);
        try
        {
            var page = editor.Pages.Select(p => p.Model).OfType<VisionTemplateAuthoringPageModel>().Single().Frame;
            await page.Template!.ReadSourceAsync(fixture.SamplePath);
            page.Template.Editor.Load(new RoiDocument(new[] { new RoiDefinition("part", new RectangleGeometry(new PointD(2, 2), 2, 2)) }));
            page.Template.OriginX = pose ? 2 : 1; page.Template.OriginY = 1;
            await page.Template.BuildAsync();
            Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Resources")));
            editor.ApplyChanges();
            var templateNode = (IWorkflowVisionTemplateNode)node;
            Assert.Equal(EWorkflowVisionTemplateSource.Resource, templateNode.TemplateSource);
            var relative = templateNode.ModelAlgorithm.Settings["templatePath"];
            Assert.False(Path.IsPathRooted(relative)); Assert.True(File.Exists(Path.Combine(fixture.Root, relative)));
            Assert.True(session.Undo()); Assert.Equal(EWorkflowVisionTemplateSource.ImageBinding, templateNode.TemplateSource);
            Assert.True(session.Redo()); Assert.Equal(relative, templateNode.ModelAlgorithm.Settings["templatePath"]);
            var json = new WorkflowDocumentJsonStore(nodes); document = json.Deserialize(json.Serialize(document)).Document;
            using var frames = new WorkflowVisionFrameScope(); using var bindings = new WorkflowVisionAlgorithmBindings(fixture.Runtime, frames, () => new VisionAlgorithmResourceContext(fixture.Root));
            var services = new WorkflowServiceProvider().Add<IWorkflowVisionFrameScope>(frames).Add<IWorkflowVisionAlgorithmBindings>(bindings)
                .Add<IWorkflowNodeCapabilityProvider>(bindings).Add<IWorkflowRunPreparationService>(bindings).Add<IWorkflowRunResourceOwner>(frames);
            using var host = new WorkflowRuntimeHost(nodes, new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers()); host.Configure(document, new WorkflowContext(services));
            var run = await host.RunAsync(); Assert.True(run.Success, run.Message);
            var result = Assert.IsType<TemplatePoseResult>(host.Engine!.RunState.NodeOutputs.Single(o => o.NodeId == "locate").Value);
            Assert.True(result.Found); Assert.Equal(pose ? 2d : 1d, result.ReferenceX, 5); Assert.Equal(1d, result.ReferenceY, 5);
            if (pose)
            {
                var point = Assert.IsType<Coordinate2D>(host.Engine.RunState.NodeOutputs.Single(o => o.NodeId == "map").Value);
                Assert.Equal(2d, point.X, 5); Assert.Equal(1d, point.Y, 5);
            }
        }
        finally { await editor.DisposeAsync(); }
    }

    [Fact]
    public async Task CancelOrStaleDraft_DoesNotPublish_AndDoesNotMutateFormalNode()
    {
        using var fixture = new Fixture();
        var node = new LocateVisionTemplateNodeModel { Id = "locate", Frame = Input<ImageFrame>("source") };
        var nodes = new WorkflowNodeCatalog().RegisterImageNodes(); var document = new WorkflowDocument { EntryNodeId = node.Id };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node }); var session = new WorkflowDesignerSession(document, nodes);
        var editor = new WorkflowNodeEditorModel(session, node.Id, node.Id, new[] { new VisionFrameEditorPageProvider(reader: fixture.Reader, templates: fixture.Editing) }, propertyEditorKey: WorkflowPropertyEditorKeys.VisionTemplateEditor);
        var page = editor.Pages.Select(p => p.Model).OfType<VisionTemplateAuthoringPageModel>().Single().Frame;
        await page.Template!.ReadSourceAsync(fixture.SamplePath); await page.Template.BuildAsync(); page.Template.OriginX += 1;
        Assert.Throws<InvalidOperationException>(editor.ApplyChanges);
        Assert.Equal(EWorkflowVisionTemplateSource.ImageBinding, node.TemplateSource); Assert.Empty(node.ModelAlgorithm.Settings);
        await editor.DisposeAsync(); Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Resources")));
    }

    [Fact]
    public async Task Rebuild_KeepsReferenceDefinition_ChangingOriginInvalidatesDownstreamBinding()
    {
        using var fixture = new Fixture(); var node = new LocateVisionTemplateNodeModel();
        using var editor = new VisionTemplateEditorModel(node, fixture.Editing, () => throw new InvalidOperationException(), fixture.Reader);
        await editor.ReadSourceAsync(fixture.SamplePath); await editor.BuildAsync(); editor.PrepareCommit();
        var first = VisionTemplateStore.Capture(Path.Combine(fixture.Root, node.ModelAlgorithm.Settings["templatePath"]));
        await editor.LoadResourceAsync(node.ModelAlgorithm.Settings["templatePath"]);
        var parameter = editor.Parameters.Single(p => p.Id == "blurKernel"); editor.SetParameter(parameter, "3"); await editor.BuildAsync(); editor.PrepareCommit();
        var second = VisionTemplateStore.Capture(Path.Combine(fixture.Root, node.ModelAlgorithm.Settings["templatePath"]));
        Assert.NotEqual(first.Manifest.RevisionId, second.Manifest.RevisionId);
        Assert.Equal(first.Manifest.Definition.Reference().Signature, second.Manifest.Definition.Reference().Signature);
        using var image = VisionTemplateSource.Decode(first.Read("source/image.bin")); using var frame = new ImageFrame("frame", image);
        // 下游经“构建本帧坐标系”的模板方式随动：坐标定义并入模板参考签名。
        var (catalog, _) = GeometryPluginTestCatalog.Create(Path.Combine(fixture.Root, "plugins"));
        var definitionNode = GeometryPluginTestCatalog.Definition(catalog, "definition", "part");
        var build = GeometryPluginTestCatalog.BuildFromTemplate(catalog, "part", "frame", "definition", "locate");
        var business = ((IWorkflowVisionCoordinateDefinitionNode)definitionNode).GetCoordinateDefinition();
        VisionCoordinateSystem Located(VisionTemplateDefinition definition) =>
            VisionCoordinateBuilder.FromMatrix(definition.Reference().Bind(business), frame, CoordinateMatrix2D.Identity);
        var binding = WorkflowVisionCoordinateBinding.Capture("part", Located(first.Manifest.Definition));
        node.Id = "locate";
        var downstream = new AnalyzeVisionColorNodeModel { Coordinates = binding };
        binding.Validate(Located(second.Manifest.Definition), frame);
        Assert.Empty(downstream.ValidateDocumentConfiguration(new IWorkflowNodeModel[] { node, definitionNode, build, downstream }));
        editor.OriginX += .5; await editor.BuildAsync(); editor.PrepareCommit();
        var third = VisionTemplateStore.Capture(Path.Combine(fixture.Root, node.ModelAlgorithm.Settings["templatePath"]));
        Assert.Equal(2, third.Manifest.Definition.ReferenceVersion);
        Assert.Throws<InvalidOperationException>(() => binding.Validate(Located(third.Manifest.Definition), frame));
        Assert.Single(downstream.ValidateDocumentConfiguration(new IWorkflowNodeModel[] { node, definitionNode, build, downstream }));
    }

    [Fact]
    public async Task ResourceImport_RestoresRoiHoles_AndUnchangedApplyDoesNotCreateRevision()
    {
        using var fixture = new Fixture(); var node = new LocateVisionTemplateNodeModel();
        using (var editor = new VisionTemplateEditorModel(node, fixture.Editing, () => throw new InvalidOperationException(), fixture.Reader))
        {
            await editor.ReadSourceAsync(fixture.SamplePath);
            editor.Editor.Load(new RoiDocument(new[] { new RoiDefinition("include", new RectangleGeometry(new PointD(2, 2), 4, 4)),
                new RoiDefinition("hole", new RectangleGeometry(new PointD(2.5, 2.5), 1, 1), ERoiPurpose.Exclude) }));
            await editor.BuildAsync(); editor.PrepareCommit();
        }
        var reference = node.ModelAlgorithm.Settings["templatePath"];
        using var imported = new VisionTemplateEditorModel(node, fixture.Editing, () => throw new InvalidOperationException(), fixture.Reader);
        await imported.LoadResourceAsync(reference);
        Assert.Equal(2, imported.Editor.Document.Rois.Count); Assert.Contains(imported.Editor.Document.Rois, r => r.Purpose == ERoiPurpose.Exclude);
        imported.PrepareCommit(); Assert.Equal(reference, node.ModelAlgorithm.Settings["templatePath"]);
        Assert.Single(Directory.GetFiles(fixture.Root, "manifest.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task UnsavedRecipe_AllowsBuildButRejectsPublish()
    {
        using var fixture = new Fixture();
        var editing = new VisionTemplateEditingRuntime(fixture.Catalog, fixture.Runtime, () => new VisionAlgorithmResourceContext());
        var node = new LocateVisionTemplateNodeModel(); using var editor = new VisionTemplateEditorModel(node, editing, () => throw new InvalidOperationException(), fixture.Reader);
        await editor.ReadSourceAsync(fixture.SamplePath); await editor.BuildAsync();
        Assert.Throws<InvalidOperationException>(editor.PrepareCommit); Assert.Empty(node.ModelAlgorithm.Settings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothDesktopRenderers_ExpandTemplateSection_WithoutLoadingOrPublishing(bool wpf)
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var fixture = new Fixture();
                var node = new LocateVisionTemplateNodeModel { Id = "locate", Frame = Input<ImageFrame>("source") };
                using var page = new VisionFrameEditorPageModel(node, reader: fixture.Reader, templates: fixture.Editing);
                var descriptor = new WorkflowNodeEditorPageDescriptor("Image", "图像", WorkflowNodeEditorPageKind.Custom, 0, page);
                if (wpf)
                {
                    var control = new DP.WorkFlow.Vision.UI.Wpf.VisionFrameEditorRenderer().CreateElement(descriptor);
                    var section = System.Windows.LogicalTreeHelper.GetChildren(control).OfType<System.Windows.Controls.Expander>().Single();
                    Assert.False(section.IsExpanded); section.IsExpanded = true; Assert.True(section.IsExpanded);
                    Assert.NotNull(section.Content);
                }
                else
                {
                    using var control = new DP.WorkFlow.Vision.UI.WinForms.VisionFrameEditorRenderer().CreateControl(descriptor);
                    control.CreateControl();
                    var section = control.Controls.Cast<System.Windows.Forms.Control>().Single(c => c.GetType().Name == "VisionTemplateEditorControl");
                    var toggle = section.Controls.OfType<ModernUI.WinForms.ModernButton>().Single();
                    Assert.Equal(38, section.Height); toggle.PerformClick(); Assert.Equal(320, section.Height);
                    Assert.True(section.Controls.OfType<System.Windows.Forms.FlowLayoutPanel>().Single().Visible);
                }
                Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Resources"))); Assert.Empty(node.ModelAlgorithm.Settings);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(UiTestThread.JoinBudgetSeconds))); Assert.Null(failure);
    }

    [Fact]
    public async Task ReplacingSample_IncrementsReferenceVersion_EvenWhenSizeAndOriginMatch()
    {
        using var fixture = new Fixture(); var node = new LocateVisionTemplateNodeModel();
        using var editor = new VisionTemplateEditorModel(node, fixture.Editing, () => throw new InvalidOperationException(), fixture.Reader);
        await editor.ReadSourceAsync(fixture.SamplePath); await editor.BuildAsync(); editor.PrepareCommit();
        await editor.LoadResourceAsync(node.ModelAlgorithm.Settings["templatePath"]);
        File.WriteAllText(fixture.SamplePath, "P2\n4 4\n255\n10 25 91 15\n20 180 75 55\n220 40 135 45\n15 60 20 80\n");
        await editor.ReadSourceAsync(fixture.SamplePath); await editor.BuildAsync(); editor.PrepareCommit();
        var manifest = VisionTemplateStore.Inspect(Path.Combine(fixture.Root, node.ModelAlgorithm.Settings["templatePath"]));
        Assert.Equal(2, manifest.Definition.ReferenceVersion);
    }

    [Fact]
    public async Task TrialMatch_UsesDraftModel_AndDoesNotPublishOrChangeRuntimeConfiguration()
    {
        using var fixture = new Fixture(); var node = new LocateVisionTemplateNodeModel();
        using var editor = new VisionTemplateEditorModel(node, fixture.Editing, () => throw new InvalidOperationException(), fixture.Reader);
        await editor.ReadSourceAsync(fixture.SamplePath); await editor.BuildAsync();
        using var image = await fixture.Reader.ReadAsync(fixture.SamplePath); using var frame = new ImageFrame("trial", image);
        await editor.TryMatchAsync(frame, new PixelBounds(0, 0, 4, 4), new TemplatePoseOptions(0d, 0d, 1d, 1d, .99));
        Assert.True(editor.TrialResult!.Found); Assert.NotNull(editor.TrialResult.ReferencePoint);
        Assert.Empty(node.ModelAlgorithm.Settings); Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Resources")));
        using var canvas = editor.Capture(true); Assert.Equal("trial", canvas!.FrameId);
    }

    [Fact]
    public async Task LateBuild_DoesNotOverwriteChangedDraft_OrAllowApply()
    {
        using var fixture = new Fixture(); var gate = new DelayedModule();
        var catalog = VisionAlgorithmCatalog.Compose(new IVisionAlgorithmModule[] { new OpenCvVisionAlgorithmModule(), gate });
        using var runtime = new VisionAlgorithmRuntime(catalog);
        var editing = new VisionTemplateEditingRuntime(catalog, runtime, () => new VisionAlgorithmResourceContext(fixture.Root));
        var node = new LocateVisionTemplateNodeModel { ModelAlgorithm = new() { ImplementationId = "test.delayed-model" } };
        using var editor = new VisionTemplateEditorModel(node, editing, () => throw new InvalidOperationException(), fixture.Reader);
        await editor.ReadSourceAsync(fixture.SamplePath);
        var task = editor.BuildAsync(); await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Throws<InvalidOperationException>(editor.PrepareCommit);
        editor.OriginX += .5; gate.Continue.SetResult(); await task;
        Assert.False(editor.IsBuilt); Assert.Throws<InvalidOperationException>(editor.PrepareCommit);
        Assert.Empty(node.ModelAlgorithm.Settings); Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Resources")));
    }

    [Fact]
    public async Task ResourceFolderCanMove_WithRecipeRelativeReference_AndReferenceMismatchStopsBeforeAcquisition()
    {
        using var fixture = new Fixture(); var node = new LocateVisionTemplateNodeModel { Id = "locate", Frame = Input<ImageFrame>("source") };
        using (var editor = new VisionTemplateEditorModel(node, fixture.Editing, () => throw new InvalidOperationException(), fixture.Reader))
        { await editor.ReadSourceAsync(fixture.SamplePath); await editor.BuildAsync(); editor.PrepareCommit(); }
        var reference = node.ModelAlgorithm.Settings["templatePath"];
        var moved = Path.Combine(fixture.Root, "moved"); Directory.CreateDirectory(moved);
        foreach (var path in Directory.GetFiles(Path.Combine(fixture.Root, "Resources"), "*", SearchOption.AllDirectories))
        { var target = Path.Combine(moved, Path.GetRelativePath(fixture.Root, path)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(path, target); }
        using (var plan = await fixture.Runtime.PrepareAsync(new[] { new VisionAlgorithmRequest("moved", typeof(IPreparedVisionTemplateMatcher), node.ModelAlgorithm) }, new VisionAlgorithmResourceContext(moved), CancellationToken.None))
            Assert.NotNull(plan.Invoke<IPreparedVisionTemplateMatcher, object>("moved", m => m));
        node.TemplateReferenceDefinition!.OriginX += .5;
        var source = new AcquireVisionImageNodeModel { Id = "source", FilePath = fixture.SamplePath };
        var document = new WorkflowDocument { EntryNodeId = "source" };
        foreach (var n in new IWorkflowNodeModel[] { source, node }) document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = n });
        document.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = "source", FromPort = WorkflowPorts.Success, ToNodeId = node.Id, ToPort = WorkflowPorts.Input });
        using var frames = new WorkflowVisionFrameScope(); using var bindings = new WorkflowVisionAlgorithmBindings(fixture.Runtime, frames, () => new VisionAlgorithmResourceContext(moved));
        WorkflowVisionAlgorithmPreparationReport? report = null; bindings.PreparationChanged += value => report = value;
        var services = new WorkflowServiceProvider().Add<IWorkflowVisionFrameScope>(frames).Add<IWorkflowVisionAlgorithmBindings>(bindings)
            .Add<IWorkflowNodeCapabilityProvider>(bindings).Add<IWorkflowRunPreparationService>(bindings).Add<IWorkflowRunResourceOwner>(frames);
        using var host = new WorkflowRuntimeHost(new WorkflowNodeCatalog().RegisterImageNodes(), new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
        host.Configure(document, new WorkflowContext(services)); await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync());
        Assert.Empty(host.Engine!.RunState.NodeOutputs); Assert.Contains(report!.Issues, issue => issue.Code == "ALG_TEMPLATE_REFERENCE_MISMATCH" && issue.BindingKey.Contains("locate"));
    }

    private sealed class HalconSdkTheoryAttribute : TheoryAttribute
    {
        public HalconSdkTheoryAttribute()
        {
            if (!typeof(DP.Vision.Halcon.HalconVisionAlgorithmModule).Assembly.GetReferencedAssemblies().Any(a => string.Equals(a.Name, "halcondotnet", StringComparison.OrdinalIgnoreCase)))
                Skip = "HALCON引擎未装配SDK；真实模型自检需要SDK与许可。";
        }
    }

    private sealed class VerificationModule(bool fail, ManualResetEventSlim? gate = null, bool ignoreCancellation = false) : IVisionAlgorithmModule
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int DisposedMatchers;
        public string ExtensionId => "test.verification";
        public void Register(IVisionAlgorithmRegistration registrations)
        {
            registrations.Add(new VisionAlgorithmDescriptor("test.verification-model", "Test", "1", new Factory(this, fail, gate, ignoreCancellation), new[] { "translation" }));
            registrations.Add(new VisionAlgorithmDescriptor("test.verification-builder", "Test", "1", VisionAlgorithmFactory<IVisionTemplateBuilder>.Stateless(() => new Builder())));
        }
        private sealed class Builder : IVisionTemplateBuilder
        {
            public async Task<VisionTemplateBuild> BuildAsync(VisionTemplateBuildRequest request, CancellationToken token = default)
            {
                var built = await new OpenCvTemplateModelBuilder("opencv.template-model").BuildAsync(request, token);
                return new VisionTemplateBuild("test.verification-model", built.Format, built.Definition, built.Settings, built.Files);
            }
        }
        private sealed class Factory(VerificationModule owner, bool fail, ManualResetEventSlim? gate, bool ignoreCancellation) : IVisionAlgorithmFactory, IVisionTemplateFactoryDescription, IVisionTemplatePreviewFactory
        {
            public string MethodDisplayName => "Verification";
            public string BuilderImplementationId => "test.verification-builder";
            public IReadOnlyList<VisionAlgorithmParameter> BuildParameters => [];
            public Type ContractType => typeof(IPreparedVisionTemplateMatcher);
            public IReadOnlyList<VisionAlgorithmDependency> GetDependencies(VisionAlgorithmConfiguration configuration) => [];
            public Task<VisionAlgorithmActivation> PrepareAsync(VisionAlgorithmConfiguration configuration, IReadOnlyDictionary<string, object> dependencies, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<VisionAlgorithmResource> PreparePreviewAsync(VisionTemplateBuild build, CancellationToken token = default)
                => Task.FromResult(new VisionAlgorithmResource(new Matcher(owner, build, fail, gate, ignoreCancellation)));
        }
        private sealed class Matcher(VerificationModule owner, VisionTemplateBuild build, bool fail, ManualResetEventSlim? gate, bool ignoreCancellation) : IPreparedVisionTemplateMatcher, IDisposable
        {
            private bool _disposed;
            public VisionTemplateDefinition Definition => build.Definition;
            public string ModelIdentity => "test.verification";
            public TemplatePoseResult Match(ImageFrame frame, PixelBounds search, TemplatePoseOptions options, RegionGeometry? region = null, CancellationToken token = default)
            {
                owner.Started.TrySetResult(); gate?.Wait(ignoreCancellation ? CancellationToken.None : token);
                Assert.False(_disposed);
                // 原生调用可能在页面关闭后才返回，期间输入图像租约仍须可读。
                var bytes = new byte[frame.Image.Info.ByteLength]; frame.Image.CopyTo(0, bytes, 0, bytes.Length);
                return fail ? throw new InvalidOperationException("原生自检错误") : new TemplatePoseResult(frame.FrameId, ModelIdentity, 0, null, Definition.Reference());
            }
            public void Dispose() { Assert.False(_disposed); _disposed = true; Interlocked.Increment(ref owner.DisposedMatchers); }
        }
    }

    private sealed class DelayedModule : IVisionAlgorithmModule
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ExtensionId => "test.delayed";
        public void Register(IVisionAlgorithmRegistration registrations)
        {
            registrations.Add(new VisionAlgorithmDescriptor("test.delayed-model", "Test", "1", new DelayedFactory(), new[] { "translation" }));
            registrations.Add(new VisionAlgorithmDescriptor("test.delayed-builder", "Test", "1", VisionAlgorithmFactory<IVisionTemplateBuilder>.Stateless(() => new DelayedBuilder(this))));
        }
        private sealed class DelayedFactory : IVisionAlgorithmFactory, IVisionTemplateFactoryDescription
        {
            public string MethodDisplayName => "Delayed";
            public string BuilderImplementationId => "test.delayed-builder";
            public IReadOnlyList<VisionAlgorithmParameter> BuildParameters => [];
            public Type ContractType => typeof(IPreparedVisionTemplateMatcher);
            public IReadOnlyList<VisionAlgorithmDependency> GetDependencies(VisionAlgorithmConfiguration configuration) => [];
            public Task<VisionAlgorithmActivation> PrepareAsync(VisionAlgorithmConfiguration configuration, IReadOnlyDictionary<string, object> dependencies, CancellationToken cancellationToken) => throw new NotSupportedException();
        }
        private sealed class DelayedBuilder(DelayedModule owner) : IVisionTemplateBuilder
        {
            public async Task<VisionTemplateBuild> BuildAsync(VisionTemplateBuildRequest request, CancellationToken token = default)
            {
                owner.Started.TrySetResult(); await owner.Continue.Task.WaitAsync(token);
                var result = await new OpenCvTemplateModelBuilder("opencv.template-model").BuildAsync(request, token);
                return new VisionTemplateBuild("test.delayed-model", result.Format, result.Definition, result.Settings, result.Files);
            }
        }
    }

    private static WorkflowInput<T> Input<T>(string id) => WorkflowInput<T>.FromBinding(new WorkflowBindingKey(id, "$"));
    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "template-authoring-" + Guid.NewGuid().ToString("N"));
        internal string SamplePath { get; }
        internal VisionAlgorithmCatalog Catalog { get; }
        internal VisionAlgorithmRuntime Runtime { get; }
        internal IImageFileReader Reader { get; } = new OpenCvImageFileReader();
        internal VisionTemplateEditingRuntime Editing { get; }
        internal Fixture(bool includeHalcon = false)
        {
            Catalog = VisionAlgorithmCatalog.Compose(includeHalcon ? new IVisionAlgorithmModule[] { new OpenCvVisionAlgorithmModule(), new DP.Vision.Halcon.HalconVisionAlgorithmModule() } : new IVisionAlgorithmModule[] { new OpenCvVisionAlgorithmModule() });
            Directory.CreateDirectory(Root); SamplePath = Path.Combine(Root, "sample.pgm");
            File.WriteAllText(SamplePath, "P2\n4 4\n255\n10 25 90 15\n20 180 75 55\n220 40 135 45\n15 60 20 80\n");
            Runtime = new VisionAlgorithmRuntime(Catalog); Editing = new VisionTemplateEditingRuntime(Catalog, Runtime, () => new VisionAlgorithmResourceContext(Root));
        }
        public void Dispose() { Runtime.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}

