# ModernUI.WinForms RC → 1.0 优化路线

## 目标

在不扩大公开控件接口、不替换原生复杂控件语义的前提下，把当前控件库从“功能与视觉完成”推进到“可发布、可诊断、可长期维护”。

## P0：发布稳定性（已落地）

- 双目标 Release 零警告构建。
- shipped public-interface baseline 与 net48/net8 接口一致性。
- 96 DPI 公开行为测试与独立 TestHost。
- ComboBox Popup、Shell AutoComplete、RTL、Handle 重建和 Dispose 压力。
- DataGridView 下拉编辑器打开状态销毁契约。
- 独立 net48/net8 原生 HWND 压力进程及 GDI/USER/Process Handle 预算。
- NuGet 全新 net48/net8 消费项目 restore、build、run。
- Solution 项目集合自动核对和多轮 Soak。
- 正式 RC 十轮 Soak 已完成：每轮 207 项行为测试、net48/net8 各 120 次 NativeStress。
- 行为测试在程序集加载时固定 96 DPI 线程上下文；每轮关闭构建服务器，避免 TestHost 继承旧 PMv2 上下文。
- NativeStress 不抢前台或控制鼠标；后台原生 ComboBox 自动关闭单独计数，不误判为控件失败。

## P1：无障碍、系统设置与 Designer（已完成首轮）

### UIA

- Calendar：星期列头、日期 Cell、Parent、方向导航、选择/禁用状态和本地化动作。
- CollapsiblePanel：Expanded/Collapsed、默认动作和状态通知。
- Pagination：本地化根名称、导航按钮和页码摘要。
- 原生输入：保留原生 Provider，并把 ValidationMessage 同步为可读 HelpText。
- DateRange：开始/结束端点名称与原生 Provider，语言切换保持范围值。
- Dialog：标题、说明、Dialog Role 与 RTL 窗口布局。
- Notification：顶层 Alert、名称、说明与 Action Command。
- Tab：验证原生 PageTab Provider 的 Selection 状态切换，不叠加自定义 Provider。
- 原生输入：验证消息同步到 AccessibleDescription/HelpText。
- 后续仅做 Inspect/NVDA 只读验收；1.0 RC 不接管 `WM_GETOBJECT`，不引入自定义 COM UIA Provider。

### 系统设置

- High Contrast 使用 SystemColors，禁止固定 RGB 绕过语义 Token。
- Reduced Motion/禁用客户端动画时，所有非必要动画直接提交最终帧。
- RTL 覆盖 Popup 对齐、日期导航、分页方向和输入 Handle 重建。

### Designer

- net48 `DesignSurface/IDesignerHost` 创建全部 Toolbox 控件。
- PropertyDescriptor 修改、集合序列化元数据、默认值和 Dispose Smoke。
- 后续在安装 Visual Studio 的 CI/验收机执行真实 WinForms Designer 打开与保存。

## 主题与滚动交互帧门禁（已落地）

- `tools/run-ui-interaction-frame-tests.ps1` 覆盖 Light→Dark、Dark→Light、Animations、Reduced Motion 与 ModernScrollView 实时拖动。
- 无激活主题门禁验证 layered overlay 的 NOACTIVATE、Owner、PMv2 DPI、客户区完整覆盖、稳定生命周期与清理，并验证宿主旧/新主题帧完整且差异显著；Reduced Motion 不得创建 overlay。
- Windows 不为跨进程 `PrintWindow` 保留 layered-window Alpha，自动门禁不伪造圆形透明区的像素结论；圆形扩散像素仍由有人值守视觉验收，生产 Overlay 不增加测试消息或私有诊断接口。
- 滚动门禁向视口 HWND 发送真实鼠标消息，不移动系统光标；验证 18 个 offset 严格递增、视口内容 Hash 连续变化、无黑帧，且 `WS_EX_COMPOSITED` 仅在实时拖动期间存在。
- 探针 Gallery 使用 `ShowWithoutActivation`，从进程启动到事务结束持续断言系统前台 HWND 不变；不使用 Topmost、不控制鼠标、不向其他进程发送输入。
- net48/net8 的 96/144 DPI 共 20 个系统交互场景通过，并已集成 `verify-ui-release.ps1`。

## 系统设置确定性矩阵（已落地）

- `tools/run-ui-settings-matrix.ps1` 覆盖 Light/Dark/HighContrast token × Animations/Reduced Motion × LTR/RTL × 96/144 DPI × net48/net8，共 48 个独立进程场景。
- 每个场景验证首次物理 DPI、固定客户区、前台 HWND 不变、完整控件树主题和方向传播、代表性业务值保持，以及 Select Popup 的 Owner、NOACTIVATE、物理几何、方向边缘和真实渲染。
- HighContrast 场景直接消费当前 Windows `SystemColors`，但不修改用户全局辅助功能设置；真实系统 High Contrast 开关仍在专用验收机上执行。
- 矩阵捕获到 Select 只有 Popup 外框 RTL 对齐而内部行未镜像；现已镜像单选/多选的箭头、文本、图标、Tag、关闭图标、复选框和滚动提示，且保持 SelectedValue(s) 不变。
- 同方向 Select Popup 必须双框架像素一致；LTR/RTL 必须产生不同内容签名；Animations/Reduced Motion 的最终帧必须一致。

## 连续帧 Popup 门禁（已落地）

