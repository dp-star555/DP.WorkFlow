# DP.WorkFlow 渐进学习指南

> **适用对象**：第一次阅读该项目，希望逐步掌握流程图模型、编译、运行、节点扩展、持久化、UI 与视觉功能的开发者。  
> **建议周期**：3 周，每天 1～2 小时。  
> **核心原则**：先理解最短运行链路，再进入并行、子流程、UI 和视觉；不要一开始逐行阅读大型类。

---

## 1. 先记住这张主线图

```text
节点与端口语义
      ↓
画布模型与连接
      ↓
结构校验与流程编译
      ↓
顺序执行与 Handler
      ↓
Context、输出与数据绑定
      ↓
循环、并行、子流程和恢复
      ↓
领域节点扩展
      ↓
JSON 持久化
      ↓
UI 设计器与运行监视
      ↓
视觉节点和图像显示
```

整个项目可以概括为：

```text
可编辑的 WorkflowCanvasModel
            ↓ 编译、校验
只读的 WorkflowExecutionPlan
            ↓ 调度
WorkflowEngine + NodeHandler
            ↓ 发布
Snapshot / Trace / Output
            ↓ 展示
WinForms / WPF / Vision UI
```

---

## 2. 项目地图

| 项目 | 主要职责 | 建议阅读顺序 |
|---|---|---:|
| `DP.WorkFlow.Abstractions` | 节点、端口、Handler、执行结果等基础契约 | 1 |
| `DP.WorkFlow.Core` | 画布、连接、校验、绑定分析和编译 | 2 |
| `DP.WorkFlow.Runtime` | Context、Engine、RuntimeHost、Snapshot、Trace | 3 |
| `DP.WorkFlow.Nodes.Standard` | 开始、结束、判断、循环、并行、信号、队列、脚本 | 4 |
| `DP.WorkFlow.Nodes.Composite` | Block 子流程 | 5 |
| `DP.WorkFlow.Nodes.Motion` | IO、轴、气缸、真空和读码器 | 6 |
| `DP.WorkFlow.Nodes.Process` | 产品流、工站、机器人和故障恢复 | 7 |
| `DP.WorkFlow.Persistence.Json` | JSON 保存、读取和版本迁移 | 8 |
| `DP.WorkFlow.UI.Shared` | 跨 WPF/WinForms 的设计器状态模型 | 9 |
| `DP.WorkFlow.Nodes.Vision` | 图像采集、Blob/颜色、测量/定位与标定 | 10 |
| `DP.WorkFlow.Vision.UI` | 图像显示、Overlay 和 ROI 编辑 | 11 |

> [!NOTE]
> `DP.WorkFlow.Scripting` 当前是空壳目录，实际脚本能力位于 `ScriptEngine` 项目。

---

# 第一部分：理解内核主链路

## 阶段 0：建立全局地图

### 学习目标

只理解项目之间的职责和依赖，不深入具体实现。

### 建议操作

- [ ] 打开 `DP.WorkFlow/DP.WorkFlow.sln`
- [ ] 查看各项目 `.csproj` 中的 `ProjectReference`
- [ ] 阅读 `docs/DP.WorkFlow_流程图架构梳理.pptx`
- [ ] 自己画一张项目依赖图

### 必须回答

1. 哪些项目属于稳定内核？
2. 哪些项目提供节点扩展？
3. UI 是否直接负责执行工作流？
4. 视觉节点是否直接依赖 HALCON 等厂商 SDK？

### 完成标志

能够用 3 分钟口头介绍每个项目的职责。

---

## 阶段 1：理解节点、端口、画布和连接

### 1.1 节点模型

首先阅读：

```text
src/Workflow/Kernel/DP.WorkFlow.Abstractions/Nodes/IWorkflowNodeModel.cs
```

重点关注：

```csharp
string Id { get; set; }
string Title { get; set; }
string NodeType { get; }
```

关键结论：

> 节点模型保存配置，不负责执行。

### 1.2 节点端口和目录

阅读：

