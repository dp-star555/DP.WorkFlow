# WinForms 界面：主题、刷新节拍与新增页面

2026-11-23 起只维护 WinForms 界面，WPF 以后再适配（WPF 继续用原来固定的深色画布 `WorkflowCanvasPalette.Classic`）。

## ModernUI 与 DP.Foundation 共用一份

- `DP.WorkFlow.UI.WinForms` 引用 `ModernUI.WinForms` 包 1.1.1-foundation.23（`vendor/packages`，`NuGet.Config` 把 `ModernUI.*` 映射到这里），和 DP.Foundation 操作台是同一份。两边程序集同名，一个进程只能加载一份，所以 Workflow 页面才能放进操作台。
- 以 DP.Foundation 的 `vendor/src/ModernUI.WinForms` 为准。要改 ModernUI，先在 Foundation 改并打新包，再把新包复制到本仓库 `vendor/packages`，同时改 `Directory.Packages.props` 里的版本。
- 本仓库 `src/Platform/Desktop/ModernUI.WinForms` 源码现在只给 Gallery、示例和 ModernUI 自己的测试用，Workflow 界面不再引用它。

## 打包给 DP.Foundation

- DP.Foundation 的 Workflow 功能包引用本仓库的包，不引用源码：内核、持久化、标准 / 复合 / 工艺 / 运动节点、脚本、UI.Shared、UI.WinForms，加上 DP.Vision 的 DP.Plugins.Runtime，共 13 个。
- 版本在根目录 `Directory.Build.props` 的 `<Version>`（现在 1.0.0-preview.3）。改了这些库：版本加一，运行 `tools/Pack-ForFoundation.ps1`（打包并复制到同级 DP.Foundation\vendor\packages），再在 Foundation 改引用版本、vendor\README.md 的哈希，`dotnet restore --force-evaluate`。
- 脚本库的包 ID 改成 `DP.WorkFlow.ScriptEngine` / `DP.WorkFlow.ScriptEngine.WinForms`（程序集名不变）。后者带一个 buildTransitive 目标，间接引用时也把 Scintilla 原生库复制到程序目录。

## 主题

- `WorkflowThemePalette`（UI.Shared）：与 DP.Foundation 操作台 `ThemePalette` 同一组颜色角色和取值，内置 blue（默认）、night、mint、warm、orange 5 套。`Resolve` 兼容旧写法 Light / Dark。
- `WorkflowWinFormsTheme`：当前主题。`Use(palette)` 或 `Use("night")` 切换，`Changed` 通知已打开的界面。
  - `Modern`：由调色板推导出的 ModernUI 主题，推导方式与 Foundation 的 `OperatorShellStyle.Derive` 相同。
  - `Canvas`：画布配色 `WorkflowCanvasPalette.From(palette)`。
  - `Follow(root, reapply)`：着色并跟随主题，控件释放时自动取消。
- 挂进操作台后，由操作台在切换主题时调用 `WorkflowWinFormsTheme.Use(当前主题 Id)`。页面里不写死颜色。

## 统一刷新节拍（R4-b）

- `WorkflowUiRefresh.FramesPerSecond`：全局帧率，默认 10，可设 1～60，改动立即生效。
- `WorkflowRefreshClock.Subscribe(control, onFrame)`：WinForms 只有这一个界面线程定时器。
  - 每帧只调用看得见的控件，即控件可见、句柄已建、窗口没有最小化。
  - 没有订阅时定时器停止，控件释放时自动退订。
- 画布运行动画、运行监视、视觉帧预览、模板编辑都挂在这个时钟上。
- `WorkflowStudioRuntimeBinding` 的快照拉取间隔默认跟随全局帧率，单独设置 `FrameIntervalMilliseconds` 后就固定不再跟随。停止类状态照旧立即补一帧。

## 节点尺寸（R6-c 收尾）

- `WorkflowCanvasNode.Width/Height` 默认 0，表示未设定。设计器打开时按内容决定，未设定尺寸的节点保持左上角不动。
- 插件未加载、而且文档里没写尺寸的节点，用界面层默认值 `WorkflowDesignerGeometry.DefaultNodeWidth/Height`（180×60）。
- WF008 只检查负数尺寸。

## 节点提示（R1-01 界面）

- 设计器会话的 `Notices` 由运行绑定设置，取自宿主的提示板。节点窗口里的子画布也会拿到它。会话按本画布路径（`NoticePlanPath`）读取提示。
- 角标：节点右上角外沿的警告色圆点或胶囊，显示条数，超过 99 显示 99+。
  - 点开弹层可以看提示，并"确认并清除"或"查看全部提示"。
  - 角标和异常的红色、执行序号区分开。
- 画布级提示（无法归属到节点的，例如记录丢失）：画布右上角的浮层胶囊。有概览图时排在概览图下面。
- 底部"提示"页（`WorkflowNoticesControl`）：
  - 列出全部画布的提示，可以按位置确认，也可以全部清除。
  - 双击定位：子画布里的提示选中本画布上包含它的最外层复合节点。

