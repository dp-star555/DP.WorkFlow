# WinForms UI 展示结构与文件分层

本文按“运行时实际界面结构 → 项目分层 → 文件职责 → Designer 可见范围 → 当前需要整理的问题”说明当前 WinForms UI。

## 1. 示例宿主

入口位于：

```text
samples/DP.WorkFlow.WinForms.Sample/
├─ Form1.cs
├─ Form1.Designer.cs
└─ DemoWorkflowServices.cs
```

运行结构：

```text
Form1
└─ WorkflowStudioControl
   ├─ 顶部工具栏
   └─ 主工作区
```

`Form1` 负责：

- 创建节点目录。
- 创建文档工作区。
- 注册 Runtime Handler。
- 注册设备模拟服务。
- 注册 WorkflowImageRuntimePluginModule。
- 固定装配 DP.Vision 中立采集和算法服务，按SDK可用性注册独立相机实现。
- 注册视觉页面 Provider 和 Renderer。
- 把这些对象注入 `WorkflowStudioControl`。

因此 `Form1` 是组合根，不应该承载通用 UI 逻辑。

---

## 2. WorkflowStudioControl 主界面

主要文件：

```text
src/Workflow/Studio/DP.WorkFlow.UI.WinForms/Studio/
├─ WorkflowStudioControl.cs
├─ WorkflowStudioControl.Designer.cs
└─ WorkflowStudioControl.resx
```

当前实际结构：

```text
WorkflowStudioControl
├─ ToolStrip 顶部工具栏
│  ├─ 文件
│  │  ├─ 新建
│  │  ├─ 打开
│  │  ├─ 保存
│  │  └─ 另存为
│  ├─ 撤销
│  ├─ 重做
│  ├─ 面包屑
│  ├─ 适合画布
│  ├─ 布局
│  ├─ 运行
│  ├─ 暂停
│  ├─ 继续
│  ├─ 停止
│  └─ 运行状态
│
└─ toolboxAndEditor 左右 ModernSplitter（固定左侧宽度）
   ├─ 左侧：WorkflowToolboxControl
   └─ 右侧：canvasAndDiagnostics 上下 ModernSplitter（固定底部高度）
      ├─ 上方：WorkflowDesignerControl
      └─ 下方：ModernTabControl
         ├─ 诊断：WorkflowDiagnosticsControl
         └─ 运行监视：WorkflowRuntimeMonitorControl
```

示意图：

```text
┌────────────────────────────────────────────────────────────┐
│ 文件 撤销 重做 Root 适合画布 布局 运行 暂停 停止          │
├──────────────┬─────────────────────────────────────────────┤
│              │                                             │
│ 工具箱       │              工作流画布                     │
│              │                                             │
│ 分类树       │                                             │
│              ├─────────────────────────────────────────────┤
│              │ 诊断 | 运行监视                             │
│              │                                             │
└──────────────┴─────────────────────────────────────────────┘
```

### 2.1 当前参数面板状态

`WorkflowStudioControl` 当前虽然公开了：

```csharp
public WorkflowPropertyPanel Properties { get; }
```

但这个参数面板没有放入主工作台可见分栏。

当前主要参数编辑入口是：

```text
双击节点
→ WorkflowNodeEditorDialog
```

所以现在不是“左侧工具箱、中央画布、右侧属性”的传统 VS 布局，而是“中央设计器 + 双击模态节点信息窗口”。

---

## 3. 工作流画布

文件：

```text
src/Workflow/Studio/DP.WorkFlow.UI.WinForms/Designer/
├─ WorkflowDesignerControl.cs
└─ WorkflowDesignerControl.Designer.cs
```

> 这里的 `Designer` 是“工作流设计器”业务目录，不是 `.Designer.cs` 的含义。

### 3.1 WorkflowDesignerControl.Designer.cs

只负责静态画布属性：

- 默认尺寸。
- 背景色。
- 前景色。
- `AllowDrop`。
- `TabStop`。
- `DoubleBuffered`。
- `ResizeRedraw`。

### 3.2 WorkflowDesignerControl.cs

负责全部运行时绘制和交互：

- 绘制背景网格。
- 绘制节点。
- 绘制端口。
- 绘制正交连接。
- 绘制运行状态。
- 绘制 Waypoint。
- 节点拖动。
- 框选。
- 画布平移。
- 连线创建。
- 连线路径段拖动。
- 右键菜单。
- 端口边切换。
- 双击节点。
- 工具箱拖放。
- 缩放和坐标转换。

它是 GDI+ 自绘 `Control`，因此 Visual Studio Designer 中只能看到空白画布外壳，不能看到运行时节点。

---

## 4. 工具箱

文件：

