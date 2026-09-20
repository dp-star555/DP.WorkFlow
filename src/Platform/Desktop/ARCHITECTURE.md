# UI 模块架构

## 稳定依赖方向

```text
src/Platform/Localization/ModernUI.Localization
                 ↑
src/Platform/Desktop/ModernUI.WinForms（包含 PropertyGrid）
                 ↑
ModernUI.WinForms.Gallery / DP.WorkFlow.UI.WinForms
```

- `ModernUI.Localization` 是平台中立的基础设施，目标为 `netstandard2.0;net8.0`，可由 WinForms、WPF、服务和用户内容生成模块复用。
- `ModernUI.WinForms` 是目标为 `net48;net8.0-windows` 的通用控件库，并包含基于 `TypeDescriptor` 的 PropertyGrid；不得引用 DP.WorkFlow 或 Vision。
- PropertyGrid 保留 `ModernPropertyGrid.WinForms` namespace 作为源码兼容边界，但不再单独发布程序集。
- Gallery 是分类案例入口和视觉验收组合根，不包含生产控件实现。
- 业务项目必须显式引用自己直接使用的项目，不依赖传递引用。

当前不继续拆分程序集。只有出现第二个真实平台 Adapter 或独立发布需求时，才增加新的项目 seam。

## ModernUI.WinForms 内部模块

```text
ModernUI.WinForms/
├─ Core/          控件基类、缓冲容器、数据成员解析和无障碍基础
├─ Drawing/       GDI Canvas、矢量图标、共享绘制几何
├─ Theming/       Theme Token、主题应用和切换动画
├─ Animation/     UI 线程动画调度和可见性治理
├─ Overlays/      Popup、Dialog、ToolTip
├─ Feedback/      Alert、Message、Notification、EmptyState
├─ Commands/      Command、CommandBar、快捷键、MenuStrip/ToolStrip 和菜单 Adapter
├─ Validation/    验证状态、Provider 和 Summary
├─ Scrolling/     滚轮路由、现代滚动视图和原生覆盖层
├─ Native/        原生输入/日期控件 Adapter
├─ Localization/  WinForms 资源和 Extender Provider
├─ Compatibility/ net48 与现代 .NET 的 Guard、数值、集合、运行时兼容层
├─ PropertyGrid/  属性发现、编辑、提交、主题与 PropertyGrid 资源
└─ Controls/
   ├─ Actions/     Button、LinkLabel
   ├─ Inputs/      Input、MaskedInput、RichTextBox、TextArea、Number、ComboBox、Slider、日期时间
   ├─ Selection/   Checkbox、Radio、Switch、Select、Segmented
   ├─ DataDisplay/ Tree、List、CheckedList、DataGrid
   ├─ Navigation/  Tab、Pagination
   ├─ Layout/      Panel、GroupBox、Collapsible、Splitter、StatusBar
   └─ Indicators/  Badge、Progress、Spinner
```

物理目录不改变公开 namespace；调用方统一使用 `ModernUI.WinForms`。

## 深模块与 seam

- `ModernOwnedOverlayForm` / `ModernOverlayAnchorTracker`：Popup、ToolTip、Message、Notification 共享的 Owner、NOACTIVATE、隐藏 HWND 创建、最终 PMv2 帧提交和锚点层级清理实现；禁止 `CS_DROPSHADOW` 生成无 Owner 阴影窗口。同一 UI 线程仅允许一个可见 ToolTip，多个 Provider 会互斥替换。
- `ModernPopupController`：托管选择 Popup 的互斥生命周期和定位实现。原生 ComboBox/ContextMenu 是有意保留的 Adapter。
- `ModernTextLayout`：反馈和浮层唯一的 TextRenderer 测量/绘制 flags 与 CJK 末字安全区实现。
- `IModernScrollAdapter`：滚动 Chrome 的内部 seam；`NativeWindowScrollAdapter` 和 `ManagedControlScrollAdapter` 分别适配标准 Win32 ScrollInfo 与 TextArea/ListBox 等托管位置源。
- `ModernListDataView`：ComboBox、Select、SelectMultiple 共享的 IList/IListSource 解析与 IBindingList 订阅生命周期。
- `ModernAnimation`：有限动画的共享 UI 线程调度器，并遵循 Windows 客户区动画偏好。
- `FinalLayoutTransaction`：把同一消息轮次中的 Resize、DPI 和子控件 Layout 请求合并为一次最终布局；Handle 重建时取消旧任务。
- `FeedbackAutoCloseTimer`：Message/Notification 的剩余时间和组合暂停原因。
- `DataMemberResolver`：选择控件共享的数据成员路径规则。
- `ModernValidationProvider`：验证状态与异步校验 seam。
- `ModernCommand`：Button、CommandBar、ContextMenu、Notification Action 的共享操作模型。CommandBar 是数据驱动且自适应的业务操作面；ModernToolStrip 是保留 ToolStripItem/Designer/MDI 语义的兼容面，二者不应在同一业务页面承担同一组命令。