```text
src/Workflow/Kernel/DP.WorkFlow.Abstractions/Nodes/WorkflowPorts.cs
src/Workflow/Kernel/DP.WorkFlow.Abstractions/Extensibility/WorkflowNodeCatalog.cs
```

重点理解：

- `WorkflowPortDescriptor`
- `WorkflowNodeDescriptor`
- `WorkflowNodeCatalog`
- 输入端口与输出端口
- `MaxConnections`
- 动态端口

### 1.3 画布和连接

阅读：

```text
src/Workflow/Kernel/DP.WorkFlow.Core/Documents/WorkflowCanvasModel.cs
src/Workflow/Kernel/DP.WorkFlow.Core/Documents/WorkflowConnectionModel.cs
```

对象关系：

```text
WorkflowCanvasModel
 ├─ Nodes
 │   └─ WorkflowCanvasNode
 │       ├─ IWorkflowNodeModel
 │       └─ X / Y / Width / Height
 └─ Connections
     └─ FromNodeId + FromPort
        ToNodeId   + ToPort
```

### 动手练习

不使用 UI，直接创建：

```text
Start → Delay → End
```

打印：

- [ ] 每个节点的 `Id`
- [ ] 每个节点的 `NodeType`
- [ ] 每条连接的来源端口
- [ ] 每条连接的目标端口

### 检查问题

- [ ] `NodeType` 为什么不能直接使用 CLR 类型名？
- [ ] 节点为什么不直接保存下一个节点 ID？
- [ ] 布局信息为什么放在 `WorkflowCanvasNode` 中？
- [ ] Handler 为什么不属于节点模型？

---

## 阶段 2：理解画布如何编译

### 阅读顺序

```text
1. src/Workflow/Kernel/DP.WorkFlow.Core/Compilation/Analysis/Validation/WorkflowValidationError.cs
2. src/Workflow/Kernel/DP.WorkFlow.Core/Compilation/WorkflowCompiler.cs
3. src/Workflow/Kernel/DP.WorkFlow.Core/Compilation/WorkflowExecutionPlan.cs
4. src/Workflow/Kernel/DP.WorkFlow.Core/Compilation/Analysis/Binding/WorkflowBindingAnalysis.cs
```

### 编译流程

```text
WorkflowCanvasModel
        ↓
节点与连接结构校验
        ↓
端口声明和连接基数校验
        ↓
构建节点索引与出边索引
        ↓
分析数据绑定
        ↓
识别并行分支和共同汇聚点
        ↓
递归编译 Block 子画布
        ↓
WorkflowExecutionPlan
```

### 重点接口

```csharp
GetNodeOrThrow(nodeId)
GetNextNodeIds(nodeId, portKey)
GetParallelScopeOrThrow(nodeId)
GetChildDefinitionOrThrow(nodeId)
```

### 动手练习

分别构造以下错误画布：

- [ ] 两个节点使用相同 ID
- [ ] 连接引用不存在的节点
- [ ] 使用未声明的输出端口
- [ ] 普通输出端口连接多个目标
- [ ] 添加一个 Start 无法到达的节点
- [ ] 创建循环引用的子画布

记录哪些属于：

| 类型 | 行为 |
|---|---|
| Error | 阻止编译和运行 |
| Warning | 保留诊断，但允许编译 |

### 运行测试

```bash
dotnet test tests/Workflow/DP.WorkFlow.Core.Tests/DP.WorkFlow.Core.Tests.csproj
```

### 完成标志

能够解释为什么 Engine 执行的是 `WorkflowExecutionPlan`，而不是直接执行 `WorkflowCanvasModel`。

---

## 阶段 3：理解最简单的顺序执行

> 暂时忽略并行、子流程和故障恢复。

### 阅读顺序

```text
1. src/Workflow/Kernel/DP.WorkFlow.Abstractions/Execution/IWorkflowNodeHandler.cs
2. src/Workflow/Kernel/DP.WorkFlow.Abstractions/Execution/NodeExecutionResult.cs
3. src/Workflow/Kernel/DP.WorkFlow.Abstractions/Execution/WorkflowNodeHandlerCatalog.cs
4. src/Workflow/Kernel/DP.WorkFlow.Runtime/Execution/WorkflowEngine.cs
5. src/Workflow/Kernel/DP.WorkFlow.Runtime/Hosting/WorkflowRuntimeHost.cs
```