```text
src/Workflow/Studio/DP.WorkFlow.UI.WinForms/Toolbox/
├─ WorkflowToolboxControl.cs
├─ WorkflowToolboxControl.Designer.cs
└─ WorkflowToolboxControl.resx
```

结构：

```text
WorkflowToolboxControl
├─ 搜索框：ModernInput（按名称、类型、分类、说明过滤）
└─ ModernTreeView
   ├─ 标准
   ├─ 复合
   ├─ 运动
   ├─ 工艺
   └─ 视觉
```

Designer 文件负责：

- 搜索框与 ModernTreeView 的尺寸、Dock、行高、Tooltip 等静态外观。

颜色由 ModernUI 深色主题统一提供；`WorkflowWinFormsStyle` 的调色板也取自 `ModernTheme.Dark`，
原生宿主控件与 Modern 控件颜色一致。

普通 `.cs` 负责：

- 从 `WorkflowNodeCatalog` 读取工具箱项目。
- 按 `/` 分割分类。
- 生成分类树。
- 双击激活节点。
- 拖放节点到画布。
- 主题变化后重建颜色。

---

## 5. 诊断区域

文件：

```text
src/Workflow/Studio/DP.WorkFlow.UI.WinForms/Diagnostics/
├─ WorkflowDiagnosticsControl.cs
├─ WorkflowDiagnosticsControl.Designer.cs
└─ WorkflowDiagnosticsControl.resx
```

结构：

```text
WorkflowDiagnosticsControl
└─ ListView
   ├─ 级别
   ├─ 代码
   ├─ 节点
   └─ 消息
```

职责：

- 显示编译和设计期诊断。
- 区分错误和警告颜色。
- 双击诊断定位节点。
- 通过 `WorkflowDiagnosticsModel` 读取 UI 无关诊断结果。

---

## 6. 运行监视区域

文件：

```text
src/Workflow/Studio/DP.WorkFlow.UI.WinForms/Runtime/
├─ WorkflowRuntimeMonitorControl.cs
├─ WorkflowRuntimeMonitorControl.Designer.cs
└─ WorkflowRuntimeMonitorControl.resx
```

当前结构：

```text
WorkflowRuntimeMonitorControl
└─ TabControl
   ├─ Tokens
   ├─ Parallel
   ├─ Children
   ├─ Trace
   │  ├─ 筛选框
   │  ├─ 暂停滚动
   │  └─ Trace ListView
   ├─ 输出
   └─ Timing
```

每张表格右键菜单提供“导出 CSV”（轨迹表按完整轨迹字段导出，Ctrl+Shift+E 同样导出轨迹）。
领域工具（如“插件与算法”）通过 `AddToolWindow` 挂到“文件”菜单，点击后在弹出窗口中打开。

Designer 文件负责：

- 页签。
- ListView。
- 固定列。
- Trace 工具栏。
- 筛选框和按钮布局。

普通 `.cs` 负责：

- 绑定 `WorkflowStudioRuntimeBinding`。
- 接收运行快照。
- 刷新 Token。
- 刷新 ParallelScope。
- 刷新子流程。
- 刷新 Trace。
- 刷新输出。
- 刷新耗时趋势。
- CSV 导出。

---

## 7. 节点信息窗口

文件：

```text
src/Workflow/Studio/DP.WorkFlow.UI.WinForms/Properties/
├─ WorkflowNodeEditorDialog.cs
├─ WorkflowNodeEditorDialog.Designer.cs
└─ WorkflowNodeEditorDialog.resx
```

当前结构：

```text
WorkflowNodeEditorDialog
├─ 工作区：SplitContainer（没有主体页面时左侧分页铺满窗口）
│  ├─ 左侧：ModernTabControl 分页（只有一页时直接显示）
│  │  ├─ 参数：WorkflowPropertyPanel（标题、节点标识在“基本信息”分类中）
│  │  ├─ 渲染器附加列表页：如视觉节点的“ROI列表”
│  │  └─ 运行结果：所有节点都有的只读结果页（最近一次运行状态与输出）
│  └─ 右侧：主体页面（子流程 / 脚本 / 图像画布等），按页面 Order 纵向排列，不放入分页
└─ 底部命令栏
   ├─ 应用
   ├─ 确定
   └─ 取消
```

```text
┌──────────────────────────┬─────────────────────────┐
│ [参数] [ROI列表] [运行结果] │ 图像与测量范围            │
├──────────────────────────┤                         │
│ 当前分页内容               │  工具栏 + 图像画布         │
│                          │                         │
├──────────────────────────┴─────────────────────────┤
│                              应用 确定 取消         │
└────────────────────────────────────────────────────┘
```

