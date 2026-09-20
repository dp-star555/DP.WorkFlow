# 节点工程分类与依赖边界

## 分类原则

节点按“运行语义和外部依赖”分类，不按每一种设备拆一个程序集。当前保持四个节点工程，避免过细：

| 工程 | 职责 | 典型节点 |
|---|---|---|
| `DP.WorkFlow.Nodes.Standard` | 不依赖设备和业务领域的基础节点 | Start/End、判断、比较、等待、循环、并行、Convert、Signal、Queue |
| `DP.WorkFlow.Nodes.Composite` | 持有子画布或模板引用的复合节点 | Block，后续可版本化子流程模板 |
| `DP.WorkFlow.Nodes.Motion` | 依赖通用设备能力，但不依赖具体工艺领域 | IO、Axis、Cylinder、Vacuum、CodeReader |
| `DP.WorkFlow.Nodes.Process` | 产品、工站、操作员和异常恢复等工艺语义 | SafePoint、告警恢复、ProductFlow、Station、WaferRobot 编排 |

## 为什么不继续细分

- 不为 IO、Axis、气缸、真空分别创建程序集；它们共享取消、超时、报警、设备服务和仿真约束。
- 不为 Product、Station、Robot 分别创建程序集；它们共同维护产品移动事务和工站状态一致性。
- `Block` 保持独立，因为 Composite 依赖 Core/Runtime 子流程契约，而 Standard 必须保持轻量。
- 厂商驱动不得进入节点工程；驱动适配器由宿主项目实现并通过接口注入。

## 依赖方向

```text
Abstractions
  ↑
  ├─ Nodes.Standard
  ├─ Nodes.Motion
  ├─ Nodes.Process
  └─ Core ← Nodes.Composite
```

约束：

1. Standard、Motion、Process 不引用 WinForms/WPF；
2. Motion 不引用 Process；
3. Process 可以消费宿主提供的领域接口，但不引用具体设备驱动；
4. 节点模型只保存可序列化配置，Handler 执行服务调用；
5. UI 通过 Catalog 元数据发现所有节点工程。

## 节点归属清单

### Standard

- 已实现：Start、End、Action、Decision、ValueCompare、StringCompare、WaitFunction、Delay、Loop、Jump、ParallelAll、WaitAllInputsCompleted。
- 已实现：SignalSet、SignalWait，以及非轮询的线程安全 `WorkflowSignalService`。
- 已实现：ConvertValue、QueueAdd/Remove/Read/Wait。
- 已实现：SignalValueSet/Wait、WaitSignals、布尔/数据信号批量初始化。
- Label 不作为运行节点恢复，后续实现为设计器 Annotation。

### Composite

- 已实现：Block。
- 待实现：可版本化子流程模板引用、专用模板参数契约。

### Motion

- 已实现：IORead、IOWrite、IOWait、IOMultiCheck、IOMultiWait。
- 已实现：AxisAction、AxisServo、AxisStop、AxisWait。
- 已实现：CylinderControl/Wait、VacuumControl/Wait。
- 已实现：CodeReaderOpen/Close/Trigger/WaitScan。
- 所有运控节点均通过宿主服务接口执行，不依赖厂商驱动或 UI。

### Process

- 已实现：SafePoint、WarnStopStation、WarningPrompt、OperatorChoice、OperatorStepConfirm。
- 已实现：RetryCurrentNode、ReturnToSafePoint、WarningHandlerBlock/Start。
- 已实现：ProductFlow、Station 和 WaferRobot 全部旧 NodeType；统一通过 `IWorkflowProcessService` 接入宿主领域实现。
