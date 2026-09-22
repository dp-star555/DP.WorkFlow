# 新版视觉链路使用说明

## 直接运行示例

- WinForms：`dotnet run --project samples/DP.WorkFlow.WinForms.Sample/WinFormsApp_test.csproj`
- WPF：`dotnet run --project samples/Legacy/WpfApptest/WpfApptest.csproj`

在DP.WorkFlow目录执行。默认流程为Start→文件→预处理→Region→形态学→Blob→筛选→掩码颜色；随输出复制的 `VisionData/demo.pgm` 有两个亮连通域。运行后双击节点打开“图像与测量范围”页面；选择输入/结果/模板。示例不依赖模型或相机，未安装HALCON不妨碍文件/算法路径。

新增8个算子的参数、精度/预算边界及配方见[算子使用说明](vision-operators.md)。下表保留基础十节点的契约；完整Module共18节点。

## 类型与节点

客户统一使用 `IImageSource`；`VisionImage.CopyFrom(info, pixels)` 复制外部紧密像素，`Retain()` 创建独立租约。`ImageFrame` 将像素与内容身份绑定；像素改变必须换FrameId。内部ImageBuffer不可作为外部配置/接口类型。

| NodeType | 输入 | 标准输出 |
|---|---|---|
| Vision.LoadFile | FilePath | ImageFrame |
| Vision.LoadFolder | FolderPath、Extensions、Loop | ImageFrame |
| Vision.CaptureFrame | CameraId、曝光/增益/触发 | ImageFrame |
| Vision.AnalyzeBlobs | Frame绑定、范围、阈值、面积/连接性 | BlobAnalysisResult |
| Vision.AnalyzeColor | Frame绑定、范围 | ColorAnalysisResult |
| Vision.MeasureEdges | Frame绑定、矩形、Canny阈值、线/圆模型 | EdgeMeasurementResult |
| Vision.LocateTemplate | Frame与Template绑定、搜索矩形、最小分数 | TemplateLocationResult |
| Vision.SolveCalibration | 对应点及可选目标旋转轨迹 | AffineCalibration |
| Vision.MapCoordinate | Calibration绑定、X/Y绑定、RotationRadians | Coordinate2D |
| Vision.MeasureDistance | X1/Y1/X2/Y2常量或绑定 | VisionDistanceResult |

控制流和数据来源独立。File→Blobs→Color中Color仍应绑定File的原图根输出，而非Blobs的结果：

```csharp
var input = WorkflowInput<ImageFrame>.FromBinding(new WorkflowBindingKey(fileNodeId, "$"));
var blob = new AnalyzeVisionBlobsNodeModel { Frame = input, MinimumGray = 0, MaximumGray = 127 };
var color = new AnalyzeVisionColorNodeModel { Frame = input };
```

可通过成员路径绑定例如Blob数量、测量坐标到普通比较/数值节点。执行Success不等于产品合格；无Blob或定位未找到是正常完成，边缘不足无法拟合则明确失败。没有自动写VisionImage等旧变量。

## 宿主装配

节点只引用中立契约；Windows宿主引用DP.Vision.OpenCv，实现与UI不反向引用Workflow。

```csharp
using DP.Vision;
using DP.Vision.Algorithms;
using DP.Vision.OpenCv;
using DP.WorkFlow;

var nodes = new WorkflowNodeCatalog();
var handlers = new WorkflowNodeHandlerCatalog();
new WorkflowRuntimePluginCatalog(nodes, handlers)
    .Register(new WorkflowImageRuntimePluginModule()).Freeze();
var reader = new OpenCvImageFileReader();
var acquisition = new WorkflowVisionAcquisitionSession(reader);
using var frames = new WorkflowVisionFrameScope(acquisition);
var services = new WorkflowServiceProvider()
    .Add<IImageFileReader>(reader)
    .Add<IWorkflowVisionFolderSource>(acquisition)
    .Add<IBlobAnalyzer>(new OpenCvBlobAnalyzer())
    .Add<IColorAnalyzer>(new RgbColorAnalyzer())
    .Add<IEdgeMeasurer>(new OpenCvEdgeMeasurer())
    .Add<ITemplateLocator>(new OpenCvTemplateLocator())
    .Add<IImagePreprocessor>(new OpenCvImagePreprocessor())
    .Add<IRegionProcessor>(new OpenCvRegionProcessor())
    .Add<IBlobSelector>(new BlobSelector())
    .Add<ICaliperMeasurer>(new CaliperMeasurer())
    .Add<IRobustLineFitter>(new RobustLineFitter())
    .Add<ITemplatePoseLocator>(new OpenCvTemplatePoseLocator())
    .Add<IWorkflowVisionFrameScope>(frames)
    .Add<IWorkflowRunPreparationService>(frames);
using var host = new WorkflowRuntimeHost(nodes, handlers);
host.Configure(document, new WorkflowContext(services));
var result = await host.RunAsync();
// 消费host.Engine.RunState.NodeOutputs；准备下一次运行前UI须Retain所需图像。
```

宿主不得在运行中更换能力实例。当前使用固定服务装配，不提供Profile候选选择或执行异常后换后端。

