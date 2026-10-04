using DP.Plugins;
using DP.Vision.Acquisition;
using DP.Vision.Algorithms;
using DP.WorkFlow;
using DP.WorkFlow.UI;
using DP.WorkFlow.UI.WinForms;
using DP.WorkFlow.Vision.UI;
using DP.WorkFlow.Vision.UI.WinForms;
using DP.WorkFlow.OperatorUI.WinForms;
using DP.WorkFlow.Samples;

namespace WinFormsApp_test;

public partial class Form1 : Form
{
    private readonly WorkflowNodeCatalog _nodeCatalog;
    private readonly WorkflowDocumentWorkspace _workspace;
    private readonly WorkflowRuntimeHost _runtimeHost;
    private readonly WorkflowStudioRuntimeBinding _runtimeBinding;
    private readonly WorkflowContext _runtimeContext;
    private readonly WorkflowVisionFrameScope _frameScope;
    private readonly VisionAlgorithmRuntime _algorithmRuntime;
    private readonly WorkflowVisionAlgorithmBindings _algorithmBindings;
    private readonly WorkflowVisionAlgorithmDiagnostics _algorithmDiagnostics;
    private readonly VisionAcquisitionRuntime _visionAcquisition;
    private readonly WorkflowVisionSourceCatalog _visionSources;
    private readonly WorkflowWinFormsOperatorService _operatorService;
    private bool _closing;
    private bool _runtimeResourcesDisposed;

    public Form1()
    {
        InitializeComponent();
        var pluginDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "plugins");
        var loadSession = new PluginLoadSession();
        loadSession.RegisterSharedAssembly(typeof(IWorkflowVisionAlgorithmNode).Assembly);
        var algorithmLoader = new VisionAlgorithmModuleLoader(loadSession);
        var driverLoader = new VisionAcquisitionDriverModuleLoader(loadSession);
        var workflowLoader = new WorkflowPluginLoader(loadSession);
        loadSession.RegisterSharedContracts(pluginDirectory);
        _operatorService = new WorkflowWinFormsOperatorService(this);
        var recoveryDemo = Environment.GetCommandLineArgs().Contains("--recovery-demo", StringComparer.OrdinalIgnoreCase)
            ? new WorkflowRecoveryDemo() : null;

        // 1. 通过领域 Runtime Module 成组注册节点和 Handler，并在宿主启动时验证后冻结。
        _nodeCatalog = new WorkflowNodeCatalog();
        var handlers = new WorkflowNodeHandlerCatalog();
        var plugins = new WorkflowRuntimePluginCatalog(_nodeCatalog, handlers)
            .Register(new WorkflowStandardRuntimePluginModule())
            .Register(new WorkflowCompositeRuntimePluginModule())
            .Register(new WorkflowMotionRuntimePluginModule())
            .Register(new WorkflowProcessRuntimePluginModule())
            .Register(new WorkflowImageRuntimePluginModule());
        if (recoveryDemo is not null) plugins.Register(recoveryDemo);
        plugins.LoadPlugins(pluginDirectory, workflowLoader);
        foreach (var failure in workflowLoader.DiscoveryFailures)
            System.Diagnostics.Trace.TraceError(failure.AssemblyPath + ": " + failure.Reason);
        plugins.Freeze();
        var algorithmCatalog = algorithmLoader.Load(pluginDirectory, new[] { new ManagedVisionAlgorithmModule() });
        foreach (var failure in algorithmCatalog.Diagnostics)
            System.Diagnostics.Trace.TraceError(failure.Source + ": " + failure.Reason);
        _algorithmRuntime = new VisionAlgorithmRuntime(algorithmCatalog);

