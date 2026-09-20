# Modern WinForms UI

该目录集中存放独立于 DP.WorkFlow/Vision 业务的 WinForms UI 工程。

## 工程

完整依赖规则、内部目录和维护约束见 [`ARCHITECTURE.md`](ARCHITECTURE.md)。

- `ModernUI.WinForms`：主题、DPI、绘制、动画、基础现代控件，以及基于 `TypeDescriptor` 的 PropertyGrid；同时生成 `net48` 与 `net8.0-windows` 版本。
- `ModernUI.WinForms.Gallery`：按使用场景分类的控件案例与固定视觉基线程序；同一套窗体源码同时编译为 `net48` 和 `net8.0-windows`，用于直接对比。
- `../../samples/ModernUI.WinForms.Sample`：net48 消费端样例，已加入解决方案 `Samples` 文件夹，用于验证应用清单和项目引用方式。
- `../../tests/Platform/ModernUI.WinForms.NativeStress`：net48/net8 独立原生生命周期压力程序，覆盖 ComboBox Popup、AutoComplete Handle 重建、待处理回调 Dispose 及 DataGridView 下拉编辑器销毁。
- `../Infrastructure/ModernUI.Localization`：平台中立的本地化基础设施，不隶属于 UI。

## 目标框架

- .NET Framework 4.8 项目使用 `ModernUI.WinForms.dll` 的 `net48` 构建和经典 Designer。
- .NET 8 项目使用 `net8.0-windows` 构建和现代进程外 Designer。
- `ModernUI.Localization` 同时生成 `netstandard2.0` 与 `net8.0`。
- 两个 UI 构建保持相同的公开 namespace 和控件类型；调用方不能混用两个输出目录中的 DLL。

Framework 运行和控件创建 Smoke 项目位于 `tests/Platform/ModernUI.WinForms.Net48.Smoke`。

## 1.1 快速开始

```powershell
dotnet add package ModernUI.WinForms --version 1.1.0
```

```csharp
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

var input = new ModernInput { Text = "Camera 01" };
input.TextChanged += (_, _) => Preview(input.Text);       // 值确实变化
input.TextCommitted += (_, _) => SaveDraft(input.Text);  // 用户完成有效事务
input.ReadOnly = true;                                   // 用户不可改，程序仍可赋值

ModernUiSettings.ApplyTheme(form, ModernTheme.Dark);
```

应用负责创建并释放控件、`Image`、`ImageList`、命令和本地化上下文。`ModernButton.Image`/`ModernCommand.Image` 始终由调用方拥有；控件不会释放它们。Popup 型控件必须在 Owner Form 的 UI 线程创建和释放。

.NET Framework 4.8 的 PerMonitorV2 支持不仅依赖进程 DPI API：启动项目必须在 `App.config` 中声明 `System.Windows.Forms.ApplicationConfigurationSection/DpiAwareness=PerMonitorV2`，在应用清单中声明 Windows 10 `supportedOS`，并让 `Application.EnableVisualStyles()` 成为入口中的首次框架调用。不要在 Framework 入口提前调用 `SetProcessDpiAwarenessContext`；这会使原生 HWND 已处于 PMv2，而 WinForms 托管 `DeviceDpi` 仍停留在系统 DPI，形成字体与控件几何比例失配。Gallery 的 `App.config`、`app.manifest` 和 `GalleryApplication` 是参考配置。

## Visual Studio 工具箱

公开控件保留中文 `DisplayName`、说明和 `ToolboxBitmap` 元数据，供经典 Designer、文档与控件目录复用。Visual Studio 18 的现代 .NET WinForms Designer 使用独立的进程外工具箱发现，目前固定显示 CLR 类型名和程序集分类，不读取这些元数据来重命名工具箱条目。

## 运行 Gallery

```powershell
# .NET Framework 4.8（同时使用 Localization/netstandard2.0）
dotnet run --project src/Platform/Desktop/ModernUI.WinForms.Gallery/ModernUI.WinForms.Gallery.csproj -f net48

# .NET 8
dotnet run --project src/Platform/Desktop/ModernUI.WinForms.Gallery/ModernUI.WinForms.Gallery.csproj -f net8.0-windows
```