- `tools/run-ui-continuous-frame-tests.ps1` 在独立进程中捕获 ComboBox、Select、DatePicker 和 ToolTip 从首次可见开始的连续帧。
- 每帧验证 Popup HWND 身份、Owner、圆角 Region、PMv2 DPI、物理几何、内容色差、内容 Hash 和主窗体稳定性；不抢前台、不移动鼠标。
- 固定几何分别为：Select `300×112`/`450×169`，DatePicker `300×288`/`450×433`，ToolTip `312×77`/`463×111`，ComboBox `340×112`/`510×169`。
- ModernComboBox 继续使用原生编辑 HWND 承担文本输入、自动完成与 UIA，候选层改由共享 ModernPopupController 承担，以统一圆角、DPI、RTL 和首帧契约。
- 压力矩阵捕获到 DatePicker/ToolTip 偶发的全黑首帧；`ModernOwnedOverlayForm` 现在在设置 `WS_VISIBLE` 前预渲染完整隐藏 backing surface，显示后仅做一次最终 PMv2 绘制提交。
- 修复后完成 5 轮 96 DPI 与 3 轮 144 DPI 组合矩阵，共 864 帧无黑帧、几何变化或同进程内容变化。
- 连续帧脚本已集成 `verify-ui-release.ps1`。

## 真实业务页面试点（已落地）

- Gallery 新增 `BusinessScenario` 设备参数页，不增加发布控件或公开接口。
- 页面通过公开接口组合 BindingSource、ValidationSummary、ModernCommandBar、异步连接测试、保存/恢复快照和运行时中英切换。
- net48/net8 同源操作探针验证：验证失败、值保持、保存恢复、异步成功和页面关闭取消。
- Light/Dark 与固定滚动分段已纳入双框架 96 DPI 截图对标。

## P2：真实 PMv2 与操作级视觉

- 双显示器首次目标屏创建：96→144→96，之后扩展到 120/192 DPI。
- 每个档位由 `GetDpiForWindow`/`DeviceDpi` 证明，不接受截图缩放推断。
- ComboBox、Calendar、ToolTip、Message、Notification 的首次打开连续帧。
- ListBox/TextArea/ScrollView 连续拖动中间帧和边界滚轮传递。
- 在真实系统 High Contrast 已开启的专用验收机补充只读环境确认；确定性 token 矩阵已自动化。

> 当前机器只有一个显示器，真实跨屏往返必须在具备两个不同 DPI 显示器的环境完成。

## P3：性能和资源预算（数据控件首轮已落地）

- 空闲 CPU、GDI/USER/Process Handle、首次 Popup、主题切换、DPI 往返预算进入 CI。
- 双框架数据压力已覆盖 10,000 项 ListBox、2,000 项 ListView、约 2,000 节点 Tree、2,000 行 Grid。
- 预热后的完整第二轮资源预算为 Process Handle +1、GDI 0、USER 0。
- 后续扩展到数据绑定增量更新、虚拟模式和真实连续滚动帧。
- 不可见动画不得持续 60Hz 失效。
- 禁止每帧整页 Refresh/Update/UPDATENOW。

## P4：内部模块深化

- 继续深化 Overlay Runtime、ModernTextLayout、滚动 Adapter 和 ModernListDataView。
- 选择控件只共享 Popup 行模型与数据视图，不创建巨型 `ModernSelectBase`。
- 反馈队列、Owner 订阅和主题发现集中到内部 Feedback Host。
- 不因文件行数继续机械拆分；只有知识边界清晰时使用 partial。

## 1.1 基础控件扩展（第一、第二批已实现）

### 第一批：缺失的原生语义基础控件

- ModernMaskedInput、ModernRichTextBox、ModernCheckedListBox。
- ModernLinkLabel、ModernGroupBox。
- ModernMenuStrip、ModernToolStrip，并与 ContextMenu/StatusStrip 共用 ToolStrip 主题基础。
- 全部进入同一 net48/net8.0-windows 包、Gallery、Theme Adapter、DesignerSmoke 和 NuGet 消费门禁。

### 第二批：现有控件深化

- 输入/选择/日期时间控件统一 ReadOnly、Changed/Committed 和 WinForms Binding 元数据；替代绑定属性具备约定命名的 Changed 事件。
- InputNumber：nullable、格式、千分位、前后缀、Clamp/Reject、长按步进、首次 null 提交与重复提交治理。
- Slider/ProgressBar：水平/垂直、反向、RTL、刻度、值格式和自定义文本。
- Button/Command：调用方拥有的自定义 Image，所有 Adapter 不转移所有权。
- Tree：Independent/Descendants/AncestorsAndDescendants 勾选传播。
- ListBox/ListView/TreeView/DataGridView：横向现代滚动 Chrome、双轴角区、RTL 沟槽和方向；滚动状态仍由原生控件或单一托管属性提供。
- 禁止在 WM_PAINT 内同步刷新滚动 Adapter；RTL + 双轴范围必须无消息闭环。

### 第三批：暂缓

ModernDropDownButton、ModernSplitButton、公开 ModernCalendar 和 ModernColorPicker 暂不实施。后续只有在真实业务页面证明现有 Button/ContextMenu、DatePicker/Calendar Popup 或输入组合无法安全表达独立键盘、绑定和无障碍语义时，才重新评审。

专业运动、通讯、视觉、报警、趋势和设备面板继续位于独立扩展工程，不进入基础包。

## 1.0 发布判定

必须同时满足：

1. `verify-ui-release.ps1` 通过。
2. 默认十轮 `run-ui-soak.ps1` 通过。
3. net48/net8 NuGet 新项目消费通过。
4. 双框架原生压力与资源预算通过。
5. 真实双屏 96↔144 往返通过。
6. UIA、High Contrast、Reduced Motion、RTL 发布矩阵无 P0/P1 缺陷。
7. Public interface baseline 仅在明确版本决策下更新。