        // 2. 创建文档工作区
        _workspace = new WorkflowDocumentWorkspace(_nodeCatalog);
        var algorithmEnvironment = WorkflowVisionAlgorithmEnvironment.Load(AppContext.BaseDirectory);
        VisionAlgorithmResourceContext Resources() => algorithmEnvironment.Capture(_workspace.CurrentFilePath);
        var fileReader = new WorkflowVisionImageFileReader(_algorithmRuntime, new VisionAlgorithmSelection { ImplementationId = "opencv.image-read" });
        var acquisition = new WorkflowVisionAcquisitionSession(fileReader);
        _frameScope = new WorkflowVisionFrameScope(acquisition);
        _algorithmBindings = new WorkflowVisionAlgorithmBindings(_algorithmRuntime, _frameScope, Resources);
        _algorithmDiagnostics = new WorkflowVisionAlgorithmDiagnostics(algorithmCatalog, _algorithmRuntime, _algorithmBindings, Resources);
        _workspace.DocumentChanged += (_, _) => _algorithmDiagnostics.Invalidate();

        // 2.1 V2 机器配置：插件自动发现 + 版本化CameraDefinition。工作流文档只保存SourceId，
        // 换机器时只改这里，不需要改流程文档，也不需要重新编译节点。
        // 宿主只按插件目录自动发现Driver Module，编译期不选择任何具体Provider。
        // 公共层只解释sourceId/acquisitionType/connection等字段；deviceSettings由对应Plugin解析，
        // 生成内部绑定、规范资源键与进入CompositionId的私有配置摘要。
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
        var providerPluginDirectory = Path.Combine(AppContext.BaseDirectory, "plugins");
        var driverModules = driverLoader.Load(providerPluginDirectory);
        var typeCatalog = new VisionAcquisitionTypeCatalogComposer().Compose(driverModules.Modules);
        var cameras = VisionAcquisitionMachineConfigurationParser.Parse(machineConfigurationJson);
        var composition = new VisionAcquisitionMachineConfigurationComposer()
            .Compose(typeCatalog, cameras);
        _visionAcquisition = new VisionAcquisitionRuntime(composition);
        // V2-3：设备连接属于软件生命周期，宿主在进入可运行状态前启动Runtime：
        // 按ResourceKey真正打开设备；Required失败→NotReady，Optional失败→Degraded。
        var runtimeState = _visionAcquisition.StartAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        // 插件包整体加载失败（例如投放不完整、缺厂商程序集）必须出现在诊断里，
        // 否则界面只会显示"未安装"，把真实原因藏起来。
        var driverLoadFailure = driverModules.Failures.Count == 0
            ? null
            : "Driver Module 加载失败：" + string.Join(
                "；",
                driverModules.Failures.Select(failure => failure.AssemblyPath + " -> " + failure.Reason));
        var runtimeFailure = runtimeState == EVisionRuntimeState.Ready
            ? null
            : $"采集运行时未就绪（{runtimeState}），部分或全部设备未连接。";
        var projectedSources = WorkflowVisionSourceCatalog.FromAcquisition(composition);
        var startupDiagnostic = string.Join(
            " ",
            new[] { driverLoadFailure, runtimeFailure }.Where(item => item is not null));
        _visionSources = startupDiagnostic.Length == 0
            ? projectedSources
            : new WorkflowVisionSourceCatalog(projectedSources.Sources.Select(source =>
                source.IsAvailable || source.Diagnostic is null
                    ? source
                    : new WorkflowVisionSourceInfo(
                        source.SourceId,
                        source.ProviderId,
                        source.SharingPolicy,
                        source.IsAvailable,
                        source.Diagnostic + " " + startupDiagnostic,
                        source.AcquisitionMode,
                        source.Kind)));