Gallery 主界面按操作、输入、选择、数据展示、导航布局、反馈浮层、日期时间和 PropertyGrid 分类；点击卡片打开对应的可操作 Demo。也可以直接启动某一分类：

```powershell
dotnet run --project src/Platform/Desktop/ModernUI.WinForms.Gallery/ModernUI.WinForms.Gallery.csproj -f net48 -- --demo=Inputs
dotnet run --project src/Platform/Desktop/ModernUI.WinForms.Gallery/ModernUI.WinForms.Gallery.csproj -f net8.0-windows -- --demo=Inputs
```

双框架同屏尺寸和截图对标：

```powershell
dotnet build src/Platform/Desktop/ModernUI.WinForms.Gallery/ModernUI.WinForms.Gallery.csproj -c Release
./tools/compare-gallery-frameworks.ps1 -Page Main -TargetDpi 96
./tools/compare-gallery-frameworks.ps1 -Page DateAndTime -TargetDpi 144
./tools/compare-gallery-frameworks.ps1 -Page FeedbackAndOverlays -FeedbackProbe Message -TargetDpi 96
./tools/compare-gallery-frameworks.ps1 -Page FeedbackAndOverlays -FeedbackProbe Notification -TargetDpi 144
./tools/measure-gallery-dpi-transition.ps1 -TargetFramework net48 -GalleryArguments '--demo=Inputs','--no-animation' -AssertLogicalClientSize
```

比较脚本要求真实目标 DPI 显示器，并断言主窗体与案例窗体客户区尺寸；net48 与 net8 使用相同源码、参数和截图尺寸。`measure-gallery-dpi-transition.ps1 -AssertLogicalClientSize` 还会验证每次 96↔144 往返后的物理客户区及同 DPI 重访不漂移。

发布前统一执行：

```powershell
./tools/verify-ui-release.ps1 -Configuration Release
```

该脚本验证双目标 Gallery、net48/net8 公开接口一致性、固定 96 DPI 行为测试、net48 Smoke，并检查生成的 NuGet 同时包含 `lib/net48` 与 `lib/net8.0-windows7.0`。真实 DPI 仍由 Gallery 跨屏脚本独立验收，不能用 96 DPI 行为测试替代。

资源预算可独立执行：

```powershell
./tools/measure-ui-resource-budget.ps1 -TargetFramework net48
./tools/measure-ui-resource-budget.ps1 -TargetFramework net8.0-windows
```

脚本记录空闲 CPU、Process Handle、GDI 和 USER Handle 增量，并在超过预算时失败。

视觉状态基线窗口：

```powershell
dotnet run --project src/Platform/Desktop/ModernUI.WinForms.Gallery/ModernUI.WinForms.Gallery.csproj -f net48 -- --visual-states
dotnet run --project src/Platform/Desktop/ModernUI.WinForms.Gallery/ModernUI.WinForms.Gallery.csproj -f net8.0-windows -- --visual-states
# 两个目标均可继续追加 --dark、--english、--scroll=1040 或 --no-animation
```

## 多语言

本地化遵循 `docs/多语言框架设计.md`：平台中立核心位于 `src/Platform/Localization/ModernUI.Localization`，以 `TextKey`、`ITextCatalog`、不可变 `LocalizationSnapshot` 和 `ILocalizationManager` 为稳定 seam；ModernUI 与 DP.WorkFlow WinForms 分别拥有自己的 RESX。

```csharp
var manager = WorkflowUiLocalization.CreateManager(CultureInfo.GetCultureInfo("zh-CN"));
using var managerLifetime = (IDisposable)manager;
ModernUiSettings.ApplyLocalization(root, manager.Context);
await manager.ChangeLocaleAsync(CultureInfo.GetCultureInfo("en-US"));
```