第一次阅读 `WorkflowEngine.cs` 时，只关注：

```text
RunAsync
ExecutePathAsync
ExecuteNodeAsync
```

暂时跳过：

```text
ExecuteParallelScopeAsync
RunChildWorkflowAsync
故障恢复细节
快照内部实现
```

### 一次节点执行的完整链路

```text
NodeId
  ↓
WorkflowExecutionPlan.GetNodeOrThrow
  ↓
IWorkflowNodeModel
  ↓
WorkflowNodeHandlerCatalog.Resolve
  ↓
IWorkflowNodeHandler.ExecuteAsync
  ↓
NodeExecutionResult
  ↓
SelectedPortKey
  ↓
WorkflowExecutionPlan.GetNextNodeIds
  ↓
下一个 NodeId
```

### NodeExecutionResult 的三种结果

| 结果 | 含义 |
|---|---|
| `Continue(portKey, output)` | 从指定端口继续 |
| `Complete(output)` | 当前路径正常结束 |
| `Fail(message, strategy)` | 当前节点执行失败 |

关键结论：

> Handler 只选择出口端口，不直接决定目标节点。

### 推荐调试流程

```text
Start
  ↓
Action
  ↓
Decision
 ├─ True  → Delay → End
 └─ False → End
```

设置断点：

- [ ] `WorkflowRuntimeHost.Configure`
- [ ] `WorkflowCompiler.Compile`
- [ ] `WorkflowRuntimeHost.RunAsync`
- [ ] `WorkflowEngine.RunAsync`
- [ ] `WorkflowEngine.ExecutePathAsync`
- [ ] `WorkflowEngine.ExecuteNodeAsync`
- [ ] `WorkflowNodeHandlerCatalog.Resolve`
- [ ] `WorkflowExecutionPlan.GetNextNodeIds`

### 完成标志

能够在纸上画出一次节点执行从配置到下一个节点的完整调用链。

---

# 第二部分：理解运行时数据

## 阶段 4：理解 Context、变量和节点输出

### 阅读文件

```text
src/Workflow/Kernel/DP.WorkFlow.Runtime/State/WorkflowContext.cs
src/Workflow/Kernel/DP.WorkFlow.Runtime/Execution/WorkflowNodeExecutionContext.cs
```

### 三类数据必须分清

| 数据 | 用途 | 生命周期 |
|---|---|---|
| Variables | 流程共享变量 | 当前 Context |
| NodeOutputs | 节点每次执行的标准输出 | 当前 Run，并区分 Token/Scope |
| Services | 宿主注入的领域能力 | 由宿主管理 |

### 重点理解

- `SetVariable` / `TryGetVariable`
- `SetNodeOutput`
- `TryGetVisibleNodeOutput`
- `WorkflowExecutionIdentity`
- `RunId`、`TokenId`、`ScopeIds`

### 练习

- [ ] 在节点中写入全局变量
- [ ] 读取不存在的变量
- [ ] 让节点产生标准输出
- [ ] 多次执行同一节点并观察输出历史
- [ ] 比较“变量”和“节点输出”的差异

---

## 阶段 5：理解强类型数据绑定

### 阅读顺序

```text
1. src/Workflow/Kernel/DP.WorkFlow.Abstractions/Data/WorkflowInput.cs
2. src/Workflow/Kernel/DP.WorkFlow.Abstractions/Data/WorkflowBindingKey.cs
3. src/Workflow/Kernel/DP.WorkFlow.Runtime/Data/WorkflowBindingResolver.cs
4. src/Workflow/Kernel/DP.WorkFlow.Core/Compilation/Analysis/Binding/WorkflowBindingAnalysis.cs
```

### 输入来源

```text
WorkflowInput<T>
 ├─ Literal：固定值
 └─ Binding
     ├─ 全局变量
     └─ 上游节点输出 + 成员路径
```

