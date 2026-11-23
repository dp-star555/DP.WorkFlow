# WinForms 界面：主题、刷新节拍与新增页面

2026-11-23 起只维护 WinForms 界面，WPF 以后再适配（WPF 继续用原来固定的深色画布 `WorkflowCanvasPalette.Classic`）。

## ModernUI 与 DP.Foundation 共用一份

- `DP.WorkFlow.UI.WinForms` 引用 `ModernUI.WinForms` 包 1.1.1-foundation.23（`vendor/packages`，`NuGet.Config` 把 `ModernUI.*` 映射到这里），和 DP.Foundation 操作台是同一份。两边程序集同名，一个进程只能加载一份，所以 Workflow 页面才能放进操作台。
- 以 DP.Foundation 的 `vendor/src/ModernUI.WinForms` 为准。要改 ModernUI，先在 Foundation 改并打新包，再把新包复制到本仓库 `vendor/packages`，同时改 `Directory.Packages.props` 里的版本。
- 本仓库 `src/Platform/Desktop/ModernUI.WinForms` 源码现在只给 Gallery、示例和 ModernUI 自己的测试用，Workflow 界面不再引用它。

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
  - 当前会话：上报、受影响的流程，以及各停点的位置、原因、恢复类别、最后一步。
  - 最近结束的会话。
- 每个流程列出全部去向：继续原操作、重做、回到各入口、结束本圈、停止。是否允许由停点判断，不允许的写明原因。
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

## 未做（等阶段 G 或后续确认）

- 运动步骤、Tag 选择编辑器（阶段 G）。
- 报警、日志、权限、设置使用 Foundation 基座页面，不在 Workflow 里重做。
- WPF 适配。