        // 3. 创建新文档
        _workspace.New(recoveryDemo is null ? "新版视觉文件分析" : "异常恢复演示（仅软件模拟）");
        if (recoveryDemo is null && Environment.GetCommandLineArgs().Contains("--barcode-demo", StringComparer.OrdinalIgnoreCase)) WorkflowImageDemo.PopulateBarcode(_workspace.Navigator!.RootSession);
        else if (recoveryDemo is null && Environment.GetCommandLineArgs().Contains("--coordinate-demo", StringComparer.OrdinalIgnoreCase)) WorkflowImageDemo.PopulateCoordinates(_workspace.Navigator!.RootSession);
        else if (recoveryDemo is null && Environment.GetCommandLineArgs().Contains("--geometry-demo", StringComparer.OrdinalIgnoreCase)) WorkflowImageDemo.PopulateGeometry(_workspace.Navigator!.RootSession);
        else if (recoveryDemo is null) WorkflowImageDemo.PopulateProcessing(_workspace.Navigator!.RootSession);
        else recoveryDemo.Populate(_workspace.Navigator!.RootSession);
        _workspace.Navigator!.RootSession.PublicDataCatalog
            .Register<DP.Vision.ImageFrame>("VisionFrame", "显式发布的图像帧", "视觉数据")
            .Register<BlobAnalysisResult>("VisionBlobs", "显式发布的连通域事实", "视觉数据")
            .Register<ColorAnalysisResult>("VisionColor", "显式发布的颜色事实", "视觉数据");

        // 4. 连接到设计器控件
        workflowStudioControl1.Workspace = _workspace;
        workflowStudioControl1.Diagnostics.Provider = _algorithmDiagnostics;
        workflowStudioControl1.AddToolPage("插件与算法", new WorkflowVisionAlgorithmPanel(_algorithmDiagnostics,
            () => _workspace.Navigator?.RootDocument, () => _workspace.Navigator?.CurrentSession));
        workflowStudioControl1.NodeEditorExtensions.Register(
            new VisionWinFormsStudioExtension()
            { FrameSource = _frameScope, FileReader = fileReader, Templates = new VisionTemplateEditingRuntime(algorithmCatalog, _algorithmRuntime, Resources) });
        // 采集节点的"逻辑图像源"从本机已发布的源里选，避免手写出机器上不存在的标识；
        // 面阵节点与线扫节点各看各的采集类型，不能互相选到对方的源。
        workflowStudioControl1.Properties.ChoiceProvider = WorkflowVisionAlgorithmChoices.CreateProvider(algorithmCatalog, WorkflowVisionSourceChoices.CreateProvider(_visionSources));
        workflowStudioControl1.Properties.AdditionalProperties = WorkflowVisionAlgorithmProperties.CreateProvider(algorithmCatalog);