示例：

```text
InspectNode | Result.Score
```

表示读取 `InspectNode` 输出对象中的 `Result.Score`。

### 绑定解析流程

```text
找到来源
   ↓
检查当前 Token/Scope 是否可见
   ↓
读取成员路径
   ↓
转换成目标类型 T
   ↓
交给消费节点
```

### 动手练习

定义：

```csharp
record ProductResult(bool Success, double Score);
```

测试以下绑定：

- [ ] `$`：读取整个输出
- [ ] `Score`：读取属性
- [ ] `ScoreX`：不存在的属性
- [ ] 将 `Score` 绑定为 `int`
- [ ] 读取未执行节点的输出
- [ ] 读取当前并行分支不可见的输出

### 完成标志

能够解释：

- 控制流决定“谁先执行”
- 数据绑定决定“值从哪里来”
- 为什么不能简单读取某个节点的“最近输出”

---

# 第三部分：高级执行能力

## 阶段 6：按顺序学习循环、并行、子流程和恢复

### 6.1 循环

阅读：

```text
src/Workflow/Nodes/DP.WorkFlow.Nodes.Standard/FlowControl/LoopNode.cs
src/Workflow/Kernel/DP.WorkFlow.Runtime/Execution/WorkflowEngine.cs
```

重点：

- 循环通过图上的回边实现
- 节点通过端口选择继续循环或结束
- Engine 设置单节点和总执行次数上限

练习：

```text
Start → Loop ──Loop──→ Action ──→ Loop
           └─Completed──────────→ End
```

---

### 6.2 并行

阅读：

```text
src/Workflow/Nodes/DP.WorkFlow.Nodes.Standard/FlowControl/ParallelAllNode.cs
src/Workflow/Nodes/DP.WorkFlow.Nodes.Standard/FlowControl/WaitAllInputsCompletedNode.cs
src/Workflow/Kernel/DP.WorkFlow.Core/Compilation/WorkflowCompiler.cs
src/Workflow/Kernel/DP.WorkFlow.Runtime/Execution/WorkflowEngine.cs
```

跟踪：

```text
ParallelAll
 ├─ Branch A ─┐
 ├─ Branch B ─┼→ WaitAllInputsCompleted
 └─ Branch C ─┘
```

检查清单：

- [ ] 编译期如何寻找共同汇聚点？
- [ ] 每个分支如何获得子 Token？
- [ ] 一个分支失败后其他分支如何取消？
- [ ] 汇聚后父 Token 如何继续？
- [ ] 分支输出何时对父流程可见？

---

### 6.3 Block 子流程

阅读：

```text
src/Workflow/Kernel/DP.WorkFlow.Core/Documents/IWorkflowSubDocumentNode.cs
src/Workflow/Nodes/DP.WorkFlow.Nodes.Composite/Block/BlockNode.cs
src/Workflow/Nodes/DP.WorkFlow.Nodes.Composite/Block/BlockNodeHandler.cs
src/Workflow/Kernel/DP.WorkFlow.Runtime/Execution/WorkflowEngine.cs
```

子流程数据关系：

```text
父 Context
   ↓ 输入映射
子 Context
   ↓ 子 WorkflowEngine
子节点输出 / 子变量
   ↓ 输出映射
父变量
```

重点：

- Services 共享
- Variables 复制
- NodeOutputs 隔离
- Signal 共享
- 输入输出必须显式映射
- 子引擎受父引擎 Pause/Hold 监管

测试：

```bash
dotnet test tests/Workflow/DP.WorkFlow.Nodes.Composite.Tests/DP.WorkFlow.Nodes.Composite.Tests.csproj
```

---

### 6.4 Pause、Hold 与 Cancel

重点阅读：

```text
WorkflowEngine.Pause
WorkflowEngine.Resume
WorkflowEngine.AddExternalHold
WorkflowEngine.RemoveExternalHold
WorkflowRuntimeHost.Cancel
AsyncManualResetEvent
```

