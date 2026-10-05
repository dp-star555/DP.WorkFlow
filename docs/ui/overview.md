# DP.WorkFlow 双 UI 第一阶段

## 工程

- `DP.WorkFlow.UI.Shared`：UI 无关设计会话、编辑命令、Undo/Redo、视口、工具箱和运行快照覆盖层。
- `DP.WorkFlow.UI.WinForms`：GDI+ 画布与 WinForms 工具箱。
- `DP.WorkFlow.UI.Wpf`：DrawingContext 画布与 WPF 工具箱。

两个 UI 使用同一个 `WorkflowCanvasModel` 和 `WorkflowDesignerSession`，但不强制共享具体控件。

## 已支持

- 深色无限画布与网格；
- 节点绘制、选择和拖动；
- 输入/输出端口绘制；
- 拖动端口创建连接；
- 端口方向、重复连接和连接基数检查；
- 中键或右键平移；
- 鼠标滚轮以指针为中心缩放；
- FitToView；
- Delete、Ctrl+Z、Ctrl+Y；
- 节点运行状态颜色覆盖；
- 双击节点编辑事件；
- 按分类路径组织的树状节点工具箱；
- 从工具箱拖放节点到指定画布坐标；拖放到水平/垂直连线附近时自动拆分连接并插入节点；
- 双击节点打开 WinForms/WPF 原生模态参数窗口，主工作区不再常驻属性侧栏；
- Studio 工具栏支持接近 Visual Studio 2022 配色的深色/浅色主题即时切换；映射表格、工具箱和参数弹窗同步更新；
- `WorkflowInput<T>` Literal/Binding 编辑和强类型候选；
- 组合式 `WorkflowStudioControl`；
- Block 双击弹出节点窗口编辑子画布（左侧含工具箱）；
- 根流程运行后再进入 Block，子节点仍显示已映射运行状态；
- 进入 Block 后可用“运行当前子流程”独立调试，不启动根流程；
- 空 Block 首次进入时自动创建可撤销的 Start 节点；
- 子画布切换会取消父画布残留的拖动状态，避免 MouseUp 跨画布异常；
- 根/子流程运行快照映射；
- 编译诊断列表、运行前阻断和双击定位；
- RuntimeHost 运行、暂停、继续、停止工具栏；
- 按端口方向预留出线段的正交折线、命中选择和高亮；支持 Delete，或在连线已选中时右键直接删除；
- 多输出节点可在参数窗口逐项启用输出端口；未启用端口不显示、不允许连线，已启用端口沿节点边分布；
- 可沿折线路径拖动、持久化位置的中段连线标签；
- 节点正文不再重复显示英文 NodeType；单一通用 Input/Success 隐藏，仅多端口节点在内部显示具有分支意义的端口名；
- 未连接节点不显示彩色端点；选中后仅在四条边中心显示更小的中性拖拽锚点；
- 单输出节点可从任意中性锚点直接拖出连接；创建连线时目标节点显示四边临时输入落点；
- 连接建立后才显示输入紫色、输出绿色端点；同一端口与边的端点去重，不同连接仍可分别从上、左、右或下边接入；
- 直接拖动输入/输出端口到四周落点即可改变所在边；
- 输入拖动反馈使用紫色，输出拖动反馈使用绿色；
- 输出端口拖离节点 42px 后自动切换为创建连线；
- 节点和端口不再提供右键端点选择菜单，避免与画布右键平移冲突；
- 基于稀疏可见性网格和 A* 的局部最短正交路由，自动绕开中间节点；
- 拖动正交连线中间线段整体调整该段；
- 双击连线添加 Waypoint；
- 拖动 Waypoint 调整路径；
- Shift+单击 Waypoint 删除路径点；
- Waypoint 修改全部支持 Undo/Redo；
- Ctrl+单击节点多选；Ctrl+C/Ctrl+V 复制粘贴单节点或框选节点及其内部连接；
- 空白区域拖动框选，Ctrl 可追加框选；
- 多节点整体拖动和批量删除，节点及手工连线路径点默认吸附 24 单位栅格；
- 同时移动连接两端节点时，中间线段和 Waypoint 实时随动；
- 相近路径点自动吸附合并，共线路径点自动精简；
- 左右/顶底/中心对齐；
- 水平/垂直等距分布；
- 基于连接层次的自动布局；
- 空闲节点不显示内部 ID，运行信息在标题栏右侧显示为 `#本次运行执行顺序  耗时`；多输出端口保留正文空间，长标题自动扩展节点宽度；
- Token、ParallelScope、ChildWorkflow 运行监视；
- 有界 Trace 实时列表；
- 新建、打开、保存、另存为；
- 文档脏状态和最近文件列表；
- 打开旧文档时保留迁移报告；
- WinForms/WPF Block 输入输出映射集合编辑器；
- 父画布和子画布强类型绑定候选；
- Block 映射整体提交和单次 Undo；
- 按来源节点和成员路径组织的绑定树；
- 按节点、路径、成员和 CLR 类型搜索绑定；
- Trace 按节点、Token、Scope、步骤和消息筛选；
- 当前 Trace 筛选结果导出为带 BOM 的 UTF-8 CSV。

## WinForms

```csharp
var session = new WorkflowDesignerSession(canvas, nodeCatalog);
var navigator = new WorkflowDesignerNavigator(canvas, nodeCatalog, startNodeId);
var studio = new DP.WorkFlow.UI.WinForms.WorkflowStudioControl
{
    Dock = DockStyle.Fill,
    Navigator = navigator
};
```

## WPF

```csharp
var session = new WorkflowDesignerSession(canvas, nodeCatalog);
var navigator = new WorkflowDesignerNavigator(canvas, nodeCatalog, startNodeId);
var studio = new DP.WorkFlow.UI.Wpf.WorkflowStudioControl
{
    Navigator = navigator
};
```

## 运行状态接入

```csharp
runtimeHost.SnapshotChanged += snapshot =>
    navigator.SetRuntimeSnapshot(snapshot);
```

WinForms 和 WPF 控件会分别切换到 UI Dispatcher/消息线程刷新。

## 下一阶段

- Dictionary、List 和复杂对象通用属性编辑器；
- 动态 Action/插件输出 Schema；
- 节点运行耗时趋势和 Trace 暂停滚动；
- 最近文件跨进程持久化；
- 主题资源、DPI、键盘导航和无障碍支持。