真实相机不由 Workflow 侧注册实现类，而是由 `DP.Vision.Acquisition.Runtime` 扫描插件目录发现 Provider 插件：`DP.Vision.Halcon` 与 `DP.Vision.Basler` 各自发布 `plugin.json`，工作流文档只保存逻辑 `SourceId`，机器配置负责把 `SourceId` 绑到 `ProviderId` + `ProviderBindingId` + `ResourceKey`。采集节点只声明中立入口 `IVisionAcquisition`，不感知厂商。缺 SDK/运行时的机器在首节点之前就失败（HALCON 是编译期 `HalconStreamCameras.IsSdkEnabled`，Basler 是原生运行时健康探测），离线设备、驱动或许可证错误在实际打开时失败。设备跨布防复用同一句柄，主动单次采集与外部回调缓冲源共用它，只在释放时关闭；曝光/触发、取消及SDK部署边界见[HALCON说明](../../../DP.Vision/src/DP.Vision.Halcon/README.md)。旧 `ICameraCapture`/`HalconCameraCapture` 路径已删除。

平台Studio扩展：

```csharp
new VisionWinFormsStudioExtension { FrameSource = frames, FileReader = reader };
new VisionWpfStudioExtension { FrameSource = frames, FileReader = reader };
```

通过各自宿主的NodeEditorExtensions.Register注册。节点程序集现在只有一个Runtime Module；Manifest扫描不会发现旧Module。旧节点/注册扩展/旧页面均已删除。未知旧NodeType仅作为通用未知节点保留原始文档信息，运行绑定阶段拒绝执行，不自动映射新版语义。

## 运行准备与生命周期

- RuntimeHost检查根/子计划能力后调用准备服务。文件夹在此冻结按Ordinal排序的清单，每次运行重置游标；失败读取不推进；默认末尾失败，Loop显式循环。
- 文件、参数和范围检查在编译/准备或实际图像可用时执行。损坏文件/设备离线仍可能在获取阶段失败，不能保证外部资源在预检后不变。
- 文件读取器/相机返回的IImageSource由调用者释放；节点把独立ImageFrame租约交给必需的帧仓。
- `frame.Image`是借用句柄，禁止消费者直接Dispose；跨窗口或重跑持有使用 `frame.Retain()`。
- 帧仓默认每轮512MiB/1024帧。超过预算失败，而非淘汰仍能被下游绑定的标准输出；它不是无限连续视频录制仓。
- 每次新运行释放仓内上一轮租约；独立UI租约仍有效。正常宿主关闭先禁用Run、`await host.StopAsync()`，再释放帧仓与设备，避免在途采集使用已释放资源。
- 预览图像/事实同帧、按节点最新值；数据输出历史与预览槽分开。跨子文档重复预览NodeId在准备阶段拒绝，以免取错节点图像。

## ROI与算法边界

Blob/颜色配置可保存旋转矩形、椭圆、多边形及Include/Exclude。包含并集减排除并集；仅有排除区域时以全图为基底。全部禁用明确拒绝，不默认为全图。后台组合为精确Region，孔洞和不连通部分不会丢失。

形状使用原图像素边界坐标，正角为顺时针弧度。开放线、圆弧不能自动当面积范围。越界不裁剪。组合限制1600万像素及200万游程；超过明确拒绝。线/圆测量、模板匹配只接受轴对齐矩形，不把任意Region的外接框当等价输入。

编辑只操作EditingNode：打开/导航/手动文件预览不改配置，ROI提交手势更新副本，节点确认单次提交，取消丢弃。结果可点击查看面积/质心/RMS；模板分数不是概率。WPF使用原生画布；Tab切换只停刷新，页面关闭才释放。

文件保留Gray8/BGR/BGRA/Gray16；多页文件仅读首张。默认编码文件限制64MiB，不是原生解码峰值内存限制，只读取可信工业文件。8位分析拒绝Gray16隐式降位深。文件夹扩展名可配置（示例PGM须加入`.pgm`）。

Blob：包含灰度闭区间、4/8连通、最小面积，质心按像素中心计算，不做隐式形态学。

颜色：RGB编码均值，支持Gray8/RGB/BGR及Alpha布局；Gray8复制三通道，Alpha不加权，不做白平衡/线性化/色差。

基础MeasureEdges仍为Canny像素边缘及线/圆拟合，不等于亚像素或鲁棒估计。新增MeasureCaliper使用真实梯度峰插值，FitRobustLine使用RANSAC＋正交重拟合；它们是不同明确契约。

基础LocateTemplate保持固定旋转/尺度的平移匹配。新增LocateTemplatePose支持有界离散旋转/尺度候选及坐标正反映射，不宣称连续角度优化、多实例或学习式形状模型。正常空检出保留Completed。

## 验证入口

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Test-DPWorkFlow.ps1
dotnet build DP.WorkFlow.sln -c Release --no-restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ../DP.Vision/verify.ps1
```

测试覆盖真实文件、线/圆/模板、ROI掩码、JSON绑定、Module扫描、原生Renderer与编辑事务、实际示例重跑、帧预算/释放、HALCON真实像素复制边界和安全停止。硬件触发/现场性能与真实WPF物理输入不由这些自动测试代替。
