using System.Windows;
using DP.WorkFlow;
using DP.WorkFlow.UI;
using DP.WorkFlow.Vision.UI;
using DP.WorkFlow.Vision.UI.Wpf;
using DP.WorkFlow.OperatorUI.Wpf;
using DP.WorkFlow.Samples;

namespace WpfApptest;

public partial class MainWindow : Window
{
    private readonly WorkflowDocumentWorkspace _workspace;
    private readonly WorkflowRuntimeHost _runtimeHost;
    private readonly WorkflowStudioRuntimeBinding _runtimeBinding;
    private readonly SampleVisionHost _vision;
    private readonly WorkflowWpfOperatorService _operatorService;
    private bool _closing;
    private bool _disposed;

    public MainWindow()
    {
        InitializeComponent();
        _operatorService = new WorkflowWpfOperatorService(this);
        var recoveryDemo = Environment.GetCommandLineArgs().Contains("--recovery-demo", StringComparer.OrdinalIgnoreCase)
            ? new WorkflowRecoveryDemo() : null;
        var catalog = new WorkflowNodeCatalog();
        var handlers = new WorkflowNodeHandlerCatalog();
        var plugins = new WorkflowRuntimePluginCatalog(catalog, handlers)
            .Register(new WorkflowStandardRuntimePluginModule())
            .Register(new WorkflowProcessRuntimePluginModule())
            .Register(new WorkflowImageRuntimePluginModule());
        if (recoveryDemo is not null) plugins.Register(recoveryDemo);
        plugins.Freeze();
        // 采集插件、机器配置与图像源目录见 SampleVisionHost。
        _vision = new SampleVisionHost();
        _workspace = new WorkflowDocumentWorkspace(catalog);
        _workspace.New(recoveryDemo is null ? "视觉文件分析" : "异常恢复演示（仅软件模拟）");
        if (recoveryDemo is null) WorkflowImageDemo.PopulateProcessing(_workspace.Navigator!.RootSession);
        else recoveryDemo.Populate(_workspace.Navigator!.RootSession);
        SampleVisionHost.RegisterPublicData(_workspace.Navigator.RootSession);
        Studio.Workspace = _workspace;
        Studio.NodeEditorExtensions.Register(new VisionWpfStudioExtension
        { FrameSource = _vision.FrameScope, FileReader = _vision.FileReader });
        // 采集节点的"逻辑图像源"从本机已发布的源里选；面阵节点与线扫节点各看各的采集类型。
        Studio.Properties.ChoiceProvider = WorkflowVisionSourceChoices.CreateProvider(_vision.Sources);
        var actions = new WorkflowActionRegistry();
        var services = _vision.AddServices(new WorkflowServiceProvider())
            .Add<IWorkflowOperatorService>(_operatorService)
            .Add<IWorkflowActionRegistry>(actions);
        recoveryDemo?.ConfigureServices(services, catalog, handlers, actions);
        _runtimeHost = new WorkflowRuntimeHost(catalog, handlers);
        _runtimeBinding = new WorkflowStudioRuntimeBinding(_runtimeHost, _workspace.Navigator)
        { AutoConfigureBeforeRun = true, RunContext = new WorkflowContext(services) };
        if (recoveryDemo is not null)
        {
            _runtimeBinding.AutoConfigureBeforeRun = false;
            _runtimeBinding.ConfigureBeforeRun = () =>
            {
                if (_runtimeBinding.RunTarget != WorkflowStudioRunTarget.RootWorkflow)
                    throw new InvalidOperationException("恢复演示请从根流程运行。");
                var document = _workspace.Navigator!.RootSession.Document;
                _runtimeHost.Configure(document, _runtimeBinding.RunContext);
                recoveryDemo.RebindTreatment(document, services, catalog, handlers);
            };
        }
        Studio.RuntimeBinding = _runtimeBinding;
        Studio.ConfirmDiscardChanges = () => MessageBox.Show(this,
            "当前流程尚未保存，是否放弃修改？", "确认", MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
        Studio.InteractionError += OnInteractionError;
        Closing += OnClosing;
    }

    private void OnInteractionError(object? sender, string message) =>
        MessageBox.Show(this, message, "工作流错误", MessageBoxButton.OK, MessageBoxImage.Error);

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs eventArgs)
    {
        if (_disposed) return;
        eventArgs.Cancel = true;
        if (_closing) return;
        _closing = true;
        Studio.IsEnabled = false;
        try { await _runtimeHost.StopAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        // 设备会话必须异步释放；放在这里等待，避免在同步释放路径上阻塞UI线程。
        try { await _vision.Acquisition.DisposeAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        _disposed = true;
        Studio.InteractionError -= OnInteractionError;
        Studio.RuntimeBinding = null;
        _operatorService.Dispose();
        _runtimeBinding.Dispose(); _runtimeHost.Dispose(); _vision.FrameScope.Dispose(); _workspace.Dispose();
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }
}
