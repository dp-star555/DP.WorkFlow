# WinForms UI 控件框架设计

> 状态：初版设计决策（当前代码结构以 `src/Platform/Desktop/ARCHITECTURE.md` 为准）  
> 适用范围：`DP.WorkFlow`、通用 WinForms 工具、DP.WorkFlow 编辑器、Vision 工业应用  
> 目标框架：`.NET 8.0-windows`  
> 设计参考：Ant Design 设计语言、AntdUI 的实现原理、WinForms 原生交互能力

---

## 1. 背景

当前项目已经创建了独立的 `ModernPropertyGrid.WinForms`，并通过 `TypeDescriptor`、`ICustomTypeDescriptor` 和编辑器 Provider 接入 DP.WorkFlow。初版解决了属性发现、分类、搜索、基础编辑和业务扩展问题，但表现层仍主要使用原生 `TextBox`、`ComboBox`、`NumericUpDown` 和 `CheckBox`，视觉一致性受到 WinForms 系统控件限制。

AntdUI 的良好观感并不来自简单换色，而来自以下完整体系：

1. 统一的语义化 Design Token；
2. 统一的控件状态模型；
3. 自定义绘制的背景、边框、图标和反馈层；
4. DPI 感知的尺寸体系；
5. 集中的动画机制；
6. SVG 图标和一致的视觉语言；
7. 自定义弹出层；
8. 对 Hover、Pressed、Focused、Disabled、Error、Loading 等状态的完整设计。

本项目不计划复制整个 AntdUI，也不计划让基础 PropertyGrid 绑定到某个第三方 UI 库，而是基于这些原理逐步建立自己的 WinForms UI 框架。

---

## 2. 目标

### 2.1 产品目标

构建一套适用于桌面工具和工业软件的现代 WinForms UI 框架，优先服务以下场景：

- 通用 PropertyGrid；
- DP.WorkFlow 节点参数编辑；
- Vision 参数配置；
- 工业设备设置；
- 脚本、集合和绑定编辑入口；
- 深色和浅色桌面工具界面。

### 2.2 技术目标

- 基础框架不依赖 DP.WorkFlow、Vision 或 AntdUI；
- 支持 Ant Design 风格，但保留产品自定义主题能力；
- 使用语义 Token，业务控件中不散布硬编码颜色；
- 支持系统 DPI 和字体缩放；
- 保留 WinForms 输入法、剪贴板和键盘能力；
- 动画统一调度，不为每个动画创建后台线程；
- 支持设计器和运行时创建；
- 支持 Provider、Adapter 等明确的扩展 seam；
- 控件可单独使用，不要求宿主必须是 DP.WorkFlow；
- PropertyGrid 成为第一批控件的真实验证场景。

### 2.3 非目标

第一阶段不追求：

- 一次性覆盖 AntdUI 的全部控件；
- 完全自绘文本输入和中文输入法；
- 立即实现 Table、Tree、Calendar、Docking 和 Chart；
- 像素级复制 Ant Design Web 组件；
- 用一个巨型基类承载所有控件能力；
- 将业务模型直接写入 UI 框架。

---

## 3. 总体依赖方向

```text
DP.WorkFlow.Vision.UI.WinForms
            │
            ▼
DP.WorkFlow.UI.WinForms
  DP.WorkFlow PropertyGrid Adapter
            │
            ▼
ModernUI.WinForms
  Theme、Canvas、DPI、Animation、基础控件、Popup、PropertyGrid
            │
            ▼
System.Windows.Forms / System.Drawing
```

依赖规则：

1. `ModernUI.WinForms` 包含通用 PropertyGrid，但不引用任何业务程序集；
2. PropertyGrid 保留 `ModernPropertyGrid.WinForms` namespace，不再使用独立程序集；
3. `DP.WorkFlow.UI.WinForms` 只通过 Adapter 和 Provider 接入；
4. Vision 专用编辑器留在 `DP.WorkFlow.Vision.UI.WinForms`；
5. 依赖只能从业务层指向通用层，禁止反向依赖。

---

## 4. 项目建议

### 4.1 第一阶段

当前实现集中于：

```text
src/Platform/Desktop/ModernUI.WinForms/
```

PropertyGrid 位于 `ModernUI.WinForms/PropertyGrid/`，平台中立本地化基础设施位于 `src/Platform/Localization/ModernUI.Localization/`。

当前结构：

