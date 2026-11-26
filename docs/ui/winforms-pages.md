# WinForms 界面：主题、刷新节拍与新增页面

2026-11-23 起只维护 WinForms 界面，WPF 以后再适配（WPF 继续用原来固定的深色画布 `WorkflowCanvasPalette.Classic`）。

## ModernUI 与 DP.Foundation 共用一份

- `DP.WorkFlow.UI.WinForms` 引用 `ModernUI.WinForms` 包 1.1.1-foundation.28（`vendor/packages`，`NuGet.Config` 把 `ModernUI.*` 映射到这里），和 DP.Foundation 操作台是同一份。两边程序集同名，一个进程只能加载一份，所以 Workflow 页面才能放进操作台。
- 以 DP.Foundation 的 `vendor/src/ModernUI.WinForms` 为准。要改 ModernUI，先在 Foundation 改并打新包，再把新包复制到本仓库 `vendor/packages`，同时改 `Directory.Packages.props` 里的版本。
- 本仓库 `src/Platform/Desktop/ModernUI.WinForms` 源码现在只给 Gallery、示例和 ModernUI 自己的测试用，Workflow 界面不再引用它。

## 打包给 DP.Foundation

- DP.Foundation 的 Workflow 功能包引用本仓库的包，不引用源码：内核、持久化、标准 / 复合 / 工艺 / 运动节点、脚本、UI.Shared、UI.WinForms，加上 DP.Vision 的 DP.Plugins.Runtime，共 13 个。
- 版本在根目录 `Directory.Build.props` 的 `<Version>`（现在 1.0.0-preview.11）。改了这些库：版本加一，运行 `tools/Pack-ForFoundation.ps1`（打包并复制到同级 DP.Foundation\vendor\packages），再在 Foundation 改引用版本、vendor\README.md 的哈希，`dotnet restore --force-evaluate`。
- 脚本库的包 ID 改成 `DP.WorkFlow.ScriptEngine` / `DP.WorkFlow.ScriptEngine.WinForms`（程序集名不变）。后者带一个 buildTransitive 目标，间接引用时也把 Scintilla 原生库复制到程序目录。

## 主题

- `WorkflowThemePalette`（UI.Shared）：与 DP.Foundation 操作台 `ThemePalette` 同一组颜色角色和取值，内置 blue（默认）、night、mint、warm、orange 5 套。`Resolve` 兼容旧写法 Light / Dark。
- `WorkflowWinFormsTheme`：当前主题。`Use(palette)` 或 `Use("night")` 切换，`Changed` 通知已打开的界面。
  - `Modern`：由调色板推导出的 ModernUI 主题，推导方式与 Foundation 的 `OperatorShellStyle.Derive` 相同。
  - `Canvas`：画布配色 `WorkflowCanvasPalette.From(palette)`。
  - `Follow(root, reapply)`：着色并跟随主题，控件释放时自动取消。
- 挂进操作台后，由操作台在切换主题时调用 `WorkflowWinFormsTheme.Use(当前主题 Id)`。页面里不写死颜色。

## 设计页布局（按设计稿“流程设计”画板）

- `WorkflowCanvasWorkspaceControl` 排成卡片（`WorkflowCard`：表面色底、细线描边、主题圆角；卡片里的宿主控件按表面色着色）：
  - 上面命令卡：左边工具条（宿主条目如“文件”、保存，撤销、重做，当前路径和“已修改”），右边适合画布、自动布局、对齐 ▾、宿主按钮（`AddCommand`，如“发布”）、公共数据、校验（带问题数，有错误时红色）、调试运行；运行中换成暂停 / 继续 / 停止。
  - 中间：工具箱卡（220，标题“工具箱 · 拖到画布”、搜索、分类树，可以拖到画布，也可以双击放到画布中间）| 画布卡（点状网格）| 检查器卡（340，`Inspector`）。
  - 检查器默认隐藏：点命令卡上的“节点属性”浮在画布右侧，鼠标移开且焦点不在里面时自动收起；标题行的“固定”让它占一栏，“取消固定”后回到自动收起（`InspectorPinned`）。
  - 工具箱拖动自己判断：`ModernTreeView` 自己处理鼠标按下，原生的 `ItemDrag` 不会触发，所以按下记住节点，移动超过系统拖动阈值就开始拖放。
  - 没有底部页签，画布占满中间：
    - 诊断（`WorkflowCanvasDiagnostics`）编辑时持续检查，直接画在画布上：有错误的节点边框标红，左上角外沿一个红色“!”角标（只有警告时为警告色）；悬停节点或角标显示原因。不属于本画布节点的问题（缺入口、宿主检查失败等）显示在画布左上角“流程错误 n”胶囊里，悬停看原因。运行中、暂停的节点仍按运行状态着色。
    - 节点提示和运行页一样只用画布角标和弹层。
    - 公共数据在命令卡“公共数据”打开的窗口里编辑（不挡住画布，双击“使用它的节点”在画布上定位）。
    - 试运行的过程看运行日志，按流程分开记（示例程序记在“试运行 · 流程名”下）。
  - 最下面一行状态：节点数、连线数、选中节点；右边是宿主消息（`StatusMessage`）。
