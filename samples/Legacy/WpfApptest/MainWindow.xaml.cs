using System.Windows;
using DP.Vision.Algorithms;
using DP.Vision.OpenCv;
using DP.Vision.Halcon;
using DP.WorkFlow;
using DP.WorkFlow.UI;
using DP.WorkFlow.Vision.UI.Wpf;
using DP.WorkFlow.OperatorUI.Wpf;
using DP.WorkFlow.Samples;

namespace WpfApptest;

public partial class MainWindow : Window
{
    private readonly WorkflowDocumentWorkspace _workspace;
    private readonly WorkflowRuntimeHost _runtimeHost;
    private readonly WorkflowStudioRuntimeBinding _runtimeBinding;
    private readonly WorkflowVisionFrameScope _frameScope;
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
        var fileReader = new OpenCvImageFileReader();
        var acquisition = new WorkflowVisionAcquisitionSession(fileReader);
        _frameScope = new WorkflowVisionFrameScope(acquisition);
        _workspace = new WorkflowDocumentWorkspace(catalog);
        _workspace.New(recoveryDemo is null ? "视觉文件分析" : "异常恢复演示（仅软件模拟）");
        if (recoveryDemo is null) WorkflowImageDemo.PopulateProcessing(_workspace.Navigator!.RootSession);
        else recoveryDemo.Populate(_workspace.Navigator!.RootSession);
        _workspace.Navigator.RootSession.PublicDataCatalog
            .Register<DP.Vision.ImageFrame>("VisionFrame", "显式发布的图像帧", "视觉数据")
            .Register<BlobAnalysisResult>("VisionBlobs", "显式发布的连通域事实", "视觉数据")
            .Register<ColorAnalysisResult>("VisionColor", "显式发布的颜色事实", "视觉数据");
        Studio.Workspace = _workspace;
        Studio.NodeEditorExtensions.Register(new VisionWpfStudioExtension
        { FrameSource = _frameScope, FileReader = fileReader });
        var actions = new WorkflowActionRegistry();
        var services = new WorkflowServiceProvider()
            .Add<IWorkflowOperatorService>(_operatorService)
            .Add<IWorkflowActionRegistry>(actions)
            .Add<IImageFileReader>(fileReader)
            .Add<IBlobAnalyzer>(new OpenCvBlobAnalyzer())
            .Add<IImagePreprocessor>(new OpenCvImagePreprocessor())
            .Add<IRegionProcessor>(new OpenCvRegionProcessor())
            .Add<IBlobSelector>(new BlobSelector())
            .Add<ICaliperMeasurer>(new CaliperMeasurer())
            .Add<IRobustLineFitter>(new RobustLineFitter())
            .Add<IColorAnalyzer>(new RgbColorAnalyzer())
            .Add<IEdgeMeasurer>(new OpenCvEdgeMeasurer())
            .Add<ITemplateLocator>(new OpenCvTemplateLocator())
            .Add<ITemplatePoseLocator>(new OpenCvTemplatePoseLocator())
            .Add<IWorkflowVisionFrameScope>(_frameScope)
            .Add<IWorkflowVisionFolderSource>(acquisition)
            .Add<IWorkflowRunPreparationService>(_frameScope);
        if (HalconCameraCapture.IsSdkEnabled) services.Add<ICameraCapture>(new HalconCameraCapture());
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
        _disposed = true;
        Studio.InteractionError -= OnInteractionError;
        Studio.RuntimeBinding = null;
        _operatorService.Dispose();
        _runtimeBinding.Dispose(); _runtimeHost.Dispose(); _frameScope.Dispose(); _workspace.Dispose();
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }
}