不要为单一实现创建外部 interface；测试通过现有公开控件接口和可观察结果进行。

## PropertyGrid 文件职责

`ModernPropertyGrid` 保持一个公开模块，内部使用 partial 文件按知识分区：

- `ModernPropertyGrid.cs`：公开属性、构造、生命周期。
- `.Build.cs`：属性发现、搜索、分类和行构建。
- `.Editors.cs`：编辑器选择与创建。
- `.Commit.cs`：值刷新、转换、验证和提交。
- `.Selection.cs`：选择、Hover 和详情面板。
- `.Theme.cs`：主题、DPI 和行尺寸。
- `.Localization.cs`：本地化与 Presentation 缓存。

这只是实现拆分，不扩大公开接口。

## Gallery 文件职责

- `GalleryForm` 是按使用场景分类的主入口；分类卡片打开独立的 `DemoCategoryForm`。
- `GalleryDemoCatalog` 集中维护分类名称、说明和能力索引。
- `DemoCategoryForm` 展示可操作的状态、事件与组合用法；底部状态条显示最近一次公开事件和值，不用调试日志解释交互结果。
- `BusinessScenario` 是 Gallery 内部真实业务试点：设备模型、快照和连接模拟不得进入 `ModernUI.WinForms`；页面必须只经公开控件接口完成绑定、验证、命令、本地化和释放。
- ComboBox、Select、DatePicker、ToolTip 的首帧完整性由 `run-ui-continuous-frame-tests.ps1` 做独立进程黑盒验收；Select 同时覆盖 LTR/RTL，生产控件不得为截图脚本增加公开测试接口。
- `ModernOwnedOverlayForm.ShowFinalFrame` 必须在 `WS_VISIBLE` 前完成隐藏 HWND 树、最终字体/几何/Region 以及一次 backing-surface 预渲染；显示后只允许一次 PMv2 最终绘制提交，不得演变为动画期同步刷新。
- `run-ui-interaction-frame-tests.ps1` 是主题事务与 ModernScrollView 拖动的独立进程黑盒门禁；Gallery 探针必须无激活启动，并持续证明系统前台 HWND 不变，不得使用 Topmost、控制鼠标或向其他进程发送输入。
- `run-ui-settings-matrix.ps1` 是 Light/Dark/HighContrast token × Animations/Reduced Motion × LTR/RTL × 真实 96/144 DPI 的确定性门禁；只允许修改 Gallery 子进程设置，不得修改用户全局系统设置或用模拟 DPI 冒充物理显示器。
- HighContrast 自动矩阵验证 `ModernTheme.HighContrast` 对当前 `SystemColors` 的消费；真实系统 High Contrast 开关由专用验收环境只读确认，不得在日常 Release 脚本中切换用户辅助功能。
- RTL 不仅要求 Popup 外框右边缘对齐，还必须镜像具有方向含义的输入面和 Popup 行布局；SelectedValue(s)、项目顺序和业务状态不得变化。
- 跨进程 GDI/PrintWindow 不保留 UpdateLayeredWindow 的 Alpha；自动主题门禁只验证 Overlay 契约和宿主最终帧，圆形透明区像素由有人值守视觉验收。不得为绕过该系统限制向生产 Overlay 增加私有测试接口。
- `WS_EX_COMPOSITED` 仍只允许由 ModernScrollView 在实时滑块拖动事务中临时启用；鼠标释放后必须立即移除。
- ModernComboBox 的 DropDownWidth 是逻辑像素；共享 ModernPopupController 在打开时按锚点窗口 DPI 计算最终物理宽度，net48/net8 不得各自维护缩放分支。
- 分类案例应至少覆盖视觉变体、运行时状态、键盘/鼠标交互、数据或命令绑定以及一个可观察事件；禁止只摆放无事件的静态控件截图。
- 长案例页通过 `--demo=<Category> --scroll=<logicalOffset>` 建立固定滚动分段；`compare-gallery-frameworks.ps1 -ScrollOffset` 用同一源码验证 net48/net8 中段与底段。
- `VisualStatesForm` 继续作为固定视觉基线入口，通过 `--visual-states` 启动。