        // 5. 注册宿主运行能力；节点和 Handler 已由上面的 Runtime Module 成组注册。
        var actions = new WorkflowActionRegistry()
            .Register("Run", static (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult<object?>("DEMO-OK");
            });
        var conditions = new WorkflowConditionRegistry()
            .Register("AlwaysTrue", static (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(true);
            })
            .Register("AlwaysFalse", static (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(false);
            });
        var services = new WorkflowServiceProvider()
            .Add<IWorkflowVisionFrameScope>(_frameScope)
            .Add<IWorkflowVisionFolderSource>(acquisition)
            .Add<IVisionAcquisition>(_visionAcquisition)
            .Add<IWorkflowVisionSourceCatalog>(_visionSources)
            .Add<IWorkflowActionRegistry>(actions)
            .Add<IWorkflowConditionRegistry>(conditions)
            .Add<IWorkflowSignalService>(new WorkflowSignalService())
            .Add<IWorkflowValueSignalService>(new WorkflowValueSignalService())
            .Add<IWorkflowQueueService>(new WorkflowQueueService())
            .Add<IWorkflowIoService>(new DemoIoService())
            .Add<IWorkflowAxisService>(new DemoAxisService())
            .Add<IWorkflowPneumaticService>(new DemoPneumaticService())
            .Add<IWorkflowCodeReaderService>(new DemoCodeReaderService())
            .Add<IWorkflowOperatorService>(_operatorService)
            .Add<IWorkflowProcessService>(new DemoProcessService())
            .Add<IWorkflowProductFlowService>(new DemoProductFlowService())
            .Add<IWorkflowWaferRobotService>(new DemoWaferRobotService())
            .Add<IWorkflowRecoveryService>(new DemoRecoveryService())
            // 准备服务只做校验；退役上一轮资源是运行所有者的职责，只有根运行宿主持有它（AR-01 阶段2）。
            .Add<IWorkflowVisionAlgorithmBindings>(_algorithmBindings)
            .Add<IWorkflowNodeCapabilityProvider>(_algorithmBindings)
            .Add<IWorkflowRunPreparationService>(_algorithmBindings)
            .Add<IWorkflowRunResourceOwner>(_frameScope)
            // 本轮作用域取得：外部回调缓冲源要在采集节点之前布防。桥接是 Kernel 与采集侧之间唯一的连接点，
            // 嵌套调用点不解析 IWorkflowRunScopeOwner，因此结构上无法重新布防或清空父运行队列。
            .Add<IWorkflowRunScopeOwner>(new VisionAcquisitionRunScope(_visionAcquisition));
        recoveryDemo?.ConfigureServices(services, _nodeCatalog, handlers, actions);
        _runtimeContext = new WorkflowContext(services);
        _runtimeHost = new WorkflowRuntimeHost(_nodeCatalog, handlers);
        _runtimeBinding = new WorkflowStudioRuntimeBinding(_runtimeHost, _workspace.Navigator!)
        {
            AutoConfigureBeforeRun = true,
            RunContext = _runtimeContext
        };
        if (recoveryDemo is not null)
        {
            _runtimeBinding.AutoConfigureBeforeRun = false;
            _runtimeBinding.ConfigureBeforeRun = () =>
            {
                if (_runtimeBinding.RunTarget != WorkflowStudioRunTarget.RootWorkflow)
                    throw new InvalidOperationException("恢复演示请从根流程运行。");
                var document = _workspace.Navigator!.RootSession.Document;
                _runtimeHost.Configure(document, _runtimeContext);
                recoveryDemo.RebindTreatment(document, services, _nodeCatalog, handlers);
            };
        }
        workflowStudioControl1.RuntimeBinding = _runtimeBinding;
        _runtimeBinding.RunConfiguring += _algorithmDiagnostics.SetRunBasePath;

        workflowStudioControl1.ConfirmDiscardChanges = () =>
            MessageBox.Show(
                this,
                "当前流程尚未保存，是否放弃修改？",
                "确认",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) == DialogResult.Yes;

        workflowStudioControl1.InteractionError += OnInteractionError;
    }

    private void OnInteractionError(object? sender, string message) =>
        MessageBox.Show(
            this,
            message,
            "工作流错误",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_runtimeResourcesDisposed)
        {
            e.Cancel = true;
            if (_closing) return;
            _closing = true;
            workflowStudioControl1.Enabled = false;
            try { await _runtimeHost.StopAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            // 设备会话必须异步释放；放在这里等待，避免在同步释放路径上阻塞UI线程。
            try { await _visionAcquisition.DisposeAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            DisposeRuntimeResources();
            BeginInvoke(new Action(Close));
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        DisposeRuntimeResources();
        base.OnFormClosed(e);
    }

    private void DisposeRuntimeResources()
    {
        if (_runtimeResourcesDisposed)
            return;
        _runtimeResourcesDisposed = true;
        workflowStudioControl1.InteractionError -= OnInteractionError;
        workflowStudioControl1.RuntimeBinding = null;
        _runtimeHost.Cancel();
        _operatorService.Dispose();
        _runtimeBinding.Dispose();
        _runtimeHost.Dispose();
        _algorithmDiagnostics.Dispose(); _algorithmBindings.Dispose(); _algorithmRuntime.Dispose(); _frameScope.Dispose();
        _workspace.Dispose();
    }
}