- `WorkflowNodeInspectorControl`：选中节点的图标、标题、类型 · 标识、上次运行状态（小标签，不截断）；“参数 | 结果 | 提示”三页，参数表放在卡片里、说明写在每行名称下面（`WorkflowPropertyPanel.UseCompactLayout`），不再占底部说明区；底部“打开节点编辑”进完整的节点窗口。参数改了立即写进流程（可撤销），不另设“应用”。
- `WorkflowStudioControl` 把检查器挂上；`FileMenu` / `AddFileMenuItem` / `Save` 给宿主换成自己的文件命令（操作台换成流程库的新建、打开、放弃草稿、重新加载）。
- 节点窗口里的子画布也用同一个工作区，但不挂检查器。

## 尺寸与缩放（排版规则）

- 卡片、间隔、标题行、状态行都是 `WorkflowBlock`：`LogicalExtent`（宽或高）和 `LogicalPadding` 按当前 DPI 换算，宿主按字体自动缩放时不再缩它们，否则会和 DPI 换算叠加（曾把工具箱 220 放大成约 700 像素）。
- `ModernButton` 是 UserControl，`AutoSize` 会算出 0 宽；控件一律用 `WorkflowSizing.Fit(控件, 逻辑宽度)` 定尺寸：高 30、外边距 4（相邻间隔 8），宽度给定或按文字量（按钮、分段、勾选），文字、字体、DPI 变了自动重量。分段控件各段等宽，按最长的文字定宽，不会被截断。
- 页面用这些部件拼，不直接用原生 ComboBox / CheckBox / TextBox / ListBox / ListView / TreeView：
  - `WorkflowToolbar`：顶部 52 高的工具条卡，`Add` 左边、`AddRight` 右边、`AddSeparator`；放 `ModernSelect`、`ModernInput`、`ModernSegmentedControl`、`ModernCheckbox`、`ModernSwitch`、`ModernButton`。
  - `WorkflowPage.Section`（42 高标题行的卡片，`HeaderOf(card).AddAction` 在标题行右边放按钮）、`Gap`（10 间隔）。
  - `WorkflowMetricStrip`（指标条，文字高度按字体量、垂直居中）、`WorkflowFactList`（侧栏“名称 …… 值”）、`WorkflowStatusLine`（最下面一行状态：左概况、右最近一条消息）。
  - `WorkflowItemList`（卡片里的列表：标题 + 副标题、状态点、右边小标签或图标按钮）、`WorkflowBanner`（故障横幅）、`WorkflowTones`（成功 / 运行中 / 提示 / 故障的颜色和小标签）。
  - 改名、连线备注这类一行输入用 `WorkflowInputDialog`。

## 其他页面（按设计稿画板）

- 运行页（`WorkflowRunPage`，原运行总览和运行监视合成一页）：左边流程列表（260：状态点、状态 / 提示 / 恢复轮次、故障标签、单独启停）| 中间指标条（运行次数、平均节拍、最近一圈、圈数、出错，统计清零）和只读画布卡 | 右栏“故障处理 | 节点耗时”（320，出故障时自动切到故障处理）| 状态行。轨迹等明细看运行日志页，运行页不再放下方的轨迹、令牌、并行域、子流程、输出表（右栏节点耗时直接取画布快照）。整线启停用宿主顶部按钮，页面不再放“全部启动 / 全部停止”。设计稿里的 OK / NG / 良率不做（流程只管自己的东西）。
- 故障处理（`WorkflowFaultWorkbenchControl`，运行页右栏）按窄栏竖排：查看（当前 / 最近结束的会话）、故障横幅、概况、停在哪里（流程 · 节点、位置、可否就地恢复）、所选流程的去向（不可用的灰显并写原因），处理流程没处理完时下面是放行、重新运行处理流程、全部停止。
- 节点提示页（`WorkflowNoticeCenterPage`）：工具条（类别分段、流程、搜索，右边全部确认）| 按节点（250）| 提示表 | 详情（330，标题行“确认此位置”）| 状态行。汇总流程管理里所有流程的提示板。
- 运行日志页：工具条（流程、运行、结果、只看控制步骤、搜索，右边跟随最新开关）| 运行记录（标题行全部展开、自动折叠）| 选中的记录（380，“名称 …… 值”，标题行复制）| 状态行。
- 流程库页：工具条（新建流程、导入 | 搜索、全部 / 已发布 / 草稿 / 有诊断、显示已删除，右边刷新）| 流程表 | 详情（380：标题行流程名和状态，下面打开设计、发布…、更多 ▾（查看修改、放弃草稿、历史版本、复制、改名、导出、删除），再下面文件、草稿、修改人和版本）| 状态行（概况和流程目录）。
- 节点编辑窗口：顶部节点标题卡（图标、标题、类型 · 标识、上次运行状态）| 左边页签卡（参数、输入输出、ROI 等列表页）| 中间主体卡（子画布、脚本、图像）| 右栏运行结果和本节点提示 | 底部取消 / 应用 / 确定。仍是弹出窗口，没做成页内页面。