`ModernLocalizationProvider` 可为普通 WinForms 控件绑定 `TextKey`、`PlaceholderKey`、`AccessibleNameKey` 和 `AccessibleDescriptionKey`。ModernInput、Select、SelectMultiple 的默认 Placeholder 使用 `null = 框架本地化默认值` 语义，显式文本和用户输入不会在语言切换时被覆盖。Gallery 支持 `--english`，视觉脚本支持 `-IncludeEnglish`。

命名占位符由核心统一处理，支持 `{name}`、`{value:N2}` 和 `{{literal}}`；缺少调用参数、模板括号错误或语言间占位符集合不一致会明确失败。`LocalizationResourceValidator` 用于资源治理测试。

`ModernPropertyGrid.LocalizationContext` 与 `PresentationProvider` 构成 PropertyGrid 本地化 seam。Provider 按 Culture 返回稳定 `PropertyKey/CategoryKey` 以及显示名、分类、说明、单位和值文本；运行时切换会重新搜索和排序，并按稳定身份恢复分类折叠、属性选择、滚动位置和可恢复的编辑焦点。属性较少的紧凑页面可设置 `ShowSearchBar = false`，隐藏重复的搜索与计数抬头。

`WorkflowPropertyPanel` 已公开 `LocalizationContext` 和 `PropertyPresentationProvider` 代理，供业务组合根注入。`WorkflowRuntimeText` 统一将工作流执行状态、节点状态及框架拥有的 Trace 步骤映射为当前语言文本，自定义 Trace 步骤保持稳定原值。`WorkflowDiagnosticsControl` 是首个业务试点：使用 ModernCommandBar、ModernAlert、ModernListView 和组合业务资源，语言切换时保留模型和选择状态。

`WorkflowRuntimeMonitorControl` 是第二个业务试点：使用现代 CommandBar、Alert、TabControl、ListView、日期范围和统一验证；支持本地化 Trace 筛选、90 天范围验证、暂停、刷新、本地化 CSV 表头导出及筛选状态保持。导出失败使用 `ApplicationError(Code, Arguments, TraceId)` 传递稳定错误语义，并由当前 `LocalizationContext` 使用命名占位符生成最终 Dialog 文本。

## 1.1 基础控件与编辑事务

第一批新增控件已经进入基础包：

- `ModernMaskedInput`：原生 `MaskedTextBox` 掩码、剪贴板、验证和无障碍语义；
- `ModernRichTextBox`：原生 RTF、选择、链接、撤销、剪贴板和滚动；
- `ModernCheckedListBox`：持续展开的批量勾选工作区，焦点行与勾选集合独立；
- `ModernLinkLabel`、`ModernGroupBox`：保留原生链接、助记键、容器和 Grouping 语义；
- 选择类弹层统一使用主题 `Radius`：`ModernComboBox`、`ModernSelect`、`ModernSelectMultiple`、日期日历及菜单下拉不再混用矩形系统边缘；
- `ModernMenuStrip`、`ModernToolStrip`：保留原生 Item、Overflow、快捷键、MDI、Designer 和 UIA；它们是既有 WinForms/Designer 界面的兼容层，新业务命令优先使用 `ModernCommandBar`。

第二批深化已统一以下契约：

- 输入、数字、可编辑 Combo、Select、Switch、Slider、日期、时间和持续时间提供 `ReadOnly`；ReadOnly 只阻止用户事务，程序赋值和绑定更新仍有效；
- `Changed` 表示值确实变化，可来自用户、程序或绑定；`Committed` 只表示用户完成有效编辑，不表示设备写入或业务命令成功；
- 默认绑定属性和替代值属性提供 WinForms `Bindable` 元数据及对应 `...Changed` 事件；
- `ModernInputNumber` 支持可空值、格式字符串、千分位、前后缀、Clamp/Reject 范围策略和长按步进；
- `ModernSlider`/`ModernProgressBar` 支持水平、垂直、反向、RTL，以及刻度或自定义显示文本；
- Tree 勾选传播可选 Independent、Descendants、AncestorsAndDescendants；ListBox、ListView、TreeView、DataGridView 提供双轴现代滚动 Chrome 和 RTL 沟槽；
- `ModernButton`/`ModernCommand` 支持调用方拥有的自定义 `Image`。

