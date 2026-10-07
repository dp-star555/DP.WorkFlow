# 标签检测业务节点

## 已实现的首版

工具箱 **业务检测 → 标签检测**，节点类型 `LabelInspection.Inspect`。一个节点执行一张显式绑定的 `DP.Vision.ImageFrame`，直接调用 DP.LabelInspection 的无界面 `InspectionEngine`；不拆散、复制其逐 ROI 检测与判定规则。

- 输入：`Frame`（必需绑定）、可选 `CycleId`（固定值或绑定）、`TaskData`（绑定或空）、可选 `LabelCoordinates`（标签坐标系，见下文“标签定位”）。周期不默认等同流程 RunId；任务数据的周期/有效期仍由 SDK 检查。
- 配方：节点内 `RecipeJson`，使用 SDK 原生格式，保留检测项目、字段约束、异常设置及库 ID/Revision。不将运行图像、TaskData、引擎或报告写入节点配置。
- 输出：`WorkflowLabelInspectionResult`，包含输入 `FrameId`、`CycleId`、配方名称/内容摘要、资源快照摘要及完整 `InspectionReport`。`Verdict`、`IsQualified`、`Regions`、`ElapsedMilliseconds` 是 SDK 报告的转发，不自行推断“全部必检已完成”。下游可以绑定 `Verdict` 或 `Report.Verdict`。
- 默认有效报告无论 Ok/Ng/Review 都经 **Success** 继续；Success 仅表示节点返回了报告。报告内的阻断/阶段失败仍完整保留；外层决定是否转人工。绑定、输入尺寸/布局、资源加载失败或无法返回报告才是节点故障。取消不发布报告。
- 开启 **“NG走失败出口”** 后，判定不是 Ok（Ng 或 Review）时报告照常提交，并沿 **Failed** 出口继续，失败支路可直接绑定本节点的 `Verdict`/`Report`。“失败”出口默认不显示，需在“输出端口”中启用并连线；未连线时本路径到此结束（运行仍正常完成）。节点故障在“失败”出口已连线时同样走这条支路，但没有报告输出。

## 使用

1. 上游放置文件/相机图像节点，将其 `ImageFrame` 绑定到标签节点的“输入图像”。生产图像不取自配置页样张。
2. 配置“资源根目录”。相对路径基于当前流程文件目录；未保存的流程基于宿主目录。其它资源路径必须位于显式根目录内，可以使用根内绝对路径或相对路径。不回退到开发机资源目录，不自动联网下载。
3. “字库/模型库目录”是 SDK 数据根目录，包含原来的 `libraries/`、`anomaly-libraries/` 等结构；不要只复制 recipe.json 就认为完成部署。必须带上配方使用的准确库 ID/Revision 和模型文件。
4. 新建节点还没有配方时不阻止流程编译运行：先运行一次流程，上游采图/定位照常执行并产生预览，执行到标签节点时报“尚未配置配方”（不准备资源，可走“失败”出口）。然后打开节点的“标签配置与试检测”页，导入原生配方，或加载配置样张/本轮上游预览后创建 ROI、项目与约束。没有图像时页面也会先接上字库/异常库管理，并在状态栏给出下一步。模板模式填写“参考图”；自由模式不加载残留参考路径。纯空白检查不强制安装 OCR 模型。
5. 编辑后的完整配方在“应用/确定”前捕获到隔离副本，保持原配方名称，随后一次性提交，可整体撤销；取消不回写。JSON 可在没有预览时导入并保存。
6. 修改资源参数或外部参考后，用配置页“重载资源”重新装配试检测引擎。生产运行在下一次准备时自动读取当前资源，不要求逐个重新确认制作签名。
7. 正式运行后查看“标签检测报告”页：同帧原图、分组证据、实际 OCR/读码文本与阶段状态。试检测不进入正式输出或此报告页；输出失效会撤销视图。

配置工作台中的“发布字库/异常库修订”、导出文件是明确的外部写入，不随节点取消/撤销回滚。新发布的库修订也不隐式替换配方原有固定修订。

## 标签定位（复用流程定位，ROI 随动）

定位不在标签节点内重做，而是复用流程已有节点：

```
图像获取 → 模板定位（旋转/尺度） → 构建本帧坐标系 → 标签检测（标签坐标系 = 坐标系节点.CoordinateSystem）
```