```text
ModernUI.WinForms/
├── Core/
│   ├── ModernControl.cs
│   ├── ControlVisualState.cs
│   ├── InteractionState.cs
│   ├── DpiScale.cs
│   └── UiDispatcher.cs
├── Drawing/
│   ├── ICanvas.cs
│   ├── GdiCanvas.cs
│   ├── DrawingQuality.cs
│   ├── GeometryExtensions.cs
│   └── RoundedPathCache.cs
├── Theming/
│   ├── ModernTheme.cs
│   ├── ThemeContext.cs
│   ├── ColorTokens.cs
│   ├── SizeTokens.cs
│   ├── TypographyTokens.cs
│   └── MotionTokens.cs
├── Animation/
│   ├── AnimationClock.cs
│   ├── AnimationHandle.cs
│   ├── Transition.cs
│   └── Easing.cs
├── Controls/
│   ├── ModernPanel.cs
│   ├── ModernButton.cs
│   ├── ModernSwitch.cs
│   ├── ModernInput.cs
│   ├── ModernInputNumber.cs
│   ├── ModernSelect.cs
│   ├── ModernSelectMultiple.cs
│   ├── ModernCheckbox.cs
│   ├── ModernDivider.cs
│   └── ModernIcon.cs
├── Overlays/
│   ├── PopupHost.cs
│   ├── PopupPlacement.cs
│   └── ModernToolTip.cs
└── Icons/
    ├── IconDefinition.cs
    ├── IconRegistry.cs
    └── SvgIconRenderer.cs
```

### 4.2 是否拆分更多程序集

第一阶段只建立一个 `ModernUI.WinForms`，避免过早拆分。只有出现以下真实需求时再拆：

- SVG 实现需要被其他 UI 平台替换；
- Popup 系统可被多个独立包使用；
- 主题模型需要在 WPF/WinForms 间共享；
- 基础控件要独立发布 NuGet 包。

“一种实现对应一个 interface”通常只是增加间接层。只有至少存在两个真实 Adapter 时才建立外部 seam。

---

## 5. 核心模块

### 5.1 `ModernControl`

`ModernControl` 是框架的基础 Control，但必须保持小而稳定。

职责：

- 设置 WinForms 绘制样式；
- 提供主题上下文；
- 提供 DPI 比例；
- 维护通用交互状态；
- 定义固定绘制管线；
- 提供安全的重绘调度；
- 管理动画句柄和资源释放。

不负责：

- Badge；
- 文件拖放；
- Loading 业务语义；
- 表单验证；
- Popup 内容；
- PropertyGrid 属性发现；
- DP.WorkFlow 会话。

建议 interface：

```csharp
public abstract class ModernControl : Control
{
    public ThemeContext ThemeContext { get; }

    protected float DpiScale { get; }

    protected ControlVisualState VisualState { get; }

    protected sealed override void OnPaint(PaintEventArgs e);

    protected virtual void RenderBackground(ICanvas canvas, Rectangle bounds);

    protected abstract void RenderContent(ICanvas canvas, Rectangle bounds);

    protected virtual void RenderOverlay(ICanvas canvas, Rectangle bounds);
}
```

固定绘制顺序：

```text
Background
→ Border/Shadow
→ Content
→ Interaction Overlay
→ Focus Ring
→ Validation/Error Overlay
```

### 5.2 绘制样式

构造函数统一启用：

```csharp
SetStyle(
    ControlStyles.UserPaint |
    ControlStyles.AllPaintingInWmPaint |
    ControlStyles.OptimizedDoubleBuffer |
    ControlStyles.ResizeRedraw |
    ControlStyles.SupportsTransparentBackColor,
    true);
```

禁止在 `OnPaint()` 中静默吞掉异常。开发阶段绘制异常必须可见；发布阶段如需隔离，应记录日志并显示安全降级内容。

---

## 6. Canvas 绘制模块

### 6.1 目标

`ICanvas` 用于统一高质量 GDI+ 设置和常用绘制操作，减少每个控件重复配置。

建议 interface：

```csharp
public interface ICanvas : IDisposable
{
    SizeF MeasureText(string text, Font font);

    void DrawText(string text, Font font, Color color, RectangleF bounds, TextAlignment alignment);

    void FillRectangle(Color color, RectangleF bounds);

    void FillRoundedRectangle(Color color, RectangleF bounds, float radius);

    void DrawRoundedRectangle(Color color, float width, RectangleF bounds, float radius);

    void DrawSvg(IconDefinition icon, RectangleF bounds, Color color);

    IDisposable PushClip(RectangleF bounds);

    IDisposable PushTransform(Matrix matrix);
}
```