第三批 `ModernDropDownButton`、`ModernSplitButton`、公开 `ModernCalendar` 和 `ModernColorPicker` 暂缓，后续仅在真实业务页面证明无法安全组合时再决定。

## 命令、反馈与时间输入

- `ModernCommand` 集中声明文本、图标、快捷键、可见性、选中状态、`CanExecute` 与执行委托；`ModernButton`、`ModernContextMenu` 和自适应 `ModernCommandBar` 共享同一命令实例；CommandBar 公开 `OverflowVisible/OverflowCommands/OpenOverflow()`；`ModernCommandManager` 在窗体范围内统一路由快捷键、保护文本输入并按配置拒绝重复快捷键。
- `ModernAlert` 用于页面内持续反馈；`ModernMessage` 提供宿主顶部消息队列；`ModernNotification` 提供右下角通知队列并跟随 Owner 移动。二者支持淡入淡出、位移动画、长文本自动换行与测高、圆角透明宿主、跨屏 DPI 重排、主题同步、重复内容合并、最大可见数量，以及 Hover/Owner 最小化期间保留剩余关闭时间；当 Owner 可用高度不足时会优先释放最早反馈，避免队列越界。`ModernNotificationOptions` 可绑定共享 `ModernCommand` 操作，并选择操作后是否关闭通知。可通过 `ModernUiSettings.MergeDuplicateFeedback`、`MaximumVisibleMessages` 和 `MaximumVisibleNotifications` 调整队列策略；`ModernDialog` 提供同步与异步确认对话框；`ModernEmptyState` 统一空数据占位。
- `ModernDatePicker` 保留原生日期编辑和 UIA，并使用统一 `ModernPopupController` 展开托管 Light/Dark 日历；支持可空日期、范围限制、禁用日期灰显、方向键、PageUp/PageDown、Home/End、屏幕边缘翻转和 Owner 生命周期；`ModernDateRangePicker` 保证起止日期有序。
- `ModernTimePicker` 使用一天内 `TimeSpan`；`ModernDurationInput` 专门使用 `TimeSpan` 表达持续时间，并支持毫秒、秒、分钟、小时和天单位。
- `ModernSelectMultiple.DisplayMode = Tags` 可显示带图标的可移除标签，并通过 `MaxVisibleTags` 合并超出项。
- `ModernValidationProvider` 集中设置验证状态、显示错误气泡并定位验证结果，也可通过 `SetAsyncValidator`/`ValidateAsync` 执行可取消异步验证；`ModernValidationSummary` 公开可执行的错误 UIA 子节点。
- `ModernAlert` 使用 Alert 角色并发布 `EVENT_OBJECT_LIVEREGIONCHANGED`；`ModernCommandBar` 公开当前可见命令及 Overflow UIA 子节点；`ModernDialog` 关闭后恢复原焦点。

## Popup 与验证

`ModernPopupController` 集中管理自绘 Popup 的多屏边缘翻转、DPI、Escape、外部点击、Owner 失活/移动/滚动、动画取消和资源释放；同一 UI 线程只保留一个活动的托管 Popup。`ModernSelect` 与 `ModernSelectMultiple` 已统一接入，`ModernComboBox` 与 `ModernContextMenu` 继续保留原生 Win32 Popup 行为。

输入类控件通过 `IModernValidationControl` 暴露统一的 `ValidationState` 与 `ValidationMessage`。`ModernInput`、`ModernTextArea`、`ModernInputNumber`、`ModernSelect`、`ModernSelectMultiple`、`ModernComboBox` 和 `ModernSlider` 支持 None、Success、Warning、Error 语义边框；Input/ComboBox 的旧 `HasError` 属性继续作为 Error 状态兼容入口。

## 选择控件数据绑定

`ModernSelect` 和 `ModernSelectMultiple` 支持 WinForms 常用的数据成员接口：