## 维护规则

1. 依赖只能从业务层指向通用层。
2. 业务类型不得进入 ModernUI 的公开枚举或控件实现。
3. 新顶层浮层必须继承 `ModernOwnedOverlayForm`；选择型 Popup 复用 `ModernPopupController`，不得复制 Owner、CreateParams、隐藏首帧或锚点订阅实现。
4. 新状态颜色必须进入语义 Theme Token，不在控件中散布 RGB。
5. 原生输入、复杂列表和表格优先使用 Adapter，保留 Win32 输入、绑定和 UIA。
6. Designer 文件、RESX 和运行时逻辑保持配对；移动 RESX 前必须验证 manifest resource name。
7. 结构整理不得改变公开行为，必须通过 Release 构建、STA 行为测试和 Gallery 视觉检查。
8. `.csproj.user`、`bin/`、`obj/` 不属于源码。
9. DPI 回调只失效 Metrics/测量并请求最终布局；不得在同一次转换的 `SizeChanged`、`OnResize` 和 `OnDpiChangedAfterParent` 中重复递归测量内容树。
10. `ModernComboBox` 首次自动完成在外层 HWND 已存在、原生子 ComboBox 尚未创建的窗口内同步配置，避免事后重建编辑 HWND；RTL 父 Handle 重建前必须停用 Shell AutoComplete，基类事务结束后再恢复。
11. 发布门禁使用独立 96 DPI PowerShell 进程运行逻辑行为测试；真实 PMv2 测试必须由 Gallery 和 `GetDpiForWindow` 证明，二者不能混为同一基线。
12. `ModernDataGridView` 销毁前必须关闭现代下拉 Popup，并通过 `CancelEdit()` + `EndEdit()` 终止编辑控件；否则基类清空 Columns 时可能仍定位已移除列。
13. 原生 HWND 生命周期使用独立双目标 `ModernUI.WinForms.NativeStress` 进程验收；原生崩溃不得只依赖会被一并终止的 xUnit TestHost。
14. 96 DPI 逻辑门禁若从已加载 WinForms 的父 PowerShell 启动，必须在创建子进程前应用 `DPIUNAWARE` 兼容层；子进程启动后再调用 DPI API 不能覆盖继承上下文。
15. 打包前由仓库 `global.json` 对 ModernUI 项目执行强制 restore，确保 `net8.0-windows7.0` 平台版本已写入依赖组，禁止复用其他 SDK 留下的不兼容 assets。
16. Shell AutoComplete 会子类化 ComboBox 编辑 HWND；`ModernComboBox` 的 RTL 父 Handle 重建必须先停用 AutoComplete，并在基类事务结束后恢复，禁止直接销毁仍被 Shell 子类化的编辑 HWND。
17. RC 门禁包含 shipped public-interface baseline、Solution 项目集合、独立双框架原生压力、全新 NuGet 消费端和可配置多轮 Soak。
18. 自绘复合控件必须暴露与键盘和鼠标等价的 UIA 状态与默认动作；原生输入继续委托原生 Provider，并将验证消息同步到 AccessibleDescription/HelpText。
19. Calendar UIA 树固定为星期 ColumnHeader + 当月日期 Cell；Cell 必须有 Parent、屏幕 Bounds、禁用/选择状态、方向导航和本地化默认动作。
20. `DesignSurface/IDesignerHost` 必须能够创建并释放所有 Toolbox 控件；`ToolStripDropDown` 只进入设计器组件容器，不加入 Form.Controls。
21. DateRange 的 UIA 根固定暴露两个原生 DatePicker Provider，端点名称随语言切换更新，但 StartDate/EndDate、选择和 Popup 状态不得变化。
22. 声明 `[DefaultValue]` 的本类型属性必须与构造后运行时默认值一致，并在默认状态下 `ShouldSerializeValue == false`；继承属性的语义覆盖不套用该规则。
23. `ModernThemeControlAdapter` 是原生基类现代控件的唯一主题分派实现；Overlay 主题解析和递归 ApplyTheme 不得各自维护类型列表。
24. RTL 必须镜像具有方向含义的导航顺序和图标；Pagination 与 Calendar 的业务页码/日期不得因镜像变化。
25. 大数据控件继续采用原生 ListBox/ListView/TreeView/DataGridView；只有双框架 `DataStress` 超预算时才引入虚拟化或增量 Adapter，禁止无基线重写。
26. RC 行为测试在程序集模块初始化和每个 STA 测试线程建立 96 DPI 上下文；Soak 每轮关闭构建服务器，禁止把 DPI 上下文漂移误判为控件双缩放。
27. 原生 Popup 压力不得抢占用户前台或控制鼠标；宿主处于后台时，ComboBox 自动关闭属于 Windows 契约，门禁改验证首次 `ComboLBox` HWND 创建并记录 `backgroundClosures`。
28. 1.0 RC 不接管 `WM_GETOBJECT`，不手写 COM UIA ABI，不新增 WPF/UIAutomation 生产依赖；无障碍继续使用 WinForms 原生 Provider 和稳定的 `AccessibilityObject` seam。
29. 1.1 的 `Changed` 事件表示值确实变化，`Committed` 只表示用户完成有效编辑事务；程序赋值、绑定、取消和验证失败不得触发 Committed，业务 Pending/Accepted/Rejected 由命令或领域层表达。
30. ReadOnly 只阻止用户输入、选择或步进；程序赋值和数据绑定必须继续有效。可编辑 Combo、日期和原生 Spinner 必须同时拦截键盘、字符、滚轮及 Popup 提交路径。
31. WinForms 可绑定属性必须同时具备 `[Bindable(true)]` 和约定命名的 `PropertyNameChanged` 事件，不能只设置 `DefaultBindingProperty` 元数据。
32. 原生数据控件不得在 `WM_PAINT` 内同步隐藏滚动条、改变子控件 Z-Order 或刷新 Adapter；范围变化通过控件消息/公开事件请求一次 `FinalLayoutTransaction`，避免 RTL 双轴场景形成绘制消息闭环。
33. 横向滚动的 RTL 方向由 `IModernScrollAdapter.IsDirectionReversed` 表达；不得在共享 Overlay 中按控件类型分支。ListBox/Tree 的原生 RTL ScrollInfo 与 ListView/Grid 的逻辑零点不同。
34. `ModernButton.Image` 与 `ModernCommand.Image` 由调用方拥有；Button、CommandBar、ContextMenu 和 Dispose 路径不得释放调用方图像。