`GdiCanvas` 内部统一设置：

- `SmoothingMode.AntiAlias`；
- `PixelOffsetMode.HighQuality`；
- `InterpolationMode.HighQualityBicubic`；
- 可配置的 `TextRenderingHint`；
- 画笔和路径的正确释放。

### 6.2 几何缓存

圆角路径创建频繁时会产生较多短生命周期 GDI 对象。只缓存纯几何描述或有限尺寸的路径模板，不缓存绑定具体 `Graphics` 的资源。

缓存必须满足：

- 有容量上限；
- DPI 改变时失效；
- Theme 改变不必失效；
- Dispose 责任明确。

---

## 7. Design Token 与主题

### 7.1 语义 Token

禁止控件直接表达“这个地方是蓝色”，应该表达“这个地方是 Primary”。

颜色 Token：

```csharp
public sealed record ColorTokens
{
    public required Color Primary { get; init; }
    public required Color PrimaryHover { get; init; }
    public required Color PrimaryActive { get; init; }
    public required Color PrimaryBackground { get; init; }
    public required Color PrimaryBorder { get; init; }

    public required Color Text { get; init; }
    public required Color TextSecondary { get; init; }
    public required Color TextTertiary { get; init; }
    public required Color TextDisabled { get; init; }

    public required Color Background { get; init; }
    public required Color Container { get; init; }
    public required Color Elevated { get; init; }
    public required Color Control { get; init; }
    public required Color ControlHover { get; init; }

    public required Color Border { get; init; }
    public required Color BorderSecondary { get; init; }

    public required Color Success { get; init; }
    public required Color Warning { get; init; }
    public required Color Error { get; init; }
    public required Color Information { get; init; }
}
```

尺寸 Token：

```csharp
public sealed record SizeTokens
{
    public int ControlHeightSmall { get; init; } = 24;
    public int ControlHeight { get; init; } = 32;
    public int ControlHeightLarge { get; init; } = 40;

    public int RadiusSmall { get; init; } = 4;
    public int Radius { get; init; } = 6;
    public int RadiusLarge { get; init; } = 8;

    public int SpaceXSmall { get; init; } = 4;
    public int SpaceSmall { get; init; } = 8;
    public int Space { get; init; } = 12;
    public int SpaceLarge { get; init; } = 16;
    public int SpaceXLarge { get; init; } = 24;
}
```

字体 Token：

```csharp
public sealed record TypographyTokens
{
    public required Font Body { get; init; }
    public required Font BodyStrong { get; init; }
    public required Font Caption { get; init; }
    public required Font Title { get; init; }
    public required Font Monospace { get; init; }
}
```

动画 Token：

```csharp
public sealed record MotionTokens
{
    public TimeSpan Fast { get; init; } = TimeSpan.FromMilliseconds(120);
    public TimeSpan Normal { get; init; } = TimeSpan.FromMilliseconds(180);
    public TimeSpan Slow { get; init; } = TimeSpan.FromMilliseconds(260);
}
```

### 7.2 Theme Context

不使用只能全局切换的静态主题作为唯一方式。主题解析顺序：

```text
Control.LocalTheme
→ Parent.ThemeContext
→ Form.ThemeContext
→ ModernTheme.Default
```

这样支持：

- 同一进程多个不同主题窗口；
- 局部主题预览；
- 设计器预览；
- 单元测试使用固定主题；
- 运行时切换。

主题改变后：

```text
ThemeContext.Changed
→ 控件清理主题相关缓存
→ Invalidate
→ 必要时 PerformLayout
```

只有尺寸或字体 Token 改变时才重新布局，颜色变化只重绘。

---

## 8. DPI 与布局

### 8.1 DPI 原则

所有以下尺寸都必须经过 DPI 缩放：

- 控件高度；
- Padding；
- 圆角；
- 边框宽度；
- 图标尺寸；
- Focus Ring；
- Popup 偏移；
- 阴影范围。

建议统一方法：

```csharp
protected int Scale(int logicalPixels);
protected float Scale(float logicalPixels);
protected Padding Scale(Padding logicalPadding);
```

不要让业务控件重复读取屏幕 DPI。

### 8.2 DPI 变化

