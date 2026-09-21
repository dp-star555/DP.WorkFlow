using DP.Vision.Acquisition;
using DP.Vision.Algorithms;
using DP.Vision.OpenCv;
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
    private readonly VisionAcquisitionRuntime _visionAcquisition;
    private readonly WorkflowVisionSourceCatalog _visionSources;
    private readonly WorkflowWinFormsOperatorService _operatorService;
    private bool _closing;
    private bool _runtimeResourcesDisposed;

    public Form1()
    {
        InitializeComponent();
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
        plugins.Freeze();

        // 2. 创建文档工作区
        _workspace = new WorkflowDocumentWorkspace(_nodeCatalog);
        var fileReader = new OpenCvImageFileReader();
        var acquisition = new WorkflowVisionAcquisitionSession(fileReader);
        _frameScope = new WorkflowVisionFrameScope(acquisition);

        // 2.1 机器配置：插件目录 + 公共Source绑定。工作流文档只保存SourceId，
        // 换机器时只改这里，不需要改流程文档，也不需要重新编译节点。
        // 宿主只认识 plugin.json 与中立插件契约，编译期不选择任何具体Provider。
        // 两个逻辑源分属两家厂商（HALCON / Basler），由同一组合按SourceId路由到各自的设备。
        var providerPluginDirectory = Path.Combine(AppContext.BaseDirectory, "plugins");
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

        // 3. 创建新文档
        _workspace.New(recoveryDemo is null ? "新版视觉文件分析" : "异常恢复演示（仅软件模拟）");
        if (recoveryDemo is null) WorkflowImageDemo.PopulateProcessing(_workspace.Navigator!.RootSession);
        else recoveryDemo.Populate(_workspace.Navigator!.RootSession);
        _workspace.Navigator!.RootSession.PublicDataCatalog
            .Register<DP.Vision.ImageFrame>("VisionFrame", "显式发布的图像帧", "视觉数据")
            .Register<BlobAnalysisResult>("VisionBlobs", "显式发布的连通域事实", "视觉数据")
            .Register<ColorAnalysisResult>("VisionColor", "显式发布的颜色事实", "视觉数据");

        // 4. 连接到设计器控件
        workflowStudioControl1.Workspace = _workspace;
        workflowStudioControl1.NodeEditorExtensions.Register(
            new VisionWinFormsStudioExtension()
            { FrameSource = _frameScope, FileReader = fileReader });
        // 采集节点的"逻辑图像源"从本机已发布的源里选，避免手写出机器上不存在的标识。
        workflowStudioControl1.Properties.ChoiceProvider = (editorKey, _) =>
            string.Equals(editorKey, WorkflowPropertyEditorKeys.VisionSource, StringComparison.Ordinal)
                ? _visionSources.Sources.Select(source => new WorkflowPropertyChoice(
                    source.IsAvailable ? source.SourceId : $"{source.SourceId}（不可用：{source.Diagnostic}）",
                    new VisionSourceReference(source.SourceId))).ToArray()
                : Array.Empty<WorkflowPropertyChoice>();

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
            .Add<IWorkflowRunPreparationService>(_frameScope)
            .Add<IWorkflowRunResourceOwner>(_frameScope);
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

        workflowStudioControl1.ConfirmDiscardChanges = () =>
            MessageBox.Show(
                this,
                "当前流程尚未保存，是否放弃修改？",
                "确认",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) == DialogResult.Yes;

        workflowStudioControl1.InteractionError += OnInteractionError;
    }

    /// <summary>按PluginId读取该Provider的私有配置；宿主只转交文本，不解释其中任何字段。</summary>
    private static string? ReadProviderConfiguration(string pluginId)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "vision-providers", pluginId + ".json");
        return File.Exists(path) ? File.ReadAllText(path) : null;
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
        _frameScope.Dispose();
        _workspace.Dispose();
    }
}
