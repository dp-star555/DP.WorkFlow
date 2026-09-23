using DP.Vision.Acquisition;
using DP.Vision.Algorithms;
using DP.Vision.OpenCv;
using DP.WorkFlow.UI;
using DP.WorkFlow.Vision.UI;

namespace DP.WorkFlow.Samples;

/// <summary>
/// 两套桌面示例共用的视觉宿主装配：文件读取、帧作用域、按插件目录组合的相机采集运行时、
/// 带启动诊断的逻辑图像源目录，以及视觉算法服务注册。
/// </summary>
public sealed class SampleVisionHost
{
    // V2 机器配置：插件自动发现 + 版本化CameraDefinition。工作流文档只保存SourceId，
    // 换机器时只改这里，不需要改流程文档，也不需要重新编译节点。
    // 宿主只按插件目录自动发现Driver Module，编译期不选择任何具体Provider。
    // 公共层只解释sourceId/acquisitionType/connection等字段；deviceSettings由对应Plugin解析，
    // 生成内部绑定、规范资源键与进入CompositionId的私有配置摘要。
    private const string MachineConfigurationJson = """
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

    /// <summary>从 <c>plugins</c> 目录组合采集运行时并同步启动设备连接。</summary>
    public SampleVisionHost()
    {
        FileReader = new OpenCvImageFileReader();
        FolderSource = new WorkflowVisionAcquisitionSession(FileReader);
        FrameScope = new WorkflowVisionFrameScope(FolderSource);
        var providerPluginDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "plugins");
        var driverModules = new VisionAcquisitionDriverModuleLoader().Load(providerPluginDirectory);
        var typeCatalog = new VisionAcquisitionTypeCatalogComposer().Compose(driverModules.Modules);
        var cameras = VisionAcquisitionMachineConfigurationParser.Parse(MachineConfigurationJson);
        var composition = new VisionAcquisitionMachineConfigurationComposer()
            .Compose(typeCatalog, cameras);
        Acquisition = new VisionAcquisitionRuntime(composition);
        // V2-3：设备连接属于软件生命周期，宿主在进入可运行状态前启动Runtime：
        // 按ResourceKey真正打开设备；Required失败→NotReady，Optional失败→Degraded。
        var runtimeState = Acquisition.StartAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
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
        Sources = startupDiagnostic.Length == 0
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
    }

    /// <summary>图像文件读取器，同时供节点详情页预览使用。</summary>
    public OpenCvImageFileReader FileReader { get; }

    /// <summary>文件夹图像源。</summary>
    public WorkflowVisionAcquisitionSession FolderSource { get; }

    /// <summary>每轮运行的图像帧作用域。</summary>
    public WorkflowVisionFrameScope FrameScope { get; }

    /// <summary>相机采集运行时；关闭窗口时必须异步释放。</summary>
    public VisionAcquisitionRuntime Acquisition { get; }

    /// <summary>附带启动诊断的逻辑图像源目录。</summary>
    public WorkflowVisionSourceCatalog Sources { get; }

    /// <summary>注册视觉算法、图像源与运行作用域服务。</summary>
    public WorkflowServiceProvider AddServices(WorkflowServiceProvider services) => services
        .Add<IImageFileReader>(FileReader)
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
        .Add<IWorkflowVisionFrameScope>(FrameScope)
        .Add<IWorkflowVisionFolderSource>(FolderSource)
        .Add<IVisionAcquisition>(Acquisition)
        .Add<IWorkflowVisionSourceCatalog>(Sources)
        // 准备服务只做校验；退役上一轮资源是运行所有者的职责，只有根运行宿主持有它（AR-01 阶段2）。
        .Add<IWorkflowRunPreparationService>(FrameScope)
        .Add<IWorkflowRunResourceOwner>(FrameScope)
        // 本轮作用域取得：外部回调缓冲源要在采集节点之前布防。桥接是 Kernel 与采集侧之间唯一的连接点，
        // 嵌套调用点不解析 IWorkflowRunScopeOwner，因此结构上无法重新布防或清空父运行队列。
        .Add<IWorkflowRunScopeOwner>(new VisionAcquisitionRunScope(Acquisition));

    /// <summary>在根会话中声明示例流程显式发布的视觉公共数据。</summary>
    public static void RegisterPublicData(WorkflowDesignerSession session) =>
        session.PublicDataCatalog
            .Register<DP.Vision.ImageFrame>("VisionFrame", "显式发布的图像帧", "视觉数据")
            .Register<BlobAnalysisResult>("VisionBlobs", "显式发布的连通域事实", "视觉数据")
            .Register<ColorAnalysisResult>("VisionColor", "显式发布的颜色事实", "视觉数据");
}