```csharp
var cameras = new BindingList<CameraOption>
{
    new(1, "Line Camera"),
    new(2, "Inspection Camera")
};

singleSelect.DisplayMember = nameof(CameraOption.Name);
singleSelect.ValueMember = nameof(CameraOption.Id);
singleSelect.DataSource = cameras;
singleSelect.SelectedValue = 2;

multipleSelect.DisplayMember = nameof(CameraOption.Name);
multipleSelect.ValueMember = nameof(CameraOption.Id);
multipleSelect.DataSource = cameras;
multipleSelect.SetSelectedValues([1, 2]);
```

- `DisplayMember` 仅控制显示文本，不参与项目身份判断。
- `ValueMember` 决定 `SelectedValue` / `SelectedValues` 的内容。
- 两种成员均支持点分隔的嵌套路径，例如 `Metadata.Name`；成员名按不区分大小写的 `TypeDescriptor` 规则解析。
- `ImageList` 配合 `ImageKeyMember` 或 `ImageIndexMember` 为选中面和 Popup 行提供图标；图标成员同样支持 `Metadata.IconKey` 这类嵌套路径，键成员优先于索引成员。
- 合法路径的中间对象为 `null` 时，解析结果为 `null`；路径不存在时，在首次解析时抛出包含完整路径和失败类型的 `InvalidOperationException`。
- `ModernSelect.SelectedValue = null` 清除当前选择；找不到匹配值时 `SelectedIndex` 为 `-1`。
- 多选状态按对象引用保存；值相等但实例不同的项目可独立选择。
- `SelectedItems` / `SelectedValues` 按数据源列表顺序返回。
- `SetSelectedItems` / `SetSelectedValues` 为批量同步接口，不触发 `SelectionChanged`。
- 多选的 `ValueMember` 可以返回 `null`；每个传入值（包括 `null`）最多匹配一个尚未选中的项目。
- 更换多选数据源会清空选择；如果原来存在选择，会触发一次 `SelectionChanged`。
- 单选和多选下拉均支持再次点击锚点收起 Popup。
- 单选/多选 Popup 的滚动指示器与页面滚动条共用胶囊几何、最小长度和透明度规则。
- `BindingList<T>` / `BindingSource` 的运行时变化会同步到已展开的 Popup。
- 设置 `DataSource = null` 会解除旧数据源订阅，之后可以重新通过 `Items` 添加手工项目。

`ModernInput` 的无障碍入口直接返回内部原生 TextBox provider，不再额外暴露一个重复的外层文本节点。文本模式、焦点、只读和密码语义继续由 Windows 原生编辑器提供。

`ModernTextArea` 在相同原生文本 seam 上增加多行语义，支持 `Lines`、`AcceptsReturn`、`AcceptsTab`、`WordWrap`、`ScrollBars`、只读 `ScrollPosition` 和 `ScrollToCaret()`。表面使用统一的横向/纵向胶囊滑块：普通滚轮优先纵向，无纵向范围时自动横向，`Shift + 滚轮` 强制横向，到达当前轴边界后再交给外层页面。它适用于说明、日志和 JSON 等普通多行文本；C# 脚本仍由专用 Roslyn 编辑器承载。

`ModernComboBox` 用于“允许自由输入，同时提供候选项”的场景，默认启用 `SuggestAppend` 和 `ListItems` 自动完成。它支持 `Items`、`DataSource`、`DisplayMember`、`ValueMember`、`SelectedValue` 以及 `BindingList<T>` 动态同步；`ImageList` 配合 `ImageKeyMember` / `ImageIndexMember` 可在选中候选项、禁用面和 OwnerDraw 下拉行中绘制图标；`SelectedValue = null` 明确清除选择。纯选择场景仍应使用 `ModernSelect`。

`ModernRadioButton` 在同一父容器及相同 `GroupName` 内互斥，支持再次点击/Space/Enter 取消当前选择、方向键循环、唯一 TabStop 和 RadioButton 无障碍状态。`ModernSegmentedControl` 用于工具模式等紧凑互斥选择，项目支持文本、矢量图标、业务值和 Disabled 状态，并将每个分段暴露为独立无障碍 RadioButton 子节点。