| 操作 | 含义 |
|---|---|
| Pause | 人工暂停，当前节点完成后停止调度 |
| External Hold | 设备、恢复流程等外部原因暂停 |
| Resume | 清除人工暂停 |
| RemoveExternalHold | 移除指定 Hold 原因 |
| Cancel | 取消整个运行 |

---

### 6.5 故障恢复

阅读：

```text
src/Workflow/Kernel/DP.WorkFlow.Abstractions/Execution/IRecoverableWorkflowNode.cs
src/Workflow/Kernel/DP.WorkFlow.Abstractions/Execution/IWorkflowFaultRecoveryCoordinator.cs
src/Workflow/Nodes/DP.WorkFlow.Nodes.Process/Recovery/
src/Workflow/Kernel/DP.WorkFlow.Runtime/Execution/WorkflowEngine.cs
```

恢复链路：

```text
节点失败
  ↓
StopRun / StopRun / Interrupt
  ↓
IWorkflowFaultRecoveryCoordinator
  ↓
RetryFaultedNode / JumpToNode / Stop
```

---

# 第四部分：学会扩展节点

## 阶段 7：从标准节点中学习开发模式

### 第一组：简单节点

```text
StartNode.cs
EndNode.cs
DelayNode.cs
```

### 第二组：分支节点

```text
DecisionNode.cs
ValueCompareNode.cs
StringCompareNode.cs
```

### 第三组：异步等待节点

```text
SignalWaitNode.cs
QueueWaitNode.cs
WaitFunctionNode.cs
```

### 节点扩展模板

```text
NodeModel
   + WorkflowNodeAttribute
   + WorkflowProperty
   ↓
WorkflowNodeDescriptor
   ↓
RegisterNodes
   ↓
NodeHandler
   ↓
RegisterHandlers
   ↓
单元测试 + JSON Round-trip 测试
```

### 实战任务：实现 CounterNode

配置：

```text
VariableKey
Increment
Threshold
```

行为：

1. 读取流程变量
2. 增加计数
3. 写回流程变量
4. 小于阈值时从 `Continue` 端口继续
5. 达到阈值时从 `Completed` 端口继续

完成清单：

- [ ] 节点模型
- [ ] 属性元数据
- [ ] 端口声明
- [ ] Handler
- [ ] 节点注册
- [ ] Handler 注册
- [ ] 成功场景测试
- [ ] 参数错误测试
- [ ] JSON Round-trip 测试

> 完成 CounterNode 后，就基本掌握了该项目的普通节点扩展方式。

---

## 阶段 8：理解领域服务 Adapter

### 运控节点

优先阅读：

```text
src/Workflow/Nodes/DP.WorkFlow.Nodes.Motion/IO/IWorkflowIoService.cs
src/Workflow/Nodes/DP.WorkFlow.Nodes.Motion/IO/IoReadNode.cs
src/Workflow/Nodes/DP.WorkFlow.Nodes.Motion/IO/IoWriteNode.cs
src/Workflow/Nodes/DP.WorkFlow.Nodes.Motion/Axis/IWorkflowAxisService.cs
src/Workflow/Nodes/DP.WorkFlow.Nodes.Motion/Axis/AxisActionNode.cs
```

结构：

```text
DP.WorkFlow Node Handler
        ↓
IWorkflowIoService / IWorkflowAxisService
        ↓
宿主注册的真实设备 Adapter 或测试 Fake
```

### 工艺节点

建议顺序：

```text
ProductFlow
   ↓
Station
   ↓
WaferRobot
   ↓
Recovery
```

重点文件：

```text
IWorkflowProductFlowService.cs
IWorkflowWaferRobotService.cs
WorkflowWarningHandlerCoordinator.cs
```

### 动手练习

- [ ] 实现假的 `IWorkflowIoService`
- [ ] 不连接真实硬件运行 IO 节点
- [ ] 模拟成功、超时和异常
- [ ] 验证节点选择的输出端口

---

# 第五部分：外围能力

## 阶段 9：理解 JSON 持久化

### 阅读顺序