## 统一刷新节拍（R4-b）

- `WorkflowUiRefresh.FramesPerSecond`：全局帧率，默认 10，可设 1～60，改动立即生效。
- `WorkflowRefreshClock.Subscribe(control, onFrame)`：WinForms 只有这一个界面线程定时器。
  - 每帧只调用看得见的控件，即控件可见、句柄已建、窗口没有最小化。
  - 没有订阅时定时器停止，控件释放时自动退订。
- 画布运行动画、视觉帧预览、模板编辑都挂在这个时钟上。
- `WorkflowStudioRuntimeBinding` 的快照拉取间隔默认跟随全局帧率，单独设置 `FrameIntervalMilliseconds` 后就固定不再跟随。停止类状态照旧立即补一帧。

## 节点尺寸（R6-c 收尾）

- `WorkflowCanvasNode.Width/Height` 默认 0，表示未设定。设计器打开时按内容决定，未设定尺寸的节点保持左上角不动。
- 插件未加载、而且文档里没写尺寸的节点，用界面层默认值 `WorkflowDesignerGeometry.DefaultNodeWidth/Height`（180×60）。
- WF008 只检查负数尺寸。

## 节点提示（R1-01 界面）

- 设计器会话的 `Notices` 由运行绑定设置，取自宿主的提示板。节点窗口里的子画布也会拿到它。会话按本画布路径（`NoticePlanPath`）读取提示。
- 角标：节点右上角外沿的警告色圆点或胶囊，显示条数，超过 99 显示 99+。
  - 点开弹层可以看提示，并"确认并清除"。全部流程的提示看节点提示页。
  - 角标和异常的红色、执行序号区分开。
- 画布级提示（无法归属到节点的，例如记录丢失）：画布右上角的浮层胶囊。有概览图时排在概览图下面。

## 公共数据清单页（R2-d 界面）

- 模型 `WorkflowPublicDataManifestModel`：把清单和节点使用合并成行。
  - 每行的状态是已登记、未登记、未使用或有错误之一，按根文档核对。
  - 可以新增、修改（改键时一次撤销）、删除，还可以"补齐未登记的"。
  - 修改都经过设计器会话，可以撤销。
- 设计页命令卡"公共数据"打开的窗口（`WorkflowPublicDataPage`）：
  - 一行操作（左概况，右新增、修改、删除、补齐未登记的），下面左清单表、右“使用它的节点”（380 宽）。
  - 选中一项，右边列出使用它的节点，双击定位。
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

## 页面基类

- `WorkflowPage`：结构同 Foundation 的 `FeaturePage`，提供：
  - 同样的入口：`OnRefresh(canOperate, force)` 和 `ShowMessage`。
  - 卡片、网格、文字、表格工厂，以及 `RestoreSelection`。
  - 单独运行时按统一时钟每秒刷新；由外壳驱动时把 `AutoRefresh` 设为 false。
  - 挂进操作台时把基类换成 FeaturePage，或写一个薄适配器，页面代码不用改。

## 运行页

- `WorkflowRunPage(center, catalog, documentOf, workbench)`：见上面“其他页面”。
- 画布 `WorkflowDesignerControl.ReadOnly`：只能平移、缩放、选中、看提示，不能拖动、连线、删除、粘贴、拖入节点，双击不打开节点编辑。
- 统计 `WorkflowRunStatistics`（UI.Shared）：只从界面拉到的快照推算。
  - 节拍：根画布有循环块时是主循环相邻两圈的间隔（一帧跳过多圈时平分），没有时是每次运行结束时的总耗时；取近 60 次平均。
  - 显示延迟：快照时刻到界面应用它的时刻。

## 运行日志页

`WorkflowRunLogPage`：选流程和某一次运行，把这次运行折叠成树；有多个流程时默认选“全部流程”，各流程同一次序的运行排在一起，运行下拉选“最近一次 / 倒数第几次”（按各流程自己的启动次序对齐）。数据由 `WorkflowRunLogBook`（Runtime）收集，界面只读快照。