## 公共数据清单页（R2-d 界面）

- 模型 `WorkflowPublicDataManifestModel`：把清单和节点使用合并成行。
  - 每行的状态是已登记、未登记、未使用或有错误之一，按根文档核对。
  - 可以新增、修改（改键时一次撤销）、删除，还可以"补齐未登记的"。
  - 修改都经过设计器会话，可以撤销。
- 底部"公共数据"页（`WorkflowPublicDataPage`）：
  - 选中一项，下方列出使用它的节点，双击定位。
  - 登记对话框里有保持和恢复时复位两个选项。

## 故障工作台（第 8 类界面）

- 模型 `WorkflowFaultWorkbenchModel(center, options)`，显示：
  - 当前会话：上报、受影响的流程，以及各停点的位置、原因、能否就地恢复、最后一步。
  - 最近结束的会话。
- 每个流程列出全部去向：就地恢复、回退到各入口再继续、结束本圈、停止。选回退再继续时先逆序回退，遇到不支持回退的节点弹窗请人处理好。是否允许由停点判断，不允许的写明原因。
- 传入创建流程管理时用的 `WorkflowRunCenterOptions`，模型就同时作为兜底界面：
  - 处理流程没处理完时，可以在工作台逐个流程选去向放行、重新运行处理流程，或全部停止。
  - 释放模型时还原原来的兜底。
  - 不传时只显示。
- 控件 `WorkflowFaultWorkbenchControl`：不可用的去向灰显。`DecisionRequested` 事件让宿主切到工作台。

## 页面基类与运行总览

- `WorkflowPage`：结构同 Foundation 的 `FeaturePage`，提供：
  - 同样的入口：`OnRefresh(canOperate, force)` 和 `ShowMessage`。
  - 卡片、网格、文字、表格工厂，以及 `RestoreSelection`。
  - 单独运行时按统一时钟每秒刷新；由外壳驱动时把 `AutoRefresh` 设为 false。
  - 挂进操作台时把基类换成 FeaturePage，或写一个薄适配器，页面代码不用改。
- `WorkflowRunCenterPage`：流程管理的运行总览，分三块：
  - 各流程的状态、恢复轮次、提示条数、记录健康，可以启动和停止。
  - 选中流程的提示。
  - 故障工作台。

## 运行监视页

- `WorkflowRunMonitorPage(center, catalog, documentOf)`：选一个流程，上面一排是状态、运行次数、圈数、节拍、界面同步，中间是只读画布，下面是 `WorkflowRuntimeMonitorControl`（令牌、并行域、子流程、事件、输出、节点耗时）。
- 画布 `WorkflowDesignerControl.ReadOnly`：只能平移、缩放、选中、看提示，不能拖动、连线、删除、粘贴、拖入节点，双击不打开节点编辑。
- 统计 `WorkflowRunStatistics`（UI.Shared）：只从界面拉到的快照推算。
  - 节拍：根画布有循环块时是主循环相邻两圈的间隔（一帧跳过多圈时平分），没有时是每次运行结束时的总耗时；取近 60 次平均。
  - 显示延迟：快照时刻到界面应用它的时刻。

## 运行日志页

`WorkflowRunLogPage`：选流程和某一次运行，把这次运行折叠成树。数据由 `WorkflowRunLogBook`（Runtime）收集，界面只读快照。

- 收集：`book.Attach(center)` 挂到每个流程的出口（保留出口原有回调）；流程管理要用 `WorkflowRunLogBook.OutletOptions()` 作为 `FlowOutlet`（调试级以上、不含数据流类别），否则并行派发 / 汇聚、提示会被默认级别滤掉。每个流程保留最近 20 次运行，每次最多 10 万行，只在内存里。
- 建树（`WorkflowRunLog`）：子流程按复合节点分层（事件的 `Parents`），循环块每圈一层，并行按分支令牌分层（`ParallelStarted` 的父令牌 → 分支令牌）；节点失败、提示、撤回、节点自己写的进度挂在那次执行下面；故障处理事件挂在最近出故障的那一层。失败和提示沿途标到每一层。
- 折叠（`WorkflowRunLogView`）：成功结束的子流程、循环圈、并行分支收起；连续成功的圈合并成一行“第 a–b 圈”；有失败、故障、提示的层和正在运行的层展开。手动展开 / 收起优先，“自动折叠”恢复。筛选“失败与故障”“提示”和搜索只留命中的行和它们所在的各层。“只看控制步骤”（默认开）隐藏节点自己写的进度。
- 右边是选中记录的详情：位置、节点、结果、开始 / 结束、耗时、令牌、画布、消息和数据，可以复制。
- 只记流程控制本身；不做良率、OK / NG 这类业务统计。

## 流程库（草稿 / 发布）