- 绑定“标签坐标系”后，配方坐标是**标签坐标**：配方像素 (x, y) → 局部坐标 (标签原点X + x·配方像素尺寸, 标签原点Y + y·配方像素尺寸) → 本帧原图（坐标系的局部→原图矩阵）。节点把组合后的仿射作为放置（`InspectionPlacement`）交给 SDK。
- SDK 只对每个 ROI（外扩 16 像素）按放置从原图取样，不变换整张图；平移、旋转、缩放都能跟随。整数像素平移直接取像素，不插值。ROI 放置后超出原图时节点故障（可走“失败”出口）。
- 本帧没有定位结果（例如模板未找到）时节点故障，不沿用其它帧的坐标系。
- 未绑定时保持原行为：配方坐标即原图坐标，输入图须与配方同尺寸（错误信息会给出两者尺寸）。
- 配置：先运行一次采图与定位，在配置页点“载入上游预览”——用同一轮的坐标系把标签区域摆正为配方尺寸（已有配方以配方尺寸为准，新建时用“新建配方宽度/高度”，为 0 时用预览图尺寸），在这张图上画的 ROI 就是标签坐标。“配置页摆正预览”要求“标签坐标系”直接绑定定位节点的 `CoordinateSystem` 成员。
- 模板模式的参考图必须是标签坐标下的图：在摆正后的预览上点“保存为参考图”，保存为资源根目录下 `label-reference-<节点ID>.png` 并填入“参考图”。
- 报告坐标为标签坐标；报告页按本次放置把 ROI 框、证据和字块换算到原图（随标签旋转），`WorkflowLabelInspectionResult.Placement` 供下游同样换算。
- 放置时推荐“假定已对齐”；“平移配准”只用于边距范围内的残余偏移。双线性重采样对逐像素差异/清晰度类指标有轻微影响，建议缩放接近 1。
- SDK 侧契约见 DP.LabelInspection 的 `docs/node-packaging/04-placement.md`。

## 界面

- WinForms 配置页和报告页使用 ModernUI 深色主题（按钮、证据树、分隔条），与工作台其它页面一致；内嵌的 SDK 标签工作台保持其自身外观。
- 报告页画布叠加本次所用配方的 ROI 框（`WorkflowLabelInspectionResult.RecipeRegions`），再叠加证据与字块，便于对照 NG 位置。
- “加载配置样张”可选资源根目录外的图片：自动复制到根目录下 `samples/`（同名同内容复用，内容不同追加序号，不覆盖），配置样张路径记录为相对根目录的路径，属性页同步刷新。
- WPF：`DP.WorkFlow.LabelInspection.UI.Wpf` 的 `LabelInspectionWpfExtension` 提供“标签配置”页（导入/导出原生配方JSON、显示配方尺寸/模式/ROI 列表，确认时提交、取消不回写）和只读“标签检测报告”页（判定、ROI 结果、实际 OCR/读码文本与证据）。ROI 图上编辑与试检测仍需 WinForms 配置页，或导入在其它工作台导出的配方。WPF 示例已注册节点模块、运行能力和页面。

## 资源与生命周期

`WorkflowLabelInspectionRuntime` 实现事务式准备：递归准备根/子计划，捕获配方、参考图、当前 ONNX 文件及活动库修订。ONNX 复制为临时文件快照后加载；字库/异常库以不可变仓传给引擎。准备失败/取消释放全部候选；提交后按绑定作用域和计划路径读取。

**多轮复用**：每次准备先计算配置指纹（节点资源配置、解析后的根目录、模型和参考图文件的大小与修改时间，不读文件内容）。与上一轮已提交的同一计划位置指纹相同时直接复用已加载的模型和引擎，不再每轮读取、复制和加载 ONNX；子流程或循环内的标签节点同样复用。配方、路径或这些文件有任何变化时下一轮重新加载，旧资源在所有在途调用结束后释放。字库/异常库按配方中的库ID与修订引用（已发布修订不可变），不计入指纹。`ResourceLoadCount`、`CachedResourceCount` 用于诊断实际加载次数。临时模型副本删除失败只记录警告，不影响运行收尾。

同一节点的引擎在一轮内复用，排队调用串行，内部 ROI 按“最大并行数”执行。请求在异步等待前 Retain 实际图与参考图；退役等待所有在途/排队调用退出后释放模型。关闭配置窗口会先异步取消并等待试检测，再释放控件和页面引擎，避免在 UI 线程同步等待造成死锁。