```text
1. Documents/WorkflowJsonDocument.cs
2. WorkflowDocumentJsonStore.Serialize
3. WorkflowDocumentJsonStore.CreateDocument
4. WorkflowDocumentJsonStore.Deserialize
5. WorkflowDocumentJsonStore.ReadDocument
6. WorkflowDocumentJsonStore.ReadLegacy
7. Models/UnknownWorkflowNodeModel.cs
```

### 两种版本必须区分

| 版本 | 作用范围 |
|---|---|
| SchemaVersion | 整个 JSON 文档格式 |
| NodeVersion | 某一种节点配置格式 |

### 关键行为

- 当前 Schema 为 3
- Schema 1/2 自动迁移
- 未注册节点保存为 Unknown 节点
- Unknown 节点保留 `RawConfig`
- 高于当前支持版本的节点被拒绝
- 保存文件采用临时文件和原子替换

### Round-trip 练习

```text
Canvas
  ↓ Serialize
JSON
  ↓ Deserialize
Canvas
  ↓ Serialize
JSON
```

比较：

- [ ] 节点配置
- [ ] 节点位置和尺寸
- [ ] 连接和端口
- [ ] 路径点
- [ ] 端口位置覆盖
- [ ] 隐藏端口
- [ ] 子画布

运行：

```bash
dotnet test tests/Workflow/DP.WorkFlow.Persistence.Tests/DP.WorkFlow.Persistence.Tests.csproj
```

---

## 阶段 10：理解 UI.Shared

> 不要一开始顺序通读 1,277 行的 `WorkflowDesignerSession.cs`。

### 建议顺序

#### 10.1 文档管理

```text
src/Workflow/Studio/DP.WorkFlow.UI.Shared/Authoring/Documents/WorkflowDocumentWorkspace.cs
```

关注：New、Open、Save、SaveAs、Dirty 和 RecentFiles。

#### 10.2 子画布导航

```text
src/Workflow/Studio/DP.WorkFlow.UI.Shared/Authoring/Navigation/WorkflowDesignerNavigator.cs
```

关注：RootSession、CurrentSession 和面包屑导航。

#### 10.3 属性检查器

```text
src/Workflow/Studio/DP.WorkFlow.UI.Shared/Editors/WorkflowPropertyInspectorModel.cs
```

关注：

- 属性元数据
- 动态可见性
- 专用 Editor
- Binding 候选
- 集合和结构化 JSON 编辑

#### 10.4 设计器会话

```text
src/Workflow/Studio/DP.WorkFlow.UI.Shared/Authoring/Designer/WorkflowDesignerSession.cs
```

按功能块阅读：

1. Selection
2. Add/Remove Node
3. Connect
4. Move/Align/Distribute
5. AutoLayout
6. Copy/Paste
7. Undo/Redo
8. RuntimeSnapshot

#### 10.5 正交路由

```text
src/Workflow/Studio/DP.WorkFlow.UI.Shared/Authoring/Designer/WorkflowOrthogonalRouter.cs
```

将它单独看成连线路径计算模块。

#### 10.6 运行监视

```text
src/Workflow/Studio/DP.WorkFlow.UI.Shared/Monitoring/WorkflowStudioRuntimeBinding.cs
src/Workflow/Studio/DP.WorkFlow.UI.Shared/Monitoring/WorkflowRuntimeMonitorModel.cs
```

数据流：

```text
WorkflowRuntimeHost
      ↓ Snapshot
WorkflowStudioRuntimeBinding
      ↓ SynchronizationContext
DesignerSession / RuntimeMonitorModel
      ↓
WPF / WinForms
```

运行测试：

```bash
dotnet test tests/Workflow/DP.WorkFlow.UI.Shared.Tests/DP.WorkFlow.UI.Shared.Tests.csproj
```

---

## 阶段 11：理解视觉链路

### 视觉节点阅读顺序

