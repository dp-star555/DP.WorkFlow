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
        // V2 机器配置：插件自动发现 + 版本化CameraDefinition；工作流文档只保存SourceId。
        // 宿主只按插件目录自动发现Driver Module，编译期不选择任何具体Provider。
        // deviceSettings由对应Plugin解析，生成内部绑定、规范资源键与进入CompositionId的私有配置摘要。
        const string machineConfigurationJson = """
            [
              {
                "sourceId": "Camera.Top",
                "acquisitionType": "dp.acquisition.halcon.area",
                "settingsVersion": 1,
                "connection": { "openOnApplicationStart": true, "transferStart": "PerRequest" },
                "deviceSettings": {
                  "interfaceName": "GigEVision2",
                  "deviceName": "cam-top",
                  "serialNumber": "DEMO0001"
                }
              },
              {
                "sourceId": "Camera.Side",
                "acquisitionType": "dp.acquisition.basler.area",
                "settingsVersion": 1,
                "connection": { "openOnApplicationStart": true, "transferStart": "PerRequest" },
                "deviceSettings": { "serialNumber": "DEMO-BASLER-0001" }
              }
            ]
            """;
        var providerPluginDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "plugins");
        var driverModules = new VisionAcquisitionDriverModuleLoader().Load(providerPluginDirectory);
        var typeCatalog = new VisionAcquisitionTypeCatalogComposer().Compose(driverModules.Modules);
        var cameras = VisionAcquisitionMachineConfigurationParser.Parse(machineConfigurationJson);
        var composition = new VisionAcquisitionMachineConfigurationComposer()
            .Compose(typeCatalog, cameras);
        _visionAcquisition = new VisionAcquisitionRuntime(composition);
        // 插件包整体加载失败（例如投放不完整、缺厂商程序集）必须出现在诊断里，
        // 否则界面只会显示"未安装"，把真实原因藏起来。
        var driverLoadFailure = driverModules.Failures.Count == 0
            ? null
            : "Driver Module 加载失败：" + string.Join(
                "；",
                driverModules.Failures.Select(failure => failure.AssemblyPath + " -> " + failure.Reason));
        var projectedSources = WorkflowVisionSourceCatalog.FromAcquisition(composition);
        _visionSources = driverLoadFailure is null
            ? projectedSources
            : new WorkflowVisionSourceCatalog(projectedSources.Sources.Select(source =>
                source.IsAvailable || source.Diagnostic is null
                    ? source
                    : new WorkflowVisionSourceInfo(
                        source.SourceId,
                        source.ProviderId,
                        source.SharingPolicy,
                        source.IsAvailable,
                        source.Diagnostic + " " + driverLoadFailure,
                        source.AcquisitionMode)));
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
            // 准备服务只做校验；退役上一轮资源是运行所有者的职责，只有根运行宿主持有它（AR-01 阶段2）。
            .Add<IWorkflowRunPreparationService>(_frameScope)
            .Add<IWorkflowRunResourceOwner>(_frameScope)
            // 本轮作用域取得：外部回调缓冲源要在采集节点之前布防。桥接是 Kernel 与采集侧之间唯一的连接点，
            // 嵌套调用点不解析 IWorkflowRunScopeOwner，因此结构上无法重新布防或清空父运行队列。
            .Add<IWorkflowRunScopeOwner>(new VisionAcquisitionRunScope(_visionAcquisition));
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