左侧默认选中“参数”。“ROI列表”由实现 `IWorkflowWinFormsNodeEditorSidePanelRenderer`
（WPF 为 `IWorkflowWpfNodeEditorSidePanelRenderer`）的渲染器提供，只在节点有可编辑测量范围时出现；
列表与右侧画布共用同一个 `RoiEditor`（`VisionRoiListModel`），两边的选择、删除和用途修改实时同步。
“运行结果”页由 UI.Shared 的 `WorkflowNodeResultPageProvider` 为每个节点提供，数据来自原始设计会话的运行快照；
输出值通过 `WorkflowStudioRuntimeBinding` 设置的 `NodeOutputProvider` 读取，并按快照 RunId 过滤。

Designer 文件当前能调整：

- 工作区容器。
- 底部命令栏。
- 默认窗口尺寸。

仍在普通 `.cs` 中动态生成：

- 左侧分页与右侧主体布局。
- ROI 列表页。
- Block 子流程工具栏。
- 脚本页面工具栏。
- 脚本诊断列表。
- 通用图像页面。
- 运行结果页。
- 自定义 Renderer 页面。

这一部分是下一步最值得继续拆分的区域。

---

## 8. 参数面板

文件：

```text
src/Workflow/Studio/DP.WorkFlow.UI.WinForms/Properties/
├─ WorkflowPropertyPanel.cs
├─ WorkflowPropertyPanel.Designer.cs
└─ WorkflowPropertyPanel.resx
```

结构：

```text
WorkflowPropertyPanel
├─ 搜索框
├─ 动态参数滚动区域
└─ 参数说明区域
```

Designer 文件负责：

```text
搜索框
动态内容容器
底部说明区
三部分高度
Padding / Margin / Dock
```

普通 `.cs` 负责动态参数行：

```text
分类标题
├─ 属性名称
└─ 编辑器
```

支持的编辑器：

- 文本。
- 数字。
- Boolean。
- Enum。
- WorkflowInput。
- 文件路径。
- 文件夹路径。
- 集合表格。
- JSON 复杂对象。
- 脚本快捷编辑。
- Block 映射快捷入口。
- 输出端口可见性。

具体参数行无法固定放入 Designer，因为节点类型和属性数量运行时才知道。

---

## 9. 绑定选择窗口

文件：

```text
src/Workflow/Studio/DP.WorkFlow.UI.WinForms/Properties/
├─ WorkflowBindingSelectorDialog.cs
├─ WorkflowBindingSelectorDialog.Designer.cs
└─ WorkflowBindingSelectorDialog.resx
```

结构：

```text
绑定选择窗口
├─ 搜索框
├─ TreeView
│  ├─ 前道节点输出
│  └─ 全局数据
├─ 候选数量状态栏
└─ 确定/取消
```

Designer 负责布局，普通 `.cs` 负责：

- 转换共享绑定树。
- 搜索。
- 折叠与展开。
- 当前项定位。
- 双击确认。
- 绑定叶子校验。

---

## 10. Block 映射窗口

文件：

```text
src/Workflow/Studio/DP.WorkFlow.UI.WinForms/Properties/
├─ WorkflowBlockMappingEditorDialog.cs
├─ WorkflowBlockMappingEditorDialog.Designer.cs
└─ WorkflowBlockMappingEditorDialog.resx
```

结构：

```text
Block 映射窗口
├─ 输入映射 Tab
│  └─ DataGridView
├─ 输出映射 Tab
│  └─ DataGridView
└─ 确定/取消
```

运行时动态生成列：

- 来源类型。
- 固定值。
- 父变量。
- 子变量。
- 父节点绑定。
- 子节点绑定。

---

## 11. C# using 管理窗口

文件：

```text
src/Workflow/Studio/DP.WorkFlow.UI.WinForms/Properties/
├─ CSharpUsingManagerDialog.cs
├─ CSharpUsingManagerDialog.Designer.cs
└─ CSharpUsingManagerDialog.resx
```

结构：

```text
using 管理
├─ 使用说明
├─ 命名空间输入框
├─ 添加按钮
├─ using 列表
├─ 删除/排序/添加常用 using
├─ 格式说明
└─ 应用/取消
```

---

## 12. Roslyn 脚本编辑控件

位于独立程序集：

```text
src/Platform/Scripting/ScriptEngine.WinForms/
├─ RoslynScriptEditorControl.cs
├─ RoslynScriptEditorControl.Designer.cs
└─ ScriptEngine.WinForms.csproj
```

Designer 文件负责：

- Scintilla 控件。
- Dock。
- 默认尺寸。
- 滚动条。
- Tab 宽度。
- 换行方式。

普通 `.cs` 负责：

- Roslyn 高亮。
- 代码补全。
- 自动缩进。
- 错误波浪线。
- 行号宽度。
- 诊断。
- 编译。
- 键盘事件。

当前不足：节点工作台中的完整脚本页面：

```text
编译按钮
using 管理
脚本上下文
状态文本
RoslynScriptEditorControl
诊断列表
```