配方最多 1MB、128 个 ROI；输入 Gray8/Bgr24，沿用 SDK 的 16M 像素限制。模型文件各最多 256MB，参考/样张编码文件最多 64MB。帧/预览使用原有 Workflow 图像仓预算。完整报告可能包含字块和差异像素；首版没有额外的永久报告历史仓，长循环需由宿主明确输出保留预算。

预览目前沿用仅 NodeId 的帧仓，标签节点 ID 不能跨子计划重复；准备阶段明确拒绝，不混合不同节点的图像。执行资源本身按计划路径隔离。

## 宿主装配

工程与内核隔离：

- `Nodes/DP.WorkFlow.Nodes.LabelInspection`（net8.0）：模型、报告包装、Handler、Runtime Module；无 UI/原生实现依赖。
- `Hosting/DP.WorkFlow.LabelInspection.Runtime`（net8.0-windows）：SDK 资源、引擎和运行租约。
- `Studio/DP.WorkFlow.LabelInspection.UI`（net8.0）：配方捕获/提交桥和页面模型。
- `Studio/DP.WorkFlow.LabelInspection.UI.WinForms`（net8.0-windows）：原生工作台及只读报告 Renderer。

先注册 `WorkflowLabelInspectionModule`，再 Freeze 目录。宿主注册 `IWorkflowLabelInspectionService` 和事务式运行准备链，例如：

```csharp
var labels = new WorkflowLabelInspectionRuntime(WorkflowDirectory, frameScope);
var algorithms = new WorkflowVisionAlgorithmBindings(algorithmRuntime, labels, Resources);
services.Add<IWorkflowLabelInspectionService>(labels)
    .Add<IWorkflowRunPreparationService>(algorithms);
// 原有算法绑定/能力提供器及帧仓/根运行所有者注册保持不变。
```

桌面通过 `LabelInspectionWinFormsExtension { BaseDirectory = WorkflowDirectory, FrameSource = frameScope }` 注册页面。停机先等待运行结束，再 `await labels.DisposeAsync()`；不在后台宿主加载 WinForms 程序集。

当前 WinForms 示例已直接注册节点模块、运行能力和页面，不需要额外安装 plugin.json。可移植节点程序集也导出独立 Runtime Module，但单独复制节点 DLL 不会自动创建宿主能力；外部插件部署应完整提供契约依赖，并让宿主/加载会话共享标签节点及报告契约程序集。

## 合成演示与验证

```powershell
dotnet run --project samples/DP.WorkFlow.WinForms.Sample/WinFormsApp_test.csproj -- --label-demo
```

演示在输出目录 `LabelDemo/` 生成 `clean.png` 和 `ink.png`，默认运行真实空白检查。将读取节点文件改为 `ink.png` 后应得到 NG，流程仍正常完成。没有生产数据、OCR 模型或工业精度承诺。

回归入口：`tests/Workflow/DP.WorkFlow.Nodes.LabelInspection.Tests`。覆盖真实 SDK OK/NG、下游完整报告字段绑定、资源更新快照、前置错误、取消、配方 JSON、隔离编辑与撤销、原生工作台捕获、关闭正在试检测的窗口及预览失效。

本次独立输出验证：节点/工作台套件21项通过，现有节点编辑/属性面板回归11项通过，SDK相关筛选在net48/net8.0-windows各24项通过（共80次执行）；WinForms启动示例构建0警告/0错误，`--label-demo`主窗口启动及正常关闭冒烟通过。未执行整个解决方案/全部SDK套件或现场精度验收。SDK测试工程现有MSTEST0032分析器警告仍保留。

常规输出构建曾被现有进程占用几何节点PDB阻挡；未强制结束调试。使用常规启动项目之前，请结束调试并重新生成，不将局部更新或旧EXE当作首版运行证据。

## 明确未包含

- WPF 原生标签工作台（ROI 图上编辑与试检测）；没有使用 WindowsFormsHost 冒充原生 WPF 支持。WPF 只提供配方导入/导出与只读报告，见下文。
- 透视（非仿射）标签配准、任意 Region 形状的标签 ROI。
- 用通用 ROI 编辑器替代 SDK 工作台的 ROI 绘制（当前仍在 SDK 工作台中、于摆正后的标签图上绘制）。
- 一键可部署资源包导入/导出、正式报告自动保存/永久历史和完整字块/差异多视图浏览器。
- 新算法、HALCON 标签后端、ISO 评级、现场相机与生产精度验收。

业务检测继续以现有 SDK 能力和实际资源为准；配置页之外的 Demo 专属工具不自动成为节点功能。