`OnDpiChangedBeforeParent` 或父窗口 DPI 变化时：

1. 清理 DPI 缓存；
2. 清理字体和几何缓存；
3. 重新计算 PreferredSize；
4. 重新布局；
5. 重绘。

### 8.3 间距规范

以 4px 为最小单位，主要使用：

```text
4 / 8 / 12 / 16 / 24 / 32
```

PropertyGrid 默认密度：

- 工业桌面标准模式：36～40px 行高；
- 紧凑模式：30～34px；
- 触屏模式：44～48px；
- 标签和值间最小间距：8px；
- 分类之间间距：8～12px。

---

## 9. 控件状态模型

统一状态：

```csharp
[Flags]
public enum ControlVisualState
{
    Normal = 0,
    Hovered = 1,
    Pressed = 2,
    Focused = 4,
    Disabled = 8,
    Selected = 16,
    Error = 32,
    Loading = 64
}
```

状态优先级：

```text
Disabled
> Error
> Pressed
> Focused
> Hovered
> Selected
> Normal
```

状态不能只通过颜色表达。例如：

- Error：错误边框 + 错误图标 + 文字说明；
- Disabled：透明度/灰度 + 禁止交互；
- Focused：Focus Ring；
- Loading：Spinner + 禁止重复提交；
- Selected：背景或指示条。

交互状态由基础模块管理：

```text
OnMouseEnter  → Hovered
OnMouseLeave  → 清除 Hovered
OnMouseDown   → Pressed
OnMouseUp     → 清除 Pressed
OnGotFocus    → Focused
OnLostFocus   → 清除 Focused
Enabled=false → Disabled
```

控件只负责根据状态解析视觉样式。

---

## 10. 动画系统

### 10.1 统一动画时钟

使用 UI 线程上的集中调度：

```text
System.Windows.Forms.Timer
→ AnimationClock.Tick
→ 更新所有活动 Transition
→ 只重绘受影响控件
→ 移除已完成动画
```

禁止每个 Hover、Switch、Loading 都创建独立后台线程。

### 10.2 动画 interface

```csharp
public sealed class AnimationClock
{
    public AnimationHandle Start(
        Control owner,
        float from,
        float to,
        TimeSpan duration,
        Easing easing,
        Action<float> update,
        Action? completed = null);
}
```

要求：

- 控件 Dispose 后自动取消；
- 新动画可以从当前值接续；
- 用户反向操作时动画可以立即反转；
- 全局可以关闭动画；
- 动画不能阻塞输入；
- 更新只发生在 UI 线程；
- 默认持续时间 120～260ms。

### 10.3 第一阶段需要的动画

- Button Hover/Pressed 颜色过渡；
- Switch Thumb 位置过渡；
- Input Focus Border 过渡；
- Select Popup 淡入/轻微位移；
- Collapse 展开可延后实现。

不做装饰性入场动画。

---

## 11. 基础控件实现策略

### 11.1 `ModernPanel`

能力：

- Container 背景；
- Border；
- Radius；
- 可选低层级阴影；
- Theme Context 宿主；
- 不自动引入复杂布局逻辑。

### 11.2 `ModernButton`

能力：

- Primary、Default、Text、Danger 类型；
- Small、Middle、Large 尺寸；
- 图标和文字；
- Hover、Pressed、Focused、Disabled、Loading；
- 一个界面只突出一个 Primary 操作。

第一版可以完全自绘，因为 Button 不涉及复杂输入法。

### 11.3 `ModernSwitch`

能力：

- Checked/Unchecked；
- Hover、Focus、Disabled；
- Thumb 平移动画；
- 可选 CheckedText/UncheckedText；
- Loading 后续实现。

第一版可以完全自绘。

### 11.4 `ModernInput`

采用混合实现：

```text
ModernInput（自绘容器）
├── 背景、边框、圆角、Focus Ring
├── Prefix/Suffix/Clear 图标
├── Error 状态
└── 原生 Borderless TextBox
```

原生 `TextBox` 负责：

- 中文输入法；
- 光标；
- 选择；
- 剪贴板；
- 撤销；
- Unicode；
- 密码字符；
- 键盘行为；
- 无障碍基础能力。

容器负责：

- 布局；
- 现代外观；
- 状态；
- 辅助图标；
- 错误提示；
- 主题。

第一阶段禁止完全自绘文本编辑。

### 11.5 `ModernInputNumber`