目前仍然由：

```text
WorkflowNodeEditorDialog.cs
```

动态创建。

也就是说，Scintilla 编辑器本身可设计，但“完整脚本页面”还没有独立的：

```text
WorkflowScriptEditorPageControl.Designer.cs
```

---

## 13. 视觉图像与 ROI 编辑器

```text
DP.WorkFlow.Vision.UI/Editors/VisionFrameEditorPage.cs
  → DP.Vision.UI.RoiEditor（共享编辑事务）
DP.WorkFlow.Vision.UI.WinForms/Editors/VisionFrameEditorRenderer.cs
  → DP.Vision.Winform.VisionCanvasControl（原生GDI+画布）
```

RendererKey为`DP.Vision.FrameEditor`。页面提供输入/结果/模板/手动预览、面积ROI工具及结果拾取。模型编辑隔离副本，确认一次提交/Undo，取消不污染正式文档；后台只使用独立ROI配置，不接活动编辑器。

画布持有独立图像租约，预览序号与FrameId分开；Tab隐藏停止刷新，页面关闭才释放。WPF对应Renderer使用真正原生DP.Vision.WPF画布，而不是WindowsFormsHost。

---

## 14. 厂商边界与显示分离

HALCON仅存在于同级`DP.Vision.Halcon`的采集/像素复制边界。设备返回的SDK图像复制为IImageSource后交给统一画布显示，UI不加载HALCON桌面窗口，也没有旧原生视口兼容接口。Gray8/Gray16/RGB通道与租约释放使用真实SDK测试，现场相机触发及物理输入仍需验收。

---

## 15. WinForms UI 下方依赖的共享层

WinForms 不是直接操作节点反射和 Runtime，而是依赖：

```text
src/Workflow/Studio/DP.WorkFlow.UI.Shared/
├─ Designer/
│  ├─ WorkflowDesignerSession.cs
│  ├─ WorkflowDesignerGeometry.cs
│  └─ WorkflowOrthogonalRouter.cs
├─ Properties/
│  ├─ WorkflowNodeEditorModel.cs
│  ├─ WorkflowPropertyInspectorModel.cs
│  ├─ WorkflowBindingTreeModel.cs
│  ├─ WorkflowCollectionTableModel.cs
│  └─ WorkflowCSharpScriptEditorModel.cs
├─ Runtime/
│  ├─ WorkflowRuntimeMonitorModel.cs
│  └─ WorkflowStudioRuntimeBinding.cs
├─ Diagnostics/
│  └─ WorkflowDiagnosticsModel.cs
└─ Documents/
   └─ WorkflowDocumentWorkspace.cs
```

依赖关系：

```text
WinForms Control
    ↓
UI.Shared Model
    ↓
Core / Runtime / Abstractions
```

---

## 16. WinForms UI 文件分层总结

```text
DP.WorkFlow.UI.WinForms
├─ Studio          整体工作台组合
├─ Designer        工作流画布
├─ Toolbox         节点工具箱
├─ Properties      参数和节点编辑窗口
├─ Diagnostics     设计期诊断
├─ Runtime         运行监视
└─ Styling         WinForms 主题

ScriptEngine.WinForms
└─ Roslyn/Scintilla 脚本编辑控件

DP.WorkFlow.Vision.UI.WinForms
└─ 共享FrameEditor页面的WinForms Renderer

DP.Vision.Winform
└─ 与Workflow无关的原生图像/几何画布

DP.WorkFlow.UI.Shared
└─ WinForms/WPF 共享的 UI 模型和编辑语义
```

---

## 17. 当前最值得优先整理的地方

从人工界面优化角度，建议顺序如下。

### 17.1 WorkflowStudioControl

- 确定主工作台最终是否增加右侧参数面板。
- 调整工具箱、画布、诊断和运行监视比例。

### 17.2 WorkflowNodeEditorDialog

- 确定普通节点与特殊节点的最终尺寸。
- 调整顶部节点信息是否过高。
- 调整左右参数/特殊内容比例。

### 17.3 把动态特殊页面提取为独立控件

- `WorkflowSubWorkflowPageControl`
- `WorkflowScriptEditorPageControl`
- `WorkflowImagePageControl`

### 17.4 WorkflowPropertyPanel

- 参数行高。
- 分类标题。
- 属性名宽度。
- 值编辑器尺寸。
- 底部说明区域。

### 17.5 视觉 ROI

- 工具栏按钮分组。
- 状态栏位置。
- 图像视口和参数区比例。

当前最大的结构问题是：**节点窗口外壳已经 Designer 化，但 Block、脚本、通用图像等特殊页面仍在 `WorkflowNodeEditorDialog.cs` 内动态拼装。**这部分应该成为下一轮 UI 整理的重点。
