using System.Windows;
using DP.Vision.Acquisition;
using DP.Vision.Algorithms;
using DP.Vision.OpenCv;
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
    private readonly VisionAcquisitionRuntime _visionAcquisition;
    private readonly WorkflowVisionSourceCatalog _visionSources;
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
        // 机器配置：插件目录 + 公共Source绑定；工作流文档只保存SourceId。
        // 宿主只认识 plugin.json 与中立插件契约，编译期不选择任何具体Provider。
        // 两个逻辑源分属两家厂商（HALCON / Basler），由同一组合按SourceId路由到各自的设备。
        var providerPluginDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "plugins");
        var providerPlugins = new VisionAcquisitionProviderPluginLoader()
            .Load(providerPluginDirectory, ReadProviderConfiguration);
        var sourceBindings = new[]
        {
            new VisionAcquisitionSourceBinding("Camera.Top", "dp.vision.halcon", "top-camera", "camera:serial:DEMO0001"),
            new VisionAcquisitionSourceBinding("Camera.Side", "dp.vision.basler", "side-camera", "camera:serial:DEMO-BASLER-0001")
        };
        _visionAcquisition = new VisionAcquisitionRuntime(
            new VisionAcquisitionProviderComposer().Compose(providerPlugins.Modules, sourceBindings));
        // 插件包整体加载失败（例如投放不完整、缺厂商程序集）必须出现在诊断里，
        // 否则界面只会显示"Provider 未安装"，把真实原因藏起来。
        var pluginLoadFailure = providerPlugins.Failures.Count == 0
            ? null
            : "插件包加载失败：" + string.Join(
                "；",
                providerPlugins.Failures.Select(failure => failure.ManifestPath + " -> " + failure.Reason));
        _visionSources = new WorkflowVisionSourceCatalog(sourceBindings.Select(binding =>
        {
            var availability = providerPlugins.ProviderAvailability
                .FirstOrDefault(item => string.Equals(item.ProviderId, binding.ProviderId, StringComparison.Ordinal));
            return new WorkflowVisionSourceInfo(
                binding.SourceId,
                binding.ProviderId,
                binding.SharingPolicy,
                isAvailable: availability?.IsAvailable ?? false,
                diagnostic: availability is null
                    ? $"Provider {binding.ProviderId} 未安装：插件目录 {providerPluginDirectory} 中没有加载到该Provider。"
                      + (pluginLoadFailure is null ? string.Empty : " " + pluginLoadFailure)
                    : availability.Diagnostic);
        }));
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
        // 采集节点的"逻辑图像源"从本机已发布的源里选。
        Studio.Properties.ChoiceProvider = (editorKey, _) =>
            string.Equals(editorKey, WorkflowPropertyEditorKeys.VisionSource, StringComparison.Ordinal)
                ? _visionSources.Sources.Select(source => new WorkflowPropertyChoice(
                    source.IsAvailable ? source.SourceId : $"{source.SourceId}（不可用：{source.Diagnostic}）",
                    new VisionSourceReference(source.SourceId))).ToArray()
                : Array.Empty<WorkflowPropertyChoice>();
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
            .Add<IVisionAcquisition>(_visionAcquisition)
            .Add<IWorkflowVisionSourceCatalog>(_visionSources)
            .Add<IWorkflowRunPreparationService>(_frameScope);
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

    /// <summary>按PluginId读取该Provider的私有配置；宿主只转交文本，不解释其中任何字段。</summary>
    private static string? ReadProviderConfiguration(string pluginId)
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "vision-providers", pluginId + ".json");
        return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;
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
        try { await _visionAcquisition.DisposeAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        _disposed = true;
        Studio.InteractionError -= OnInteractionError;
        Studio.RuntimeBinding = null;
        _operatorService.Dispose();
        _runtimeBinding.Dispose(); _runtimeHost.Dispose(); _frameScope.Dispose(); _workspace.Dispose();
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }
}