`ModernTabControl` 保留原生 `TabPages`、`ImageList`、`TabPage.ImageIndex/ImageKey`、Visual Studio Designer、Ctrl+Tab 和 PageTabList UIA provider，接管标签头图文绘制、页面主题，并让选中背景、文字状态和底部指示条在同一缓动帧中切换。现有 `TabControl` 可以直接替换类型，不需要改变页面组合代码。

`ModernTreeView` 继承原生 `TreeView`，保留 `Nodes`、展开/折叠、方向键、CheckBoxes、ImageList、滚动、Designer 和 Outline UIA provider；现代层接管行背景、Hover、选中、焦点、DPI 行高、独立 Chevron 槽位和 CheckBoxes 绘制，并与 `ModernCheckbox` 共用 renderer。父节点勾选会同步全部后代，子节点变化会通过 `GetNodeCheckState()` 汇总为 Checked、Unchecked 或 Indeterminate。

`ModernListBox` 继承原生 `ListBox`，保留 `Items`、数据绑定、单选/多选、键盘、滚动、Designer 和 List/ListItem UIA provider；现代层提供 DPI 行高、圆角 Hover/Selected 状态及 Disabled 视觉。

`ModernListView` 继承原生 `ListView`，保留 `Columns`、`Items/SubItems`、VirtualMode、CheckBoxes、ImageList、键盘、Designer 和 List/ListItem UIA provider；现代层绘制柔性圆角外轮廓、Details 表头、业务文字色、Hover/Selected 行、DPI 行高和统一复选框，并清理最后一列后的剩余区域。启用胶囊滚动 Chrome 后会移除 Win32 `WS_HSCROLL/WS_VSCROLL` 非客户区样式，避免现代滑块与旧方形滚动条同时出现。

`ModernDataGridView` 继承原生 `DataGridView`，保留绑定、编辑、排序、增删行、VirtualMode、列类型、Designer 和 Table UIA provider；现代层映射柔性圆角外轮廓、表头、交替行、网格线、选中与 DPI 尺寸。需要统一圆角下拉体验时使用 `ModernDataGridViewComboBoxColumn`：非编辑状态由表格绘制，编辑状态通过 `IDataGridViewEditingControl` adapter 复用 `ModernSelect` 及其圆角弹层，并使用无额外边框的嵌入式单元格外观，避免点击前后样式跳变；现代下拉单元格首次点击即进入编辑并展开，再次点击锚点会收起；标准 `DataGridViewComboBoxColumn` 仍保留原生 editor 兼容行为。集合字段使用 `ModernDataGridViewMultiSelectColumn`：候选项支持 `DataSource/DisplayMember/ValueMember`，编辑时复用 `ModernSelectMultiple` 的复选弹层，非编辑状态显示标签；业务列可显式设置 `ShowTagRemoveButtons = true` 启用可点击 `×`，并通过 `MaxVisibleTags`、`AutoFitVisibleTags`、`MaximumTagWidth` 控制固定数量、按可用空间铺排和单标签宽度；增删选择时保留原配置值的顺序。DataGridView 编辑器使用框架传入的 `rowIndex` 读取共享行值，确保枚举列和多选列第一次点击即可稳定展开。

## Visual States 全页检查

不能只截取 Gallery 首屏或新增控件局部。以下命令会在固定滚动偏移 `0/520/1040/1560/2080` 分别捕获 Light/Dark，共生成 10 张真实窗口截图：

在 `DP.WorkFlow` 目录执行：

```powershell
$s = Get-Content -Raw tools/capture-visual-states.ps1
& ([scriptblock]::Create($s)) -RepositoryRoot (Get-Location)
# 双屏环境可明确指定目标显示器：
& ([scriptblock]::Create($s)) -RepositoryRoot (Get-Location) -TargetDpi 144
```

使用 ScriptBlock 是为了兼容禁止直接执行 `.ps1` 的开发机策略。

默认输出到 `artifacts/visual-states/`。捕获线程会切换为 Per-Monitor-V2，避免 PowerShell 的 96 DPI 虚拟化截断位图；固定滚动偏移按逻辑像素传入并由 Gallery 按实际 `DeviceDpi` 换算。每次变更组合输入、列表、树、页签或状态矩阵布局后，必须逐张检查裁切、系统浅色残留、子控件穿透、边框断裂、焦点状态和滚动条占位；单元测试通过不能替代这项检查。可用 `--scroll=<offset>` 单独启动指定分段。