建议组合：

```text
ModernInputNumber
├── ModernInput
├── Decrease Button
└── Increase Button
```

支持：

- `decimal` 内部编辑值；
- Minimum、Maximum、Increment；
- DecimalPlaces；
- Unit；
- 鼠标滚轮可配置；
- 长按连续增减后续实现；
- 失焦提交；
- 输入中允许临时不完整文本，如 `-`、`.`。

不要直接依赖系统 `NumericUpDown` 的外观。

### 11.6 `ModernSelect`

由以下模块组成：

```text
ModernSelect
├── 输入框式选择器
├── 下拉箭头
├── PopupHost
└── SelectList
```

第一阶段能力：

- 单选；
- 键盘上下导航；
- Enter 确认；
- Escape 关闭；
- 当前项高亮；
- Popup 自动选择上下方向；
- 屏幕边缘约束；
- DPI 感知。

搜索、多选、虚拟列表属于后续阶段。

---

## 12. Popup 系统

Popup 是 Select、Tooltip、ColorPicker、DatePicker 的共同基础，应作为深模块隐藏窗口定位和生命周期复杂度。

外部 interface 应保持较小：

```csharp
public sealed class PopupHost
{
    public PopupSession Show(Control anchor, Control content, PopupOptions options);
}
```

内部负责：

- 计算屏幕工作区；
- 下方空间不足时向上展开；
- DPI 和多显示器；
- 点击外部关闭；
- Escape 关闭；
- Anchor 移动时重定位；
- Owner 窗口关闭时销毁；
- Focus 恢复；
- 阴影和圆角；
- 进入/退出动画。

第一版优先使用无边框普通 Form，不立即采用 Layered Window。只有普通 Form 无法满足透明阴影或性能需求时再引入 Win32 Layered Window。

---

## 13. SVG 和图标

原则：

- 使用统一图标集；
- 不用 Emoji 充当功能按钮图标；
- 图标颜色来自 Theme Token；
- 图标尺寸经过 DPI 缩放；
- 图标按钮必须有 `AccessibleName` 或 Tooltip。

建议：

```csharp
public sealed record IconDefinition(string Key, string SvgData);
```

图标注册：

```csharp
IconRegistry.Register("search", svg);
IconRegistry.Register("clear", svg);
IconRegistry.Register("chevron-down", svg);
```

如果第一阶段不实现完整 SVG，可以先使用预渲染矢量路径或高 DPI PNG，但 interface 保持稳定。

---

## 14. PropertyGrid 与 UI 框架的结合

### 14.1 PropertyGrid 的职责

`ModernPropertyGrid.WinForms` 继续负责：

- `SelectedObject`；
- `TypeDescriptor` 属性发现；
- 标准 Attribute；
- 搜索、分类和排序；
- 属性描述区；
- 属性提交；
- 验证事件；
- 编辑器 Provider 注册；
- DP.WorkFlow 之外的通用使用方式。

它不负责：

- DP.WorkFlow 会话；
- Vision ROI；
- Theme 的底层绘制实现；
- TextBox 输入法实现；
- Popup 窗口管理。

### 14.2 编辑器替换

```text
原生 TextBox       → ModernInput
原生 NumericUpDown → ModernInputNumber
原生 ComboBox      → ModernSelect
原生 CheckedListBox → ModernSelectMultiple
原生 CheckBox      → ModernCheckbox
原生 Button        → ModernButton
```

编辑器解析顺序保持：

```text
1. 显式 PropertyEditorKey
2. 注册的 IPropertyEditorProvider（按 Priority）
3. ReadOnly
4. Boolean
5. Enum
6. Number
7. TypeConverter StandardValues
8. Text/Fallback
```

### 14.3 PropertyGrid 布局

```text
┌─────────────────────────────────────────┐
│ [搜索属性]                 24 项 [选项] │
├─────────────────────────────────────────┤
│ ▼ 常规                              5   │
│ 名称               [Camera 1          ] │
│ 启用               [● 已启用]           │
│ 模式               [连续采集         ▼] │
│                                         │
│ ▼ 采集                              6   │
│ 曝光时间           [1500.000      ] μs │
│ 增益               [4.500         ] dB │
├─────────────────────────────────────────┤
│ 曝光时间 · Double                       │
│ 控制传感器的曝光持续时间。               │
│ 有效范围：10～100000 μs                 │
└─────────────────────────────────────────┘
```