## 1.1 批次边界

- 第一批已纳入：ModernMaskedInput、ModernRichTextBox、ModernCheckedListBox、ModernLinkLabel、ModernGroupBox、ModernMenuStrip、ModernToolStrip。
- 第二批已纳入：统一 ReadOnly/Changed/Committed/Binding 元数据、自定义 Image、InputNumber 格式和范围、Slider/Progress 方向、Tree 勾选策略、原生数据控件横向滚动与 RTL。
- 下拉视觉统一：ModernComboBox 的编辑 HWND 继续保留原生输入、自动完成与 UIA，候选层改由共享 ModernPopupController 承载；Select、多选、日期和 ToolStrip 下拉统一使用主题 Radius，避免出现系统矩形弹层。
- 第三批暂缓：ModernDropDownButton、ModernSplitButton、公开 ModernCalendar、ModernColorPicker。只有真实页面证明组合方案不安全或语义不完整时，才重新执行基础控件准入评审。
- 专业运动、通讯、视觉、报警和设备 UI 仍只能进入独立扩展工程，并单向依赖 ModernUI.WinForms。

## 下一批结构热点

1. `ModernComboBox`、`ModernSelect` 和 `ModernSelectMultiple` 已共享 `ModernListDataView`；下一步只收敛 Popup 行模型，不创建巨型基类。
2. `ModernScrollView` 已按 Lifecycle、Interaction、Rendering 和 Native 拆为 partial；原生滚动覆盖层已共享两个真实 Adapter，后续不得重新散布 `GetScrollInfo`。
3. Message / Notification：继续把队列容量、Owner 订阅和主题发现集中为内部 Feedback Host。
4. Calendar 日期单元格已提供 UIA Cell 子节点；后续补充行列头关系和 UIA 自动化矩阵。
5. 在窗口级 Theme Context 稳定前，保留当前显式 `ApplyTheme` 兼容接口。
6. Gallery 八个 Build 方法已按分类拆为 partial 文件；主文件只保留组合根、共享布局和资源释放。
7. `ModernToolTip.Bubble.cs`、`ModernTabControl.Header.cs`、`ModernListView.Native.cs` 分离顶层气泡绘制、Tab Header 实现和原生 HeaderWindow；这些是同一公开模块的实现分区，不增加外部 seam。