- `WorkflowFlowLibrary`（Persistence.Json）：一个目录的流程文件、草稿和发布历史，只按 JSON 文本操作，不需要节点目录。
  - 已发布的文件在目录下，流程管理只加载这些；草稿在 `.drafts\名字.json`，不影响正在跑的流程，重启也还在。
  - 设计页打开的是 `EnsureDraft` 给的草稿（没有时从已发布的复制一份），保存只写草稿。`HasDraftChanges` 按内容比较（格式差别不算）。
  - `Publish`：草稿写成正式文件（流程名改成库里的名字），记一版（说明默认“发布”），删掉草稿。检查草稿、确认没有流程在运行由调用方负责。
  - `DiscardDraft` 回到已发布的版本；从没发布过的流程放弃草稿就没了。
  - 新建（`CreateDraft`）、复制、导入、恢复（`Restore` 把版本写成草稿）都先落成草稿，发布后才加载。
  - 文件名就是流程名：复制、改名、导入时把文件里的流程名一起改掉；改名时草稿和历史跟着走。
  - 历史版本在 `.history\名字\`：`v0001.json`… 和 `versions.json`（时间、发布人、说明）。发布和删除前各记一版，内容没变时不重复记。
  - `Compare` 比根画布：新增、删掉、参数变了、只挪了位置的节点，新增和删掉的连线，流程名、入口、公共数据清单。`CompareDraft` 比草稿和已发布的。
- `WorkflowDraftPublisher`（UI.Shared）：发布 / 放弃草稿的共同流程。`BlockedReason`（例如有流程在运行）、`CheckDraft`（宿主编译 + 设备检查）、`Gate`（准入，操作键 `workflow.design.publish` / `workflow.design.discard`）、`CurrentUser`。
- `WorkflowPublishDialog`（UI.WinForms）：先检查，不过就列原因；过了显示这次改了什么，填发布说明，再发布。流程库页和设计页共用。
- `WorkflowFlowLibraryPage`（UI.WinForms）：表格（多一列“草稿”：未发布 / 有修改）、搜索、显示已删除；查看修改、发布、放弃草稿，新建（交给宿主）、复制、改名、删除、导入、导出、历史版本对话框（比较、恢复成草稿）。状态文字由宿主给，发布器在 `Publisher` 上配置。
- `WorkflowDocumentWorkspace.Saved`：写到文件后发生（打开文件、放弃修改不发）。

## 插件节点的专用编辑器

`WorkflowStudioControl.LoadNodeEditorPlugins(插件目录, 加载器)` 装插件里的 Studio / WinForms 扩展。有 plugin.json 的按分组装；直接放进插件目录的 dll（没有 plugin.json）按类型自动发现，和运行节点一样。宿主要把装运行节点的同一个 `WorkflowPluginLoader` 传进来，否则 net8 下节点类型会被装成两份。

插件目录里的 dll 用哪一份：程序目录（`WorkflowPluginLoader.HostDirectory`，默认程序所在目录）里有同名的，一律用程序目录那份，插件目录里的副本不加载也不扫描；插件目录只补程序目录没有的。节点工程因此可以把编译输出整个放进来，不用关心基座装了哪些功能包。

## 节点自己提供候选与设备检查

- 下拉候选：属性标 `[WorkflowPropertyEditor(WorkflowPropertyEditorKeys.NodeChoice)]`，节点实现 `IWorkflowPropertyChoices.GetPropertyChoices(属性名, 上下文)`。上下文里有同文档节点和宿主交给流程的服务表（`Services`），节点自己从服务表取需要的服务并返回候选。没有候选时退回文本输入。基座不需要为新节点登记任何东西。
- 宿主把服务表交给设计器：`WorkflowStudioControl.Properties.Services = 服务表`（属性面板和节点工作台都会用到）。
- `IWorkflowNodeEnvironmentValidator`：节点按宿主服务检查自己固定引用的外部对象；`WorkflowEnvironmentCheck.Check` 走整份流程（含子画布）。
- 设备类节点（运动、IO、气缸……）不在本仓库：由宿主侧的节点工程直接使用宿主服务实现，dll 放进插件目录即进工具箱。

## 操作准入（审计）

- `IWorkflowOperationGate`（UI.Shared）：宿主对界面操作的准入与记录，默认 `WorkflowOperationGates.Direct` 直接执行。
- `WorkflowOperation(Kind, Target, Detail)`，`Key` 是稳定的操作键，例如 `workflow.flow.start`、`workflow.fault.release`、`workflow.design.run`。
- 经过它的操作：
  - 运行总览：单独启动 / 停止、全部启动 / 停止（`WorkflowRunCenterPage.Gate`）。
  - 故障工作台：放行、重新运行处理流程、全部停止（`WorkflowFaultWorkbenchControl.Gate`，另有 `CanOperate`）。
  - 设计器运行：开始（整个流程、当前子流程、单节点）和停止（`WorkflowStudioRuntimeBinding.Gate`）。
- 只包住“发出命令”，不等流程跑完。
- `WorkflowStudioRuntimeBinding.RunBlockedReason`：不能开始运行的原因，运行按钮灰显并在提示里写明。

## 未做（等阶段 G 或后续确认）

- 运动步骤、Tag 选择编辑器（阶段 G）。
- 报警、日志、权限、设置使用 Foundation 基座页面，不在 Workflow 里重做。
- WPF 适配。