### 14.4 PropertyGrid 行状态

每一行支持：

- Normal；
- Hovered；
- Selected；
- Editing；
- ReadOnly；
- Modified；
- Error；
- Hidden。

错误不能只显示在底部说明区。行内至少包含：

- 错误边框；
- 错误图标；
- Tooltip 或辅助文字；
- 保存时定位到第一个错误项。

### 14.5 刷新策略

当前初版在较多场景中会完整 `Rebuild()`。后续区分：

```text
ValueChanged(key)      → 更新单个编辑器
MetadataChanged(key)   → 更新单行布局
VisibilityChanged(keys)→ 增删受影响行
SchemaChanged          → 完整重建并恢复状态
SelectedObjectChanged  → 完整重建
ThemeChanged           → 重绘；尺寸变化时重新布局
```

完整重建必须保存：

- 搜索文本；
- 分类折叠状态；
- 滚动位置；
- 当前选中属性；
- 标签列宽；
- 可恢复时的 Focus。

属性超过 100 项后再评估行虚拟化，不在第一阶段提前实现。

---

## 15. DP.WorkFlow 适配

DP.WorkFlow 通过标准 .NET seam 接入：

```text
WorkflowPropertyInspectorModel
→ WorkflowPropertyObjectAdapter : ICustomTypeDescriptor
→ WorkflowPropertyDescriptor : PropertyDescriptor
→ ModernPropertyGrid.SelectedObject
```

通用 PropertyGrid 不知道：

- `WorkflowDesignerSession`；
- `WorkflowInput<T>`；
- 绑定分析；
- 节点撤销；
- 输出端口；
- ROI。

DP.WorkFlow 自定义编辑器：

```text
WorkflowInputPropertyEditorProvider
WorkflowPathPropertyEditorProvider
WorkflowScriptPropertyEditorProvider
WorkflowStructuredPropertyEditorProvider
```

Vision 自定义编辑器：

```text
VisionRoiPropertyEditorProvider
VisionPointPropertyEditorProvider
VisionRectPropertyEditorProvider
VisionRangePropertyEditorProvider
VisionImageSourcePropertyEditorProvider
```

业务 Provider 只能调用 `PropertyEditorContext.CommitValue()` 或业务 Adapter 提供的提交入口，不能直接绕过工作流通知和事务。

---

## 16. 验证与提交

### 16.1 提交流程

```text
用户输入
→ Editor.Parse
→ TypeConverter
→ Range/Required 等通用校验
→ PropertyValueChanging（可取消）
→ PropertyDescriptor.SetValue
→ PropertyValueChanged
→ 更新 Modified 状态
```

失败流程：

```text
转换或提交异常
→ 保留用户输入
→ 行进入 Error 状态
→ 显示原因和修复建议
→ ValidationFailed
→ 不修改源对象
```

### 16.2 提交策略

不同编辑器允许选择：

- `Immediate`：Switch、Select；
- `OnValidated`：Text、Number；
- `Explicit`：复杂对象、脚本、集合弹窗。

PropertyGrid 不负责 DP.WorkFlow 的最终 Apply 事务，但必须正确产生属性修改事件。

---

## 17. 键盘与无障碍

最低要求：

- Tab 顺序符合视觉顺序；
- Shift+Tab 反向导航；
- Enter 打开 Select 或确认；
- Escape 关闭 Popup/清除搜索；
- Space 切换 Switch；
- 上下键切换 Select；
- Ctrl+F 聚焦 PropertyGrid 搜索；
- Focus Ring 清晰可见；
- 图标按钮设置 `AccessibleName`；
- 错误不能只依赖红色；
- ReadOnly 与 Disabled 视觉和语义不同；
- 正常文本和背景满足至少 4.5:1 对比度。

完全自绘控件必须验证 `AccessibleObject`。第一阶段优先保留原生输入子控件，以降低屏幕阅读器和输入法风险。

---

## 18. 性能与资源管理

### 18.1 性能预算

- 普通 Hover 重绘目标小于 16ms；
- 搜索防抖 150～200ms；
- 动画更新频率不高于显示需要；
- 只重绘受影响区域；
- 不在每帧执行属性反射；
- 不在 `OnPaint` 创建大型 Bitmap；
- 不在每帧重复解析 SVG；
- 50 项以内无需虚拟化；
- 100 项以上进行性能测量后再决定。

### 18.2 GDI 资源

所有以下对象必须明确 Dispose：

