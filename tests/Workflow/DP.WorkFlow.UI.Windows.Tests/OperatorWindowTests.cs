using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DP.WorkFlow.OperatorUI.WinForms;
using DP.WorkFlow.OperatorUI.Wpf;
using DP.WorkFlow.Samples;
using DP.WorkFlow.UI;
using Forms = System.Windows.Forms;

namespace DP.WorkFlow.Tests;

public sealed class OperatorWindowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task WinForms_AuthoredRecoveryRunsThroughRealButtons(bool restart) => RunUiAsync(async () =>
    {
        using var owner = new Forms.Form { Text = "恢复测试", Width = 800, Height = 500 };
        using var interaction = new WorkflowWinFormsOperatorService(owner);
        Assert.False(owner.IsHandleCreated);
        owner.Show();
        using var demo = new DemoRun(interaction);
        var running = demo.Host.RunAsync();
        PumpUntil(() => WinFormsButtons(owner).Any(button => Equals(button.Tag, "Continue")));
        Assert.True(owner.Enabled);
        Assert.False(running.IsCompleted);
        var dialog = Assert.Single(owner.OwnedForms);
        Assert.Null(dialog.AcceptButton);
        var message = Descendants(dialog).OfType<Forms.TextBox>().Single();
        Assert.Contains(Environment.NewLine, message.Text);
        Assert.Equal(0, message.SelectionLength);
        if (!restart)
        {
            using var bitmap = new System.Drawing.Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bitmap.Size));
            bitmap.Save(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "workflow-operator-winforms.png"));
        }
        WinFormsButtons(owner).Single(button => Equals(button.Tag, restart ? "Restart" : "Continue")).PerformClick();
        if (restart)
        {
            PumpUntil(() => WinFormsButtons(owner).Any(button => Equals(button.Tag, "Confirm")));
            WinFormsButtons(owner).Single(button => Equals(button.Tag, "Confirm")).PerformClick();
        }
        var result = await running;
        Assert.True(result.Success, result.Message);
        demo.AssertResult(restart);
        PumpUntil(() => owner.OwnedForms.Length == 0);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Wpf_AuthoredRecoveryRunsThroughRealButtons(bool restart) => RunUiAsync(async () =>
    {
        var owner = new Window { Title = "恢复测试", Width = 800, Height = 500 };
        using var interaction = new WorkflowWpfOperatorService(owner);
        try
        {
            owner.Show();
            using var demo = new DemoRun(interaction);
            var running = demo.Host.RunAsync();
            PumpUntil(() => WpfButtons(owner).Any(button => Equals(button.Tag, "Continue")));
            Assert.True(owner.IsEnabled);
            Assert.False(running.IsCompleted);
            Assert.All(WpfButtons(owner), button => Assert.False(button.IsDefault));
            if (!restart)
            {
                var dialog = Assert.Single(owner.OwnedWindows.Cast<Window>());
                dialog.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth), (int)Math.Ceiling(dialog.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(dialog);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = System.IO.File.Create(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "workflow-operator-wpf.png"));
                encoder.Save(stream);
            }
            WpfButtons(owner).Single(button => Equals(button.Tag, restart ? "Restart" : "Continue")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (restart)
            {
                PumpUntil(() => WpfButtons(owner).Any(button => Equals(button.Tag, "Confirm")));
                WpfButtons(owner).Single(button => Equals(button.Tag, "Confirm")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            var result = await running;
            Assert.True(result.Success, result.Message);
            demo.AssertResult(restart);
            PumpUntil(() => owner.OwnedWindows.Count == 0);
        }
        finally { owner.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task StopRun_CancelsVisiblePromptAndNeverSendsSecondCommand(bool wpf) => RunUiAsync(async () =>
    {
        using var formsOwner = wpf ? null : new Forms.Form { Width = 700, Height = 450 };
        var wpfOwner = wpf ? new Window { Width = 700, Height = 450 } : null;
        using var adapter = wpf
            ? (IDisposable)new WorkflowWpfOperatorService(wpfOwner!)
            : new WorkflowWinFormsOperatorService(formsOwner!);
        try
        {
            formsOwner?.Show(); wpfOwner?.Show();
            using var demo = new DemoRun((IWorkflowOperatorService)adapter);
            var running = demo.Host.RunAsync();
            PumpUntil(() => wpf ? WpfButtons(wpfOwner!).Any() : WinFormsButtons(formsOwner!).Any());
            await demo.Host.StopAsync();
            Assert.Equal(E_WorkflowExecutionState.Canceled, (await running).State);
            Assert.DoesNotContain(demo.Context.ExportNodeOutputs(), output => output.Value is WorkflowRecoveryDemo.MoveResult);
            PumpUntil(() => wpf ? wpfOwner!.OwnedWindows.Count == 0 : formsOwner!.OwnedForms.Length == 0);
        }
        finally { wpfOwner?.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ClosingPrompt_IsNotAnImplicitConfirmation(bool wpf) => RunUiAsync(async () =>
    {
        using var formsOwner = wpf ? null : new Forms.Form { Width = 700, Height = 450 };
        var wpfOwner = wpf ? new Window { Width = 700, Height = 450 } : null;
        using var adapter = wpf
            ? (IDisposable)new WorkflowWpfOperatorService(wpfOwner!)
            : new WorkflowWinFormsOperatorService(formsOwner!);
        try
        {
            formsOwner?.Show(); wpfOwner?.Show();
            var pending = ((IWorkflowOperatorService)adapter).ConfirmStepAsync("人工处置", "尚未完成，关闭不能当作确认", default).AsTask();
            PumpUntil(() => wpf ? WpfButtons(wpfOwner!).Any() : WinFormsButtons(formsOwner!).Any());
            if (wpf) Assert.Single(wpfOwner!.OwnedWindows.Cast<Window>()).Close();
            else Assert.Single(formsOwner!.OwnedForms).Close();
            await Assert.ThrowsAsync<WorkflowOperatorPromptDismissedException>(() => pending);
        }
        finally { wpfOwner?.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DisposingHiddenOwnerAdapter_CancelsPendingAndQueuedPrompts(bool wpf) => RunUiAsync(async () =>
    {
        using var formsOwner = wpf ? null : new Forms.Form();
        var wpfOwner = wpf ? new Window() : null;
        using var adapter = wpf
            ? (IDisposable)new WorkflowWpfOperatorService(wpfOwner!)
            : new WorkflowWinFormsOperatorService(formsOwner!);
        try
        {
            var service = (IWorkflowOperatorService)adapter;
            var first = service.ConfirmStepAsync("first", "", default).AsTask();
            var second = service.ConfirmStepAsync("second", "", default).AsTask();
            adapter.Dispose();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        }
        finally { wpfOwner?.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task HideAndShowOwner_PreservesUnansweredTask(bool wpf) => RunUiAsync(async () =>
    {
        using var formsOwner = wpf ? null : new Forms.Form { Width = 700, Height = 450 };
        var wpfOwner = wpf ? new Window { Width = 700, Height = 450 } : null;
        using var adapter = wpf ? (IDisposable)new WorkflowWpfOperatorService(wpfOwner!) : new WorkflowWinFormsOperatorService(formsOwner!);
        try
        {
            formsOwner?.Show(); wpfOwner?.Show();
            var task = ((IWorkflowOperatorService)adapter).ConfirmStepAsync("处理", "尚未确认", default).AsTask();
            PumpUntil(() => wpf ? WpfButtons(wpfOwner!).Any() : WinFormsButtons(formsOwner!).Any());
            formsOwner?.Hide(); wpfOwner?.Hide();
            PumpUntil(() => wpf ? wpfOwner!.OwnedWindows.Count == 0 : formsOwner!.OwnedForms.Length == 0);
            Assert.False(task.IsCompleted);
            formsOwner?.Show(); wpfOwner?.Show();
            PumpUntil(() => wpf ? WpfButtons(wpfOwner!).Any() : WinFormsButtons(formsOwner!).Any());
            if (wpf) WpfButtons(wpfOwner!).Single(button => Equals(button.Tag, "Confirm")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else WinFormsButtons(formsOwner!).Single(button => Equals(button.Tag, "Confirm")).PerformClick();
            await task;
        }
        finally { wpfOwner?.Close(); }
    });

    [Fact]
    public Task WinForms_HandleRecreationDoesNotLosePendingPrompt() => RunUiAsync(async () =>
    {
        using var owner = new RecreatingForm();
        using var interaction = new WorkflowWinFormsOperatorService(owner);
        owner.Show();
        var task = interaction.ConfirmStepAsync("处理", "尚未确认", default).AsTask();
        PumpUntil(() => WinFormsButtons(owner).Any());
        var id = interaction.CurrentPrompt!.Id;
        owner.RecreateForTest();
        PumpUntil(() => WinFormsButtons(owner).Any());
        Assert.False(task.IsCompleted);
        Assert.Equal(id, interaction.CurrentPrompt!.Id);
        WinFormsButtons(owner).Single().PerformClick();
        await task;
    });

    private sealed class RecreatingForm : Forms.Form { public void RecreateForTest() => RecreateHandle(); }

    [Fact]
    public async Task DemoRebind_UsesEditedTreatmentInsteadOfOldSnapshot()
    {
        var nodes = new WorkflowNodeCatalog(); var handlers = new WorkflowNodeHandlerCatalog();
        var demo = new WorkflowRecoveryDemo();
        new WorkflowRuntimePluginCatalog(nodes, handlers).Register(new WorkflowStandardRuntimePluginModule())
            .Register(new WorkflowProcessRuntimePluginModule()).Register(demo).Freeze();
        using var workspace = new WorkflowDocumentWorkspace(nodes);
        workspace.New("恢复演示"); demo.Populate(workspace.Navigator!.RootSession);
        using var interaction = new WorkflowOperatorInteraction();
        var services = new WorkflowServiceProvider().Add<IWorkflowOperatorService>(interaction);
        demo.ConfigureServices(services, nodes, handlers, new WorkflowActionRegistry());
        var document = workspace.Navigator.RootSession.Document;
        var block = document.Graph.Nodes.OfType<WarningHandlerBlockNodeModel>().Single();
        block.SubDocument.CanvasProjection.Connections.Clear();
        block.SubDocument.CanvasProjection.Connections.Add(new WorkflowConnectionModel
            { FromNodeId = "HandlerStart", FromPort = WorkflowPorts.Success, ToNodeId = "Stop" });
        block.SubDocument.Graph.Nodes.OfType<StopCurrentStationNodeModel>().Single().Reason = "工程师改为停止";
        demo.RebindTreatment(document, services, nodes, handlers);
        using var host = new WorkflowRuntimeHost(nodes, handlers);
        host.Configure(document, new WorkflowContext(services));
        var result = await host.RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(result.Success);
        Assert.Contains("工程师改为停止", result.Message);
        Assert.Null(interaction.CurrentPrompt);
    }

    private static IEnumerable<Forms.Button> WinFormsButtons(Forms.Form owner) => owner.OwnedForms.SelectMany(form => Descendants(form).OfType<Forms.Button>());
    private static IEnumerable<Forms.Control> Descendants(Forms.Control control)
    {
        foreach (Forms.Control child in control.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static IEnumerable<Button> WpfButtons(Window owner) => owner.OwnedWindows.Cast<Window>().SelectMany(window => VisualDescendants(window).OfType<Button>());
    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject item)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(item); index++)
        {
            var child = VisualTreeHelper.GetChild(item, index); yield return child;
            foreach (var descendant in VisualDescendants(child)) yield return descendant;
        }
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(12)) throw new TimeoutException("等待人工窗口或流程结果超时。");
            Forms.Application.DoEvents();
            Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background);
            Thread.Yield();
        }
    }

    private static async Task RunUiAsync(Func<Task> action)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                var task = action();
                PumpUntil(() => task.IsCompleted);
                if (task.IsFaulted) completed.TrySetException(task.Exception!.InnerExceptions);
                else if (task.IsCanceled) completed.TrySetCanceled();
                else completed.TrySetResult();
            }
            catch (Exception failure) { completed.TrySetException(failure); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private sealed class DemoRun : IDisposable
    {
        private readonly WorkflowDocumentWorkspace _workspace;
        public WorkflowRuntimeHost Host { get; }
        public WorkflowContext Context { get; }
        public DemoRun(IWorkflowOperatorService interaction)
        {
            var nodes = new WorkflowNodeCatalog(); var handlers = new WorkflowNodeHandlerCatalog();
            var demo = new WorkflowRecoveryDemo();
            new WorkflowRuntimePluginCatalog(nodes, handlers).Register(new WorkflowStandardRuntimePluginModule())
                .Register(new WorkflowProcessRuntimePluginModule()).Register(demo).Freeze();
            _workspace = new WorkflowDocumentWorkspace(nodes); _workspace.New("恢复演示");
            demo.Populate(_workspace.Navigator!.RootSession);
            var services = new WorkflowServiceProvider().Add<IWorkflowOperatorService>(interaction);
            demo.ConfigureServices(services, nodes, handlers, new WorkflowActionRegistry());
            Context = new WorkflowContext(services);
            Host = new WorkflowRuntimeHost(nodes, handlers);
            Host.Configure(_workspace.Navigator.RootSession.Document, Context);
        }
        public void AssertResult(bool restart)
        {
            var result = Assert.Single(Context.ExportNodeOutputs().Select(output => output.Value).OfType<WorkflowRecoveryDemo.MoveResult>());
            Assert.Equal(restart ? 10 : 110, result.Position);
            Assert.Equal(restart ? 2 : 1, result.Commands);
            Assert.Equal(1, result.Feeds);
        }
        public void Dispose() { Host.Dispose(); _workspace.Dispose(); }
    }
}