```text
1. src/Workflow/Nodes/DP.WorkFlow.Nodes.Vision/Catalog/WorkflowImageRuntimePluginModule.cs
2. src/Workflow/Nodes/DP.WorkFlow.Nodes.Vision/Acquisition/LoadVisionFileNode.cs
3. src/Workflow/Nodes/DP.WorkFlow.Nodes.Vision/Tools/AnalyzeVisionFrameNodes.cs
4. src/Workflow/Nodes/DP.WorkFlow.Nodes.Vision/Tools/MeasureLocateVisionNodes.cs
5. src/Workflow/Nodes/DP.WorkFlow.Nodes.Vision/Calibration/TypedVisionCalibrationNodes.cs
```

节点链路：

```text
LoadVisionFile / CaptureVisionFrame
    ↓ ImageFrame（统一IImageSource）
AnalyzeVisionBlobs / AnalyzeVisionColor
    ↓ BlobAnalysisResult / ColorAnalysisResult（事实数据）
通用比较 / 条件 / 脚本节点
```

### 视觉 UI 阅读顺序

```text
1. src/Workflow/Nodes/DP.WorkFlow.Nodes.Vision/Acquisition/WorkflowVisionFrameScope.cs
2. src/Workflow/Studio/DP.WorkFlow.Vision.UI/Editors/VisionFrameEditorPage.cs
3. src/Workflow/Studio/DP.WorkFlow.Vision.UI.WinForms/Editors/VisionFrameEditorRenderer.cs
4. src/Workflow/Studio/DP.WorkFlow.Vision.UI.Wpf/Editors/VisionFrameEditorRenderer.cs
5. ../DP.Vision/src/DP.Vision.UI/Roi/Editing/RoiEditor.cs
```

重点概念：

- 统一图像源与FrameId
- 每节点最新预览，不丢检测任务
- 有界运行输出帧仓
- 独立Retain/Dispose租约
- 先等待StopAsync再释放资源
- 同帧证据与拾取
- 原图像素边界坐标与精确Include/Exclude Region

### 推荐练习

先使用本地图像，不连接相机：

```text
Start
  ↓
Vision.LoadFile
  ↓
Vision.AnalyzeBlobs
  ↓
通用脚本或比较节点
  ↓
End
```

观察：

- [ ] 流程变量
- [ ] 节点标准输出
- [ ] Trace
- [ ] Success 端口和技术故障恢复
- [ ] 空识别结果仍作为成功执行的数据输出
- [ ] 图像发布
- [ ] Overlay
- [ ] 资源释放

---

# 第六部分：三周执行计划

## 第一周：掌握内核主链路

| 天数 | 学习内容 | 当天产出 |
|---:|---|---|
| 第 1 天 | 项目依赖、NodeModel、Catalog | 项目依赖图 |
| 第 2 天 | Canvas、Connection、Port | 手工创建 Start→Delay→End |
| 第 3 天 | Validator、Compiler、Definition | 5 个错误画布实验 |
| 第 4 天 | Handler、Result、顺序执行 | 一张执行调用链图 |
| 第 5 天 | Context、Variables、Outputs | 三类运行数据对比表 |
| 第 6 天 | BindingKey、WorkflowInput | Literal/Binding 示例 |
| 第 7 天 | Resolver、BindingAnalyzer | 绑定错误实验记录 |

## 第二周：掌握高级执行与节点扩展

| 天数 | 学习内容 | 当天产出 |
|---:|---|---|
| 第 1 天 | Loop | 可运行循环流程 |
| 第 2 天 | ParallelAll、WaitAll | 三分支并行流程 |
| 第 3 天 | Block 子流程 | 带输入输出映射的子流程 |
| 第 4 天 | Pause、Hold、Cancel | 状态转换图 |
| 第 5 天 | Fault Recovery | 恢复决策流程图 |
| 第 6 天 | CounterNode | 节点模型与 Handler |
| 第 7 天 | CounterNode 测试 | 单元测试与持久化测试 |

## 第三周：掌握外围能力