- `Pen`；
- `Brush`；
- `GraphicsPath`；
- `Matrix`；
- `Region`；
- `Bitmap`；
- 自定义 Font。

系统共享 Font 不由控件释放。主题创建的 Font 由 Theme 生命周期统一管理。

### 18.3 控件释放

`ModernControl.Dispose` 负责：

- 取消动画；
- 解除 Theme 订阅；
- 解除全局事件；
- 释放缓存；
- 关闭所属 Popup；
- 调用基础 Dispose。

---

## 19. 设计器支持

基础控件至少支持：

- Toolbox 创建；
- `DefaultProperty`；
- `DefaultEvent`；
- `Category`；
- `Description`；
- `DefaultValue`；
- 必要时 `DesignerSerializationVisibility`；
- 设计器中不启动动画；
- 设计器中不连接业务服务；
- 设计器环境下使用稳定的默认主题。

可通过：

```csharp
LicenseManager.UsageMode == LicenseUsageMode.Designtime
```

判断设计器环境，但不要只依赖 `DesignMode`。

---

## 20. 测试策略

### 20.1 Theme 和状态测试

纯逻辑测试：

- 状态优先级；
- Theme Token 解析；
- DPI 缩放；
- Popup Placement；
- 数值范围和格式转换；
- 编辑器 Provider 解析优先级。

### 20.2 WinForms STA 测试

通过 STA 线程测试：

- 控件可创建和 Dispose；
- SelectedObject 可绑定；
- 编辑器能够提交；
- 验证失败不会修改对象；
- DP.WorkFlow Adapter 会通知模型；
- Popup 能关闭并恢复 Focus；
- Theme 切换不会抛异常。

### 20.3 视觉回归

建立独立演示/视觉测试程序：

```text
ModernUI.WinForms.Gallery
```

固定：

- 窗口尺寸；
- DPI；
- 字体；
- Light/Dark；
- 控件状态；
- PropertyGrid 示例对象。

可保存截图进行人工或像素差异检查。视觉测试不能替代行为测试。

### 20.4 测试 seam

测试通过控件公共 interface 和可观察事件验证行为，不测试私有字段或绘制实现细节。重构 Canvas 或控件内部布局时，行为测试应保持稳定。

---

## 21. 开发阶段规划

### Phase 0：整理现有基础

- 保留 `ModernPropertyGrid.WinForms` 当前 interface；
- 记录现有 DP.WorkFlow Adapter 行为；
- 为 PropertyGrid 增加 STA 冒烟测试；
- 建立 Gallery 项目。

完成标准：现有 DP.WorkFlow 功能无回退。

### Phase 1：主题与绘制地基

实现：

- `ModernControl`；
- `ModernTheme`；
- Theme Context；
- Color/Size/Typography/Motion Token；
- DPI；
- `ICanvas`/`GdiCanvas`；
- 基础几何工具。

完成标准：Light/Dark 可切换，DPI 下尺寸正确，无 GDI 泄漏。

### Phase 2：简单基础控件

实现：

- `ModernPanel`；
- `ModernDivider`；
- `ModernButton`；
- `ModernSwitch`；
- `ModernIcon`；
- 集中动画时钟。

完成标准：状态、键盘、Focus、Disabled、主题表现一致。

### Phase 3：表单控件

实现：

- `ModernInput`；
- `ModernInputNumber`；
- `PopupHost`；
- `ModernSelect`；
- `ModernSelectMultiple`；
- `ModernCheckbox`；
- `ModernToolTip`。

完成标准：中文输入法、剪贴板、键盘导航、Popup 多屏定位可用。

### Phase 4：PropertyGrid 迁移

- 替换基础编辑器；
- 增加单位、范围和 Error 状态；
- 优化增量刷新；
- 保留 Provider interface；
- 完善行和分类视觉。

完成标准：普通对象和 DP.WorkFlow 同时工作，基础编辑不依赖原生控件外观。

### Phase 5：DP.WorkFlow/Vision 专用扩展

- WorkflowInput；
- 文件和目录；
- 脚本；
- 结构化集合；
- Vision Point/Rect/Range/ROI；
- 相机与图像源选择。

完成标准：业务扩展不修改通用 PropertyGrid 核心。

### Phase 6：按真实需求扩展

候选：

- Collapse；
- Tabs；
- Message/Notification；
- Tree；
- Table；
- DatePicker；
- Docking。