- 收集：`book.Attach(center)` 挂到每个流程的出口（保留出口原有回调）；流程管理要用 `WorkflowRunLogBook.OutletOptions()` 作为 `FlowOutlet`（调试级以上、不含数据流类别），否则并行派发 / 汇聚、提示会被默认级别滤掉。不归流程管理的运行宿主（设计页试运行）用 `book.CreateSink(名字或取名字, 取执行计划)` 放进宿主的 `Recording.Sink`，按同样的过滤记在该名字下；取名字的写法每次启动时取一次，示例程序用它按流程记在“试运行 · 流程名”下（Foundation 目前记在“试运行”下，示例确认后再迁）。每个流程保留最近 20 次运行（一次运行 = 一次启动到停止），每次最多 10 万行，只在内存里。
- 历史：怎么存、存在哪由宿主决定，运行日志页只认读接口 `IWorkflowRunLogHistory`（`FindAsync` 按时间段、流程查有哪些运行；`LoadAsync` 取某次运行的全部原始事件）。宿主保存的是原始运行事件（和日志本收到的相同），读回来交给 `book.Replay(名字, 事件)` 按同一套规则重建，折叠、红标、详情、节点名称都和实时的一样。可选的 `WorkflowRunEventJson` 一行一条地写和读（小值保留类型，耗时等显示不变）。
  - 页面设了 `History` 后工具条有“历史”：选日期范围和流程查询，双击打开；看历史时流程、运行、跟随最新不能动，点“回到当前”返回。
  - 示例程序 `SampleRunLogFiles`（只为测试）：每次运行一个文件 `runlogs\日期\流程名\时分秒_运行ID.jsonl`，保留 30 天。
- 建树（`WorkflowRunLog`）：子流程按复合节点分层（事件的 `Parents`），循环块每圈一层，并行按分支令牌分层（`ParallelStarted` 的父令牌 → 分支令牌）；节点失败、提示、撤回、节点自己写的进度挂在那次执行下面；故障处理事件挂在最近出故障的那一层。失败和提示沿途标到每一层。
- 折叠（`WorkflowRunLogView`）：成功结束的子流程、循环圈、并行分支收起；连续成功的圈合并成一行“第 a–b 圈”；只展开最近一次失败 / 故障所在的那一路层级（更早的失败收起，行首红标）和正在运行的层。手动展开 / 收起优先，“自动折叠”恢复。筛选“失败与故障”“提示”和搜索只留命中的行和它们所在的各层。“只看控制步骤”（默认开）隐藏节点自己写的进度。
- 关掉“跟随最新”（手动选了旧的运行、向上滚动、手动展开收起都会关掉）后界面不再自动刷新，状态行提示有新记录；打开跟随或改筛选时刷新。
- 右边是选中记录的详情：位置、节点、结果、开始 / 结束、耗时、令牌、画布、消息和数据，可以复制。
- 数据项显示名称：记录里只存键（和语言无关），显示时由 `WorkflowRunLogBook.DataLabel(流程, 节点类型, 键)` 换成名称：引擎常用键（耗时、出口、原因……）→ 节点属性的 `WorkflowProperty` 名称、输出类型属性的 `DisplayName`（`OutputValue.X` 显示为“输出 · 名称”）→ 键本身。布尔值显示“是 / 否”。以后做多语言只换这一步。
- 停点挂起原因带节点名称（“停点 名称（ID） #短号”）；停点引起的挂起 / 解除挂在出故障的那一层，挂起引起的暂停 / 继续不单独记。
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
  - 运行页：单独启动 / 停止（`WorkflowRunPage.Gate`，同时交给故障处理）。
  - 故障工作台：放行、重新运行处理流程、全部停止（`WorkflowFaultWorkbenchControl.Gate`，另有 `CanOperate`）。
  - 设计器运行：开始（整个流程、当前子流程、单节点）和停止（`WorkflowStudioRuntimeBinding.Gate`）。
- 只包住“发出命令”，不等流程跑完。
- `WorkflowStudioRuntimeBinding.RunBlockedReason`：不能开始运行的原因，运行按钮灰显并在提示里写明。

## 未做（等阶段 G 或后续确认）

- 运动步骤、Tag 选择编辑器（阶段 G）。
- 报警、日志、权限、设置使用 Foundation 基座页面，不在 Workflow 里重做。
- WPF 适配。

## 示例程序

- `samples/DP.WorkFlow.Pages.Sample`：不依赖 Foundation，直接打开 5 个页面，带 3 条模拟流程（会报提示、偶发故障）。运行方法见该目录的 README。