## DPI 视觉检查

在 Windows 显示设置中依次切换 125%、150%、200%，每次重新启动浅色和深色视觉状态窗口。窗口标题区域会显示实际 `DeviceDpi`，应分别为：

- 125% → 120 DPI
- 150% → 144 DPI
- 200% → 192 DPI

每档检查输入文字基线、控件边框、图标、Popup 锚点、列表行高、焦点描边以及窗口边缘是否存在 1px 裂缝。截图只有在标题中的 DPI 与目标档位一致时才作为有效基线。

当前人工验收状态：

- 150% / 144 DPI：已检查浅色、深色、键盘焦点、单选/多选 Popup、主 Gallery、快速连续主题切换，以及 Date/Time/DateRange/Duration 的嵌套缩放边界。
- 125% / 120 DPI：待检查。
- 200% / 192 DPI：待检查。

Gallery 展示：

- Light/Dark 主题切换；
- Primary、Default、Danger、Loading 按钮；
- 带动画的 Switch；
- 保留原生输入法能力的 ModernInput 和 ModernTextArea；
- ModernInputNumber（竖向步进按钮、范围校验和错误气泡）；
- ModernToolTip（设计器扩展属性与即时气泡提示）；
- ModernComboBox（可编辑候选和自动完成）；
- ModernSelect（单选）；
- ModernSelectMultiple（复选下拉多选）；
- ModernCheckbox、ModernRadioButton 和 ModernSegmentedControl；
- 保留原生 TabPages 和 UIA 的 ModernTabControl；
- 保留原生 Nodes、键盘和 Outline UIA 的 ModernTreeView；
- 保留原生数据绑定、多选和 List UIA 的 ModernListBox；
- 保留原生 Details、VirtualMode 和 List UIA 的 ModernListView；
- 保留原生绑定、编辑、列类型和 Table UIA 的 ModernDataGridView；
- ModernProgressBar（确定/不确定进度、百分比和 Success/Warning/Error 语义状态）；
- ModernSpinner（可释放动画计时器、文本和 UIA Animation 语义）；
- ModernBadge（数字、溢出、零值、文本和状态圆点）；
- ModernSlider（decimal 范围、步进、鼠标/键盘和 `ValueChanged`/`ValueCommitted` 分离）；
- ModernContextMenu（保留原生菜单项、快捷键、子菜单和 UIA 的主题 adapter）；
- ModernCollapsiblePanel（内容组合、矢量 `HeaderIcon`、键盘展开/折叠和 Grouping UIA）；
- ModernStatusBar（保留 StatusStrip 项模型，支持弹性项和主题）；
- ModernPagination（PageIndex、PageSize、TotalCount、首尾/前后页和 PageChanged）；
- ModernScrollView（6px 胶囊滑块、8px Hover/拖动态；复杂原生子控件页面由 WinForms `ScrollableControl` 的显示矩形负责位移，隐藏原生非客户区滚动条并由现代滑块直接设置位置；默认启用 `UseCompositedScrolling`，仅在现代滑块实时拖动期间临时启用视口级 Win32 子窗口双缓冲合成，空闲时自动关闭以避免原生输入控件触发整页持续重绘；可关闭 `LiveScrollDuringThumbDrag` 使用松开后提交的兼容模式；内层有可用滚动范围时优先滚动内层，无范围或到达边界后自动继续滚动外层页面）；
- TreeView、ListBox、ListView 和 DataGridView 共享方向感知的滚轮链路，同样支持向原生 `AutoScroll` 页面传递滚轮；
- ModernSplitter（水平/垂直可拖动分隔容器）；
- 属性分类、搜索、单位、范围、只读值、验证事件和按钮型自定义编辑器；
- 实时交互事件日志。

完整架构说明见：`docs/WinForms_UI控件框架设计.md`。