| 天数 | 学习内容 | 当天产出 |
|---:|---|---|
| 第 1 天 | JSON 持久化 | Round-trip 实验 |
| 第 2 天 | Workspace、Navigator | 文档和子画布笔记 |
| 第 3 天 | DesignerSession、Monitor | UI 状态流转图 |
| 第 4 天 | Motion Adapter | Fake IO 服务 |
| 第 5 天 | Process 节点 | 产品流与恢复关系图 |
| 第 6 天 | Vision Nodes | 本地图像工作流 |
| 第 7 天 | WorkflowVisionFrameScope、RoiEditor | 视觉数据链路图 |

---

# 第七部分：学习记录模板

建议每学习一个模块，就复制下面的模板：

```markdown
## 模块名称

### 它解决什么问题？

### 对外接口是什么？

### 输入是什么？

### 输出是什么？

### 它维护哪些状态？

### 它依赖哪些模块？

### 正常路径是什么？

### 失败路径是什么？

### 并发或生命周期风险是什么？

### 删除该模块后，复杂度会转移到哪里？

### 对应测试在哪里？

### 我做了什么实验？
```

---

# 第八部分：最终验收清单

完成整个学习计划后，应能够独立回答：

## 模型与编译

- [ ] 节点模型为什么不执行？
- [ ] 端口和连接如何决定控制流？
- [ ] 画布为什么必须先编译？
- [ ] 编译期检查了哪些问题？

## 运行时

- [ ] Engine 如何找到和执行 Handler？
- [ ] Handler 如何选择下一条路径？
- [ ] Variable 和 NodeOutput 有什么区别？
- [ ] Token、Scope 和 RunId 分别解决什么问题？

## 高级能力

- [ ] 并行作用域如何汇聚？
- [ ] 子流程如何传入和传出数据？
- [ ] Pause、Hold 和 Cancel 有什么区别？
- [ ] 可恢复故障如何重试或跳转？

## 扩展和外围

- [ ] 如何增加一种新节点？
- [ ] 如何接入一个新的设备 Adapter？
- [ ] SchemaVersion 和 NodeVersion 有什么区别？
- [ ] UI 如何接收运行快照？
- [ ] 视觉图像为什么需要 Lease 和 ResourceTracker？

---

# 第九部分：建议使用的测试命令

当前 `DP.WorkFlow.sln` 没有收录全部测试项目。学习期间建议直接执行目标测试项目。

```bash
dotnet test tests/Workflow/DP.WorkFlow.Core.Tests/DP.WorkFlow.Core.Tests.csproj
dotnet test tests/Workflow/DP.WorkFlow.Runtime.Tests/DP.WorkFlow.Runtime.Tests.csproj
dotnet test tests/Workflow/DP.WorkFlow.Nodes.Standard.Tests/DP.WorkFlow.Nodes.Standard.Tests.csproj
dotnet test tests/Workflow/DP.WorkFlow.Nodes.Composite.Tests/DP.WorkFlow.Nodes.Composite.Tests.csproj
dotnet test tests/Workflow/DP.WorkFlow.Nodes.Motion.Tests/DP.WorkFlow.Nodes.Motion.Tests.csproj
dotnet test tests/Workflow/DP.WorkFlow.Nodes.Process.Tests/DP.WorkFlow.Nodes.Process.Tests.csproj
dotnet test tests/Workflow/DP.WorkFlow.Persistence.Tests/DP.WorkFlow.Persistence.Tests.csproj
dotnet test tests/Workflow/DP.WorkFlow.UI.Shared.Tests/DP.WorkFlow.UI.Shared.Tests.csproj
```

执行全部测试项目：

```bash
for project in tests/*.Tests/*.csproj; do
  dotnet test "$project" --no-restore
 done
```

---

## 最后建议

1. **不要按文件夹顺序从头通读。**始终围绕一条可运行流程跟踪调用链。
2. **不要先阅读大型类。**先建立概念，再按方法进入 `WorkflowEngine` 和 `WorkflowDesignerSession`。
3. **每阶段都运行测试。**测试通常比注释更能说明模块真正承诺的行为。
4. **优先使用小实验。**一个 3 节点流程比阅读 500 行实现更容易建立正确理解。
5. **先理解外部接口，再理解内部实现。**重点关注调用者需要知道什么，以及复杂度被隐藏在哪里。