没有真实调用方时不提前实现。

---

## 22. 第一版验收标准

### 视觉

- Light/Dark 均具有完整 Token；
- 同类控件高度、圆角、边框一致；
- Hover、Focus、Pressed、Disabled 可区分；
- 普通文本对比度至少 4.5:1；
- 图标不使用 Emoji；
- 125%、150%、200% DPI 不错位。

### 行为

- 输入法、复制粘贴、撤销正常；
- 键盘可完成主要操作；
- Popup 不超出屏幕工作区；
- 动画可关闭且不阻塞输入；
- 控件 Dispose 后没有活动 Timer/动画/Popup；
- PropertyGrid 验证失败不会污染源对象。

### 架构

- `ModernUI.WinForms` 无 DP.WorkFlow/Vision 依赖；
- `ModernPropertyGrid.WinForms` 通过 Provider 扩展；
- DP.WorkFlow 通过 `ICustomTypeDescriptor` Adapter 接入；
- Vision 编辑器保留在 Vision 程序集；
- 控件代码不散布原始 RGB；
- 不通过本机绝对路径引用第三方项目。

---

## 23. 需要避免的反模式

### 23.1 巨型基础控件

错误：所有控件都从包含 Badge、Loading、拖拽、Popup、文件处理的基类继承。  
正确：基础类只提供绘制、Theme、DPI、状态和生命周期。

### 23.2 全局静态状态泛滥

错误：所有窗口只能共享一个静态主题和动画配置。  
正确：支持窗口级 Theme Context，静态默认主题只作为最终回退。

### 23.3 完全自绘输入框起步

错误：为了圆角输入框重新实现输入法和光标。  
正确：先使用自绘容器托管 Borderless TextBox。

### 23.4 每个动画独立线程

错误：Hover、Loading、Switch 分别创建后台 Task/Thread。  
正确：UI 线程统一 AnimationClock。

### 23.5 控件内部硬编码颜色

错误：`Color.FromArgb(...)` 分布在所有控件。  
正确：使用 Theme Token；只有默认主题定义具体颜色。

### 23.6 业务类型进入通用枚举

错误：在基础 UI 中加入 `WorkflowInput`、`VisionRoi` 等 EditorKind。  
正确：通过 Provider 和 PropertyEditorKey 扩展。

### 23.7 每次值变化完整重建

错误：`Controls.Clear()` 后创建所有参数行。  
正确：区分值变化、元数据变化和 Schema 变化。

### 23.8 静默吞掉绘制异常

错误：`catch { }`。  
正确：开发环境抛出或记录，生产环境提供明确降级策略。

---

## 24. 与 AntdUI 的关系

本框架参考 AntdUI 的实现原理，但不直接复制其代码：

- 借鉴语义 Token、自绘、DPI、动画、SVG、状态体系；
- 改用更小的基础类；
- 改用窗口级 Theme Context；
- 改用集中 UI 动画时钟；
- 输入控件优先采用混合实现；
- 业务扩展使用 Provider/Adapter；
- 不引入 AntdUI 的全量依赖。

如短期需要对照实现，可以把 AntdUI 作为开发参考或 Gallery 对照，不应以本机绝对路径成为生产依赖。复制 Apache-2.0 源码时必须保留其版权和许可证声明；只参考原理并自行实现时，应保持独立代码和命名。

---

## 25. 最终决策摘要

1. 建立独立 `ModernUI.WinForms`；
2. 以 PropertyGrid 为第一真实调用方；
3. 采用 Ant Design 风格语义 Token，但保留自主品牌能力；
4. Button、Switch、Panel 可完全自绘；
5. Input 使用自绘容器 + 原生 TextBox；
6. NumberInput 和 Select 基于框架组合实现；
7. 动画由 UI 线程统一调度；
8. Theme 使用上下文继承，不只依赖全局静态状态；
9. PropertyGrid 保留 `TypeDescriptor` 和 Provider interface；
10. DP.WorkFlow/Vision 只能通过 Adapter 和 Provider 接入；
11. 先完成 5～6 个真实需要的控件，再扩展完整控件库；
12. 所有阶段都以 DPI、键盘、输入法、无障碍和资源释放为验收条件。

该路线既能逐步形成属于项目自己的现代 WinForms 框架，又能控制完全自绘带来的维护风险，并为后续 DP.WorkFlow、Vision 和其他桌面应用提供稳定的复用基础。
