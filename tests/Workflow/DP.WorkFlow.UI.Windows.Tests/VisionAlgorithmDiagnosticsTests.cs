using DP.Vision;
using DP.Vision.Algorithms;
using DP.WorkFlow.UI;
using DP.WorkFlow.Vision.UI;

namespace DP.WorkFlow.Tests;

public sealed class VisionAlgorithmDiagnosticsTests
{
    [Fact]
    public async Task OldAutomaticPreparation_ReportDoesNotReplaceEditedConfigurationStatus()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = Catalog(new VisionAlgorithmFactory<IColorAnalyzer>(async (_, _, _) =>
        { entered.SetResult(); await proceed.Task; throw new InvalidOperationException("obsolete automatic preparation"); }));
        using var runtime = new VisionAlgorithmRuntime(catalog); using var bindings = new WorkflowVisionAlgorithmBindings(runtime);
        using var diagnostics = new WorkflowVisionAlgorithmDiagnostics(catalog, runtime, bindings, () => new());
        var node = new AnalyzeVisionColorNodeModel { Id = "color", Algorithm = new() { ImplementationId = "test.color" } };
        var pending = bindings.PrepareRunAsync(new WorkflowRunPreparationContext(new[] { node }, WorkflowRunScopeKind.Root, BindingScopeId: Guid.NewGuid()), default).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); diagnostics.Invalidate(); proceed.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Contains("配方已修改", diagnostics.Status); Assert.Empty(diagnostics.Analyze(Document(node)));
    }

    [Fact]
    public async Task CurrentChildRunFailure_MapsBackToRootPathCapturedBeforePreparation()
    {
        var catalog = Catalog(new VisionAlgorithmFactory<IColorAnalyzer>((_, _, _) => throw new InvalidOperationException("license")));
        using var runtime = new VisionAlgorithmRuntime(catalog); using var bindings = new WorkflowVisionAlgorithmBindings(runtime);
        using var diagnostics = new WorkflowVisionAlgorithmDiagnostics(catalog, runtime, bindings, () => new());
        var node = new AnalyzeVisionColorNodeModel { Id = "same", Algorithm = new() { ImplementationId = "test.color" } };
        diagnostics.SetRunBasePath("$/parent%2Fa");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await bindings.PrepareRunAsync(
            new WorkflowRunPreparationContext(new[] { node }, WorkflowRunScopeKind.Root, BindingScopeId: Guid.NewGuid()), default));
        var issue = Assert.Single(diagnostics.Analyze(Document(node)));
        Assert.Equal("$/parent%2Fa", issue.PlanPath); Assert.True(issue.FromRoot); Assert.Equal("same", issue.NodeId);
    }

    [Fact]
    public void DesktopPanels_CreateBothPlatformsWithInventoryAndDomainDiagnostics()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                var catalog = VisionAlgorithmCatalog.Compose(new[] { new ManagedVisionAlgorithmModule() });
                using var runtime = new VisionAlgorithmRuntime(catalog); using var bindings = new WorkflowVisionAlgorithmBindings(runtime);
                using var diagnostics = new WorkflowVisionAlgorithmDiagnostics(catalog, runtime, bindings, () => new());
                var document = Document(new StartNodeModel { Id = "start" });
                var session = new WorkflowDesignerSession(document, new WorkflowNodeCatalog().RegisterStandardNodes());
                using var forms = new DP.WorkFlow.Vision.UI.WinForms.WorkflowVisionAlgorithmPanel(diagnostics, () => document, () => session);
                var wpf = new DP.WorkFlow.Vision.UI.Wpf.WorkflowVisionAlgorithmPanel(diagnostics, () => document, () => session);
                Assert.NotNull(wpf.Content); Assert.Equal(3, forms.Controls.Count);
                using var formsDiagnostics = new DP.WorkFlow.UI.WinForms.WorkflowDiagnosticsControl { Session = session, EntryNodeId = "start", Provider = diagnostics };
                var wpfDiagnostics = new DP.WorkFlow.UI.Wpf.WorkflowDiagnosticsControl { Session = session, EntryNodeId = "start", Provider = diagnostics };
                Assert.True(formsDiagnostics.CanRun); Assert.True(wpfDiagnostics.CanRun);
                wpfDiagnostics.Provider = null;
                diagnostics.Invalidate();
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(UiTestThread.JoinBudgetSeconds))); Assert.Null(failure);
    }

    [Fact]
    public void StaticDiagnostics_AggregatesMissingSelections_WithNodeAndNestedPaths()
    {
        var child = Document(new AnalyzeVisionColorNodeModel { Id = "same", Algorithm = new() { ImplementationId = "missing-child" } });
        var root = Document(new AnalyzeVisionColorNodeModel { Id = "same", Algorithm = new() { ImplementationId = "missing-root" } },
            new BlockNodeModel { Id = "parent/a", SubDocument = child });
        var catalog = VisionAlgorithmCatalog.Compose(Array.Empty<IVisionAlgorithmModule>());
        using var runtime = new VisionAlgorithmRuntime(catalog);
        using var bindings = new WorkflowVisionAlgorithmBindings(runtime);
        using var diagnostics = new WorkflowVisionAlgorithmDiagnostics(catalog, runtime, bindings, () => new());
        var issues = diagnostics.Analyze(root);
        Assert.Equal(2, issues.Count); Assert.All(issues, i => Assert.True(i.BlocksRun));
        Assert.Equal(new[] { "$", "$/parent%2Fa" }, issues.Select(i => i.PlanPath));
        var report = diagnostics.ExportReport(root);
        Assert.Contains("missing-child", report); Assert.Contains("ALG_IMPLEMENTATION_MISSING", report);
    }

    [Fact]
    public async Task ManualCheck_FailureAndCancellation_AreStructuredAndDoNotRetireFrames()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new VisionAlgorithmFactory<IColorAnalyzer>(async (_, _, token) =>
        {
            entered.TrySetResult(); await proceed.Task.WaitAsync(token);
            return new VisionAlgorithmActivation("native", EVisionAlgorithmSharing.SharedConcurrent,
                _ => throw new InvalidOperationException("native model invalid"));
        });
        var catalog = Catalog(factory); var document = Document(new AnalyzeVisionColorNodeModel { Id = "color", Algorithm = new() { ImplementationId = "test.color" } });
        using var runtime = new VisionAlgorithmRuntime(catalog); using var frames = new WorkflowVisionFrameScope();
        using var bindings = new WorkflowVisionAlgorithmBindings(runtime, frames);
        using var diagnostics = new WorkflowVisionAlgorithmDiagnostics(catalog, runtime, bindings, () => new());
        using var image = VisionImage.CopyFrom(new ImageInfo(2, 2, EPixelLayout.Gray8), new byte[4]);
        using var input = new ImageFrame("retained", image.Retain());
        var retained = frames.Retain(input);
        using var cancellation = new CancellationTokenSource();
        var pending = diagnostics.CheckAsync(document, cancellation.Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Contains("取消", diagnostics.Status); Assert.Equal("retained", retained.FrameId);
        Assert.Equal(2, retained.Image.Info.Width);
        proceed.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => diagnostics.CheckAsync(document, CancellationToken.None));
        var issue = Assert.Single(diagnostics.Analyze(document));
        Assert.Equal("color", issue.NodeId); Assert.False(issue.BlocksRun); Assert.Contains("native model invalid", issue.Detail);
    }

    [Fact]
    public async Task CheckResult_FromOldRevision_CannotOverwriteEditedRecipeStatus()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new VisionAlgorithmFactory<IColorAnalyzer>(async (_, _, _) =>
        { entered.SetResult(); await proceed.Task; throw new InvalidOperationException("obsolete check"); });
        var catalog = Catalog(factory); using var runtime = new VisionAlgorithmRuntime(catalog);
        using var bindings = new WorkflowVisionAlgorithmBindings(runtime);
        using var diagnostics = new WorkflowVisionAlgorithmDiagnostics(catalog, runtime, bindings, () => new());
        var document = Document(new AnalyzeVisionColorNodeModel { Id = "color", Algorithm = new() { ImplementationId = "test.color" } });
        var pending = diagnostics.CheckAsync(document, CancellationToken.None); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        diagnostics.Invalidate(); proceed.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Contains("配方已修改", diagnostics.Status); Assert.Empty(diagnostics.Analyze(document));
    }

    [Fact]
    public void Migration_UsesUndoTransaction_AndMachinePathsUseConfigurationDirectory()
    {
        var factory = new VisionAlgorithmFactory<IColorAnalyzer>((_, _, _) => throw new InvalidOperationException("not initialize"))
            .WithConfigurationPolicy(c => c.SettingsVersion == 2 ? Array.Empty<string>() : new[] { "version2 required" },
                c => c.SettingsVersion == 1 ? new VisionAlgorithmConfiguration(2, new Dictionary<string, string> { ["value"] = c.Settings["old"] }) : null);
        var catalog = Catalog(factory); using var runtime = new VisionAlgorithmRuntime(catalog); using var bindings = new WorkflowVisionAlgorithmBindings(runtime);
        using var diagnostics = new WorkflowVisionAlgorithmDiagnostics(catalog, runtime, bindings, () => new());
        var node = new AnalyzeVisionColorNodeModel { Id = "color", Algorithm = new() { ImplementationId = "test.color", Settings = new() { ["old"] = "value" } } };
        var session = new WorkflowDesignerSession(Document(node), new WorkflowNodeCatalog().RegisterImageNodes());
        diagnostics.MigrateNode(session, node.Id); Assert.Equal(2, node.Algorithm.SettingsVersion);
        Assert.True(session.Undo()); Assert.Equal(1, node.Algorithm.SettingsVersion); Assert.Equal("value", node.Algorithm.Settings["old"]);
        Assert.True(session.Redo()); Assert.Equal(2, node.Algorithm.SettingsVersion);
        var root = Path.Combine(Path.GetTempPath(), "machine-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var file = Path.Combine(root, "algorithm-environment.json");
        try
        {
            File.WriteAllText(file, "{\"settingsVersion\":1,\"resourceDirectory\":\"Models\"}");
            var environment = WorkflowVisionAlgorithmEnvironment.Load(root);
            Assert.Equal(Path.Combine(root, "Models"), environment.Capture(null).ResourceDirectory);
            Assert.Null(environment.Capture(null).RecipeDirectory);
            File.WriteAllText(file, "{\"settingsVersion\":99}");
            Assert.Throws<InvalidOperationException>(() => WorkflowVisionAlgorithmEnvironment.Load(root));
        }
        finally { File.Delete(file); Directory.Delete(root); }
    }

    private static VisionAlgorithmCatalog Catalog(IVisionAlgorithmFactory factory) => VisionAlgorithmCatalog.Compose(new[] { new Module(factory) });
    private sealed class Module(IVisionAlgorithmFactory factory) : IVisionAlgorithmModule
    { public string ExtensionId => "test"; public void Register(IVisionAlgorithmRegistration registrations) => registrations.Add(new("test.color", "Test", "1", factory)); }
    private static WorkflowDocument Document(params IWorkflowNodeModel[] nodes)
    { var document = new WorkflowDocument { EntryNodeId = nodes[0].Id }; foreach (var node in nodes) document.CanvasProjection.Nodes.Add(new() { Node = node }); return document; }
}
