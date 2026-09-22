# Workflow 运行记录与实时快照设计

- 状态：Draft，供后续实施和验收对照
- 范围：`DP.WorkFlow` 运行期数据、实时快照、Trace、本地运行记录
- 不包含：设备状态回滚、产品领域数据库设计、图像文件归档格式、跨进程工作流续跑

## 1. 目标

本设计解决四个问题：

1. 长时间循环中，`RuntimeSnapshot` 不再反复复制随运行时间增长的完整历史。
2. 外部分析可以按真实执行顺序看到节点输入、节点内部步骤、节点输出、故障和恢复过程。
3. 节点执行到一半进程崩溃时，已经可靠提交的前缀记录仍然存在。
4. 运行工作数据、实时状态、诊断记录和业务记录有明确的所有者及生命周期。

核心原则：

```text
RuntimeSnapshot 是当前仪表盘。
Run Event 是执行行车记录。
Run Output 是当前运行的数据传递事实。
业务记录由领域能力显式提交。
```

## 2. 非目标

第一阶段不做以下事情：

- 不把所有节点输出永久序列化。
- 不通过 Trace 恢复工作流执行。
- 不让本地记录仓参与设备动作事务。
- 不尝试使本地数据库和设备控制器形成分布式事务。
- 不在本轮顺便解决节点输出的提前退役；真实输出第一阶段仍可保留到 Run 结束。
- 不把局部变量、公共数据、信号、图像和设备对象统一成一个 Key-Value 仓。

## 3. 数据分类与生命周期

### 3.1 运行工作集

只服务当前 Run：

- 当前节点状态；
- 活动 Token、祖先 Token 和并行 Scope；
- 循环帧与迭代号；
- 流程局部变量与运行内信号；
- Pending Operation；
- 当前 Run 的真实节点输出及可见性索引；
- 输出失效标记；
- 恢复入口和当前恢复会话；
- 当前子流程状态。

这些数据默认不本地化，Run 结束后按资源契约释放。

### 3.2 实时快照

`WorkflowRuntimeSnapshot` 是运行工作集的只读、不可变、时间点投影，用于 Studio 或其他实时监视器。

它只表达当前状态，不表达完整执行历史。

### 3.3 运行事件

运行过程中已经发生的事实，按 Run 内统一序号追加：

- 生命周期；
- 节点执行；
- 输入解析和输出提交；
- 节点内部 Trace；
- 故障；
- 恢复。

运行事件是本地化和外部分析的主要数据源。

### 3.4 业务记录

产品判定、条码、关键测量值、不合格原因、配方和证据资源属于领域记录。它们通过明确的领域能力提交，只通过 `RunId`、`ProductId`、`InspectionId` 等身份与运行事件关联。

运行事件不能替代产品记录，自动保存全部节点输出也不能替代领域提交。

## 4. 当前实现基线

当前代码：

- `WorkflowRunState` 保存输出历史、Latest 输出索引、Fault、RecoveryEvent 和失效输出序号。
- `WorkflowEngine` 保存有界 Trace 队列、节点当前状态、并行状态和子流程状态。
- `GetRuntimeSnapshot()` 每次读取 `RunState.NodeOutputs` 和 `RunState.Faults`，分别执行 `ToArray()`。
- 节点开始、节点结束、状态变化、并行变化、恢复记录和子流程快照都会触发 `PublishSnapshot()`。
- `_parallelScopes` 保留已完成 Scope；循环重复进入并行节点时会持续增长。
- `_childWorkflows` 以父节点、Token 和执行次数组成键；循环重复执行 Block 时会持续增长。
- Trace 只在内存中保存最近 `MaxTraceEntries` 条，没有内置本地持久化。
- 当前 Studio 主要消费 `Nodes`、`ActiveTokens` 和 `ChildWorkflows`；节点绑定直接读取 `WorkflowRunState`，不通过 Snapshot。

因此当前快照同时承担“实时状态”和“历史传输”，长 Run 中会产生累计平方级引用复制。

## 5. 目标结构

```text
WorkflowEngine
│
├─ Runtime Working Set
│  ├─ Node/Token/Scope/Loop
│  ├─ Variables/Signals
│  ├─ Operations
│  └─ Run Outputs
│
├─ Runtime Snapshot Projector
│  └─ 只冻结当前可观察状态
│
└─ Workflow Run Recorder
   ├─ 分配 Run 内统一事件序号
   ├─ 规范化、限长和脱敏 Payload
   ├─ 维护 UI 最近事件窗口
   └─ 向顶层配置的 Event Sink 推送
      ├─ 可不配置（仅保留内存最近窗口）
      ├─ SQL Adapter（由宿主选择）
      ├─ 文件 Adapter（由宿主选择）
      └─ 其他外部分析 Adapter
```

`Workflow Run Recorder` 应是深模块：Engine 只提交事件草稿和记录等级，不了解宿主最终选择 SQL、文件、远程系统还是不持久化。记录链路故障必须被观察和诊断，但不能反向改变工作流执行结果。

## 6. RuntimeSnapshot 设计

### 6.1 应保留的数据

建议目标字段：

```text
RunId
SnapshotSequence
Timestamp
WorkflowName
ExecutionState
CurrentNodeId
ActiveNodeIds
ActiveTokens
TotalElapsed
Nodes（每个静态节点最近状态）
ActiveParallelScopes
ActiveChildWorkflows（按当前子运行执行身份区分）
LatestChildWorkflowByParentNode（每个父节点最多一份）
ExternalHoldReasons
CurrentFault
CurrentRecovery
Message
```

### 6.2 应删除的数据

从高频 Snapshot 删除：

```text
NodeOutputs 全量历史
Faults 全量历史
RecoveryEvents 全量历史
已完成 ParallelScope 历史
同一 Block 的历次 ChildWorkflow 历史
Trace 历史
```

### 6.3 生成时机

只在当前可观察状态变化时发布：

- Run 开始和终止；
- 节点进入 Running；
- 节点进入 Completed、Failed 或 Canceled；
- Pause、Resume、Hold 变化；
- 活动并行分支进度变化；
- 当前故障或恢复阶段变化；
- 当前子流程状态变化。

以下事件默认只进入运行事件流，不发布 Snapshot：

- `InputResolved`；
- 节点内部 Trace；
- 设备调用步骤；
- 普通变量变更；
- `OutputCommitted`；
- 算法中间步骤。

### 6.4 为什么仍要复制当前状态

Snapshot 可能在线程切换后由 UI 读取。Engine 不能把仍会变化的内部 `Dictionary`、`HashSet` 或可变对象直接交给观察者。

因此生成 Snapshot 时仍然复制当前状态，但复制量必须受以下因素限制：

```text
静态节点数量 + 当前并行度 + 当前活动子流程数量
```

不能随 Run 已执行次数增长。

### 6.5 并行和子流程收敛

- 已完成并行 Scope 产生 `ParallelMerged` 事件后，从实时 Scope 集合移除。
- 活动子流程必须持续把最新 Snapshot 推送给父流程，Studio 在子流程运行期间双击 Block 后能够实时查看内部节点、Token 和状态。
- 同一父节点可能存在并发子运行，活动项按父执行身份区分；活动数量只受当前并发度限制。
- 子流程完成后，从活动集合移除，并按父节点 ID 覆盖保存最近一次完成状态，保证完成后短期查看仍可用且不会随循环次数增长。
- 更早的子流程执行只进入事件流，不在父 Snapshot 中保留历次完整快照。

## 7. 运行事件模型

### 7.1 Span 与 Event

一次节点执行是逻辑 Span，由 `WorkflowExecutionIdentity` 关联；Span 内每个步骤是独立 Event。

```text
NodeStarted
InputResolved
CommandPrepared
CommandDispatchStarted
DeviceAcknowledged
OutputCommitted
NodeCompleted
```

不得等节点结束后把整个 Span 打包为一条记录。

### 7.2 事件外壳

建议引入概念模型：

```csharp
public sealed record WorkflowRunEvent(
    Guid RunId,
    long Sequence,
    DateTimeOffset Timestamp,
    WorkflowRunEventCategory Category,
    string EventType,
    WorkflowEventWriteMode WriteMode,
    string? WorkflowName,
    string? NodeId,
    string? NodeType,
    WorkflowExecutionIdentity? ExecutionIdentity,
    Guid? OperationId,
    Guid? RecoveryCaseId,
    int SchemaVersion,
    IReadOnlyDictionary<string, WorkflowTraceValue>? Data);
```

说明：

- `Sequence` 在单个 Run 内严格唯一并单调增加。
- `EventType` 使用稳定英文键，显示文本单独存入 Data 或由 UI 本地化。
- `SchemaVersion` 是事件 Payload 版本，不是节点版本。
- 子 Run 使用自己的 `RunId`，并在 Run 记录中保存 `ParentRunId` 和父执行身份。
- `WorkflowExecutionIdentity` 需要完整保留 `NodeExecutionCount` 和 `LoopIteration`。

具体类型名可在实施时调整，但上述身份和排序字段不能丢失。

### 7.3 事件类别

```text
Lifecycle
NodeExecution
DataFlow
Trace
Fault
Recovery
```

类别用于查询和保留策略，`EventType` 表达具体语义。

### 7.4 必需事件

#### 生命周期

```text
RunStarted
RunCompleted
RunCanceled
RunFaulted
RunInterruptedDetected
```

#### 节点执行

```text
NodeStarted
NodeCompleted
NodeCanceled
```

`NodeCompleted` 至少包含耗时和所选控制出口。

#### 数据流

```text
InputResolved
OutputCommitted
VariableChangesCommitted
PublicDataChangesCommitted
```

第一阶段不记录每一次变量读取，只记录成功提交的 Key 和值摘要。脚本中的动态读取只有节点主动 Trace 时才可见。

#### 故障

```text
NodeFailed
OperationCleanupFailed
RecordingFailed
```

#### 恢复

```text
RecoveryStarted
RecoveryDecision
RecoveryRejected
RecoveryApplied
RecoveryStopped
```

### 7.5 节点自定义事件

节点通过执行上下文记录领域步骤，例如：

```text
CommandPrepared
CommandDispatchStarted
DeviceAcknowledged
WaitingForTarget
TargetObserved
ImageCaptured
AlgorithmStarted
AlgorithmCompleted
```

稳定的 `Step/EventType` 用于聚合；自由文本 `Message` 只用于人员阅读，不能作为机器查询键。

## 8. 数据血缘

### 8.1 输出提交

节点输出正式进入 Run Output Store 后记录 `OutputCommitted`。事件 Payload 把输出值展开到顶层稳定键，而不是塞进一个字典值（否则统一编码器只会留下"n 项"摘要）：

```text
OutputExecutionSequence
OutputType
OutputKeys              （只用于枚举稳定键）
OutputValue.$           （标量或不可展开根值）
OutputValue.<Property>  （结果 DTO 的第一层公开属性）
```

输出键不得由 Handler 在记录调用处重复手写。结果 DTO 使用公开输出属性的稳定名称；标量根输出使用统一键 `$`。例如结果对象的 `Output = 10` 自动记录为 `OutputValue.Output = 10`，绑定整个标量时记录为 `OutputValue.$ = 10`。图像、二进制、Stream、句柄和其他资源对象仍只提取明确身份及必要摘要，不能因自动键值提取而递归序列化完整内容；单个属性 getter 失败只记录 `[读取失败]` 摘要并降级诊断，其他键继续提取。超过 `MaxOutputProperties` 时按属性名顺序截断并记录降级诊断。

失败尝试不产生 `OutputCommitted`。

### 8.2 输入解析

每次 `WorkflowInput<T>` 成功解析后记录 `InputResolved`：

```text
InputName 或稳定配置路径
SourceKind：Literal/NodeOutput/PublicData
SourceNodeId（节点输出来源）
SourceOutputSequence（节点输出来源）
PublicDataKey（公共数据来源）
MemberPath
TargetType
ResolvedValueSummary
```

节点输出来源必须记录 `SourceOutputSequence`，否则循环和重复执行时无法确认消费者使用了来源节点的哪次输出。

### 8.3 自动输入槽识别

普通 Handler 保持自然调用：

```csharp
T? ResolveInput<T>(WorkflowInput<T> input);
```

不得要求每个 Handler 重复手写 `ResolveInput("Left", node.Left)`。目标输入键本来已经存在于节点模型的 `WorkflowInput<T>` 属性中，重复字符串可能与真实属性漂移或写错。

编译或绑定阶段应从冻结节点模型建立输入槽元数据：

```text
InputKey：节点模型中 WorkflowInput<T> 属性的稳定名称
ValueType
对应当前冻结模型输入实例的访问计划
```

ExecutionContext 根据当前节点及传入的 `WorkflowInput<T>` 自动定位输入槽，并记录：

```text
TargetNodeId
InputKey
SourceKind
SourceNodeId
SourceOutputKey/MemberPath
SourceOutputSequence
ResolvedValueSummary
```

来源节点与来源输出键直接来自 `WorkflowBindingKey.NodeId` 和 `MemberPath`，不由 Handler 重复指定。绑定解析器返回 `ResolvedValue`、`SourceKind`、`SourceOutputIdentity/Sequence` 和 `BindingKey`，来源信息仅在 Runtime 内部使用。

显式命名重载只作为动态输入、集合输入或无法从静态节点属性唯一识别时的逃生口，不是普通内置节点的默认迁移路径。同一个 `WorkflowInput` 实例若被多个输入槽复用并造成歧义，应在编译时拒绝或要求显式动态键。

## 9. Trace Payload 规则

### 9.1 不持久化任意对象图

当前 `IReadOnlyDictionary<string, object?>` 可以包含不可序列化对象、设备句柄、图像和循环引用。持久化前必须经过统一编码器。

建议 Recorder 内部拥有 `WorkflowTracePayloadEncoder`，负责：

- 支持标量和明确注册的结构类型；
- 限制字符串长度；
- 限制集合项数、对象深度和单事件字节数；
- 标记截断；
- 脱敏；
- 将大型资源转换为引用；
- 捕获格式化异常，避免诊断编码异常掩盖原业务故障。

### 9.2 默认值策略

直接保存：

```text
null、bool、整数、浮点、decimal、string、enum、Guid、时间、坐标等小值
```

摘要保存：

```text
普通结果 DTO、集合、异常、复杂设备结果
```

引用保存：

```text
图像、文件、模型、二进制、大型数组、设备资源
```

示例：

```json
{
  "FrameId": "F123",
  "Width": 2448,
  "Height": 2048,
  "PixelFormat": "Mono8",
  "ArtifactId": "optional"
}
```

### 9.3 标识化与禁止项

图像、二进制和复杂对象不记录完整内容，只记录明确标识和必要摘要，例如：

```text
类型名
FrameId / ArtifactId / FileId / ModelId
长度或尺寸
Hash（确有分析价值时）
可选外部位置
```

默认拒绝或脱敏：

- 密码、Token、连接字符串；
- 原始图像字节和任意二进制正文；
- 设备 SDK 句柄；
- `IServiceProvider`、Context 和 Operation 实例；
- 无界集合；
- 未知可变对象的递归序列化。

暂不在 Workflow 运行事件中建模操作员身份；未来由顶层人工操作模块单独记录，并可通过 PromptId、RecoveryCaseId 和 RunId 关联。

## 10. 写入可靠性

### 10.1 两种写入模式

```csharp
public enum WorkflowEventWriteMode
{
    Buffered,
    FlushRequested
}
```

#### Buffered

事件被有界 Recorder 队列接受后即可返回，由后台批量写入。进程突然终止时允许丢失尚未提交的尾部事件。

适用：

- 高频轮询；
- 算法调试步骤；
- 重复进度；
- 非关键性能采样。

#### FlushRequested

`FlushRequested` 在本设计中表示“高优先级、要求 Recorder 立即安排 Flush”，不是 Engine 执行前置条件。事件进入 Recorder 后，工作流继续运行；外部 Sink 在独立写入路径中确认或失败。

适用：

- Run 起止；
- 节点故障；
- 恢复裁决；
- 外部设备命令关键边界；
- 业务明确要求优先推送的步骤。

配置的 Sink 若确实写入稳定存储，只有它已经成功确认的事件才具有崩溃持久性。Runtime 不等待持久化成功，也不承诺 Sink 一定是 SQL、文件或稳定存储。

### 10.2 节点 Trace 接口

保留现有同步 `Trace(...)` 作为 Buffered 兼容入口，并增加记录模式重载：

```csharp
void Trace(
    string step,
    string? message,
    IReadOnlyDictionary<string, object?>? data,
    WorkflowEventWriteMode writeMode = WorkflowEventWriteMode.Buffered);
```

该调用只完成事件规范化和进入有界 Recorder 队列，不等待外部 Sink。这样记录延迟和失败不会成为设备命令或节点执行的前置条件。

### 10.3 已确定的批次级推送策略

Recorder 以“已封装批次”而不是单条事件作为后台队列和 Sink 调用的最小颗粒度。内部维护一个尚未封装的 `CurrentBatch`；事件仍在进入 Recorder 时统一分配 Run 内 Sequence，并按 Sequence 追加到该批次。

满足以下任一条件时，Recorder 在短临界区内原子地分离当前批次，并立即创建新的空 `CurrentBatch`，后续事件不会追加到已经封装的批次：

```text
当前批次达到 128 条
从批次第一条事件起经过 100ms
追加了一条 FlushRequested 事件
Run 结束
宿主正常关闭
```

具体语义：

- 达到 128 条时，封装一个恰好包含 128 条事件的批次；第 129 条进入新批次。
- FlushRequested 事件先按 Sequence 追加到当前批次，再立即封装整个批次。因此已有 20 条 Buffered 后出现一条 FlushRequested 时，产生包含 21 条事件且要求立即 Flush 的批次。
- 100ms 从空批次收到第一条事件时开始计算，后续事件不得延后该批次的截止时间，避免持续低频输入永远不推送。
- Run 结束和宿主正常关闭会封装非空尾批次，并请求尽力 Flush。
- 空批次永远不会入队或调用 Sink。

后台只有一个批次消费者，按批次中首个 Sequence 的顺序调用 Sink。Sink 较慢时，Workflow 仍可继续向新的 `CurrentBatch` 写入并封装后续批次；不得在组包临界区内调用 Sink。

通知表达“至少有一个完整批次可处理”，不是事件计数。无论一个批次包含 1 条还是 128 条事件，每个批次只入队一次；不得因待发送事件数持续大于 128 而按后续每条事件重复累计 Semaphore 唤醒。

FlushRequested 批次不得越过更早的批次乱序发送。它只要求立即封装、立即调度，并在该批次到达 Sink 时携带 `requestImmediateFlush = true`；不改变 fail-open，也不表示 Workflow 已等待外部持久化确认。

待发送容量按事件数核算。容量不足时从已封装待发送批次的队首开始淘汰最老批次并累计准确的丢失事件数，为最新批次腾出空间；不得淘汰正在写入 Sink 的批次，也不得阻塞 Workflow。至少保留最新一个批次：容量小于单批容量时不做无意义的清空。

### 10.4 记录失败语义

已确定采用 fail-open：记录不能反向影响工作流运行。

- 未配置外部 Sink：流程正常运行，只保留 Recorder 的有界内存最近窗口。
- `RunStarted` 推送失败：记录健康状态变为异常并通知宿主，Run 仍然启动。
- FlushRequested 推送失败：返回失败 Receipt、累计计数并通知宿主，节点和流程继续原业务路径。
- Buffered 队列达到上限：丢弃最老的待推送低等级事件，为最新事件腾出空间；不得无界增长，也不得阻塞流程。
- Sink 持续失败：使用独立的健康事件或宿主回调报告，不能依赖同一个失败 Sink 记录自身故障。
- 正常停止：尽力 Flush；Flush 失败不改变已经得到的 Run 终态。
- 进程崩溃：只保证外部 Sink 已经实际确认并持久化的事件；Runtime 不伪造持久性保证。

记录故障属于软件可观测性和防呆问题，不是节点故障、设备急停或恢复触发条件。

## 11. 设备命令与崩溃窗口

Event Sink 和外部设备不能形成同一事务，因此不要使用含义模糊的单一 `CommandIssued`。

推荐阶段：

```text
CommandPrepared
CommandDispatchStarted
DeviceAcknowledged
TargetObserved
```

含义：

- `CommandPrepared`：固定了操作意图和参数。
- `CommandDispatchStarted`：即将进入设备调用，不能证明设备收到。
- `DeviceAcknowledged`：设备 Adapter 返回已接受；仍不一定代表动作完成。
- `TargetObserved`：通过反馈确认最终目标状态。

若崩溃前最后事件是 `CommandDispatchStarted`，恢复时必须视为结果未知，通过 OperationId、业务幂等键或设备状态核实，不能自动重发。

## 12. Recorder 与外部 Sink 的接口

### 12.1 Engine 使用的深模块接口

建议保持小接口：

```csharp
public interface IWorkflowRunRecorder : IAsyncDisposable
{
    WorkflowRunEventReceipt Record(
        WorkflowRunEventDraft @event,
        WorkflowEventWriteMode writeMode);

    WorkflowRunEventBatch GetRecent(long afterSequence = 0);

    WorkflowRecordingHealth Health { get; }

    void ReportDegraded(string message);

    ValueTask CompleteAsync(
        WorkflowRunCompletion completion,
        CancellationToken cancellationToken);
}
```

职责隐藏在实现内部：

- 分配序号；
- 串行化多分支事件顺序；
- Payload 编码；
- 最近事件窗口；
- Buffered 批量和 FlushRequested 立即调度；
- 独立 Sink 写入循环与尽力 Flush；
- 健康状态、Sink 失败计数和独立的元数据降级诊断计数。

`ReportDegraded` 用于自动输入槽识别或输出摘要失败这类"记录内容不完整"的情况：它不抛异常、把 `Healthy` 降为 `Degraded`、累计独立的 `DiagnosticCount`，并按 `HealthNotificationInterval` 节流通知宿主；它不写回可能已经故障的 Sink，也不伪装成 Sink 写入失败。

### 12.2 顶层 Event Sink seam

Workflow Runtime 只负责推送，不决定是否保存到 SQL、文件或远程系统：

```csharp
public interface IWorkflowRunEventSink
{
    ValueTask<WorkflowRunEventWriteResult> WriteAsync(
        IReadOnlyList<WorkflowRunEvent> events,
        bool requestImmediateFlush,
        CancellationToken cancellationToken);

    ValueTask FlushAsync(CancellationToken cancellationToken);
}
```

约束：

- Sink 由顶层宿主注入，可以为空。
- SQL、文件和远程分析系统都是宿主 Adapter，不进入 Runtime 核心设计。
- Sink 返回失败时 Recorder 更新健康状态并通知宿主，但不向 Engine 抛出会改变流程结果的异常。
- Runtime 不暴露数据库事务、连接、文件句柄和查询实现。
- 外部系统若需要查询接口，由对应 Adapter 自己提供，不强塞进工作流执行接口。

## 13. 外部保存与清理政策

当前阶段不直接实现 SQLite 或文件保存。Runtime 只提供有序事件推送和有界最近窗口，顶层决定保存方式。

当外部 Adapter 实现有界保存时，必须遵守：

- 新事件优先，容量不足时删除最老记录，不得因保护旧记录而拒绝最新记录。
- 正常 Run 可使用较短保留期，故障、恢复和中断 Run 可使用较长保留期。
- 默认参考值仍可采用正常 30 天、故障/恢复/中断 180 天，但这是 Adapter 配置，不是 Runtime 规则。
- 达到容量上限时持续淘汰最旧记录；记录空间不足不得阻止 Workflow 启动或继续。
- 删除运行事件不能直接删除由 Vision 或业务仓拥有的外部 Artifact。
- SQL、追加文件或其他格式由顶层根据部署环境选择。

## 14. 崩溃检测

宿主启动时查询：

```text
存在 RunStarted
且没有 Completed/Canceled/Faulted 终态
```

若顶层 Sink 支持持久查询，可将其识别为中断运行，并由宿主追加：

```text
RunInterruptedDetected
DetectedAt
LastConfirmedSequence
LastKnownNodeId
```

这不表示工作流可以自动续跑，也不表示最后设备命令没有执行。未配置可查询持久 Sink 时，Runtime 本身不提供跨进程崩溃检测。

## 15. 与现有 Trace 的兼容迁移

- `WorkflowTraceEntry` 的现有字段映射到 `WorkflowRunEvent` 的 `Trace` 类别。
- `NodeTrace` 事件和 `GetTraceBatch()` 第一阶段继续存在，数据来源改为 Recorder 的最近窗口。
- `MaxTraceEntries` 只限制 UI 最近窗口，不限制本地完整记录。
- 现有 `context.Trace(...)` 继续产生 Buffered 事件。
- Engine 自动产生的 `NodeStarted`、`NodeCompleted`、`NodeFailed` 等改由 Recorder 记录；避免同一语义既写旧 Trace 又写新 Event 两次。

## 16. Engine 接入点与顺序

### 16.1 Run 开始

```text
创建 RunId
初始化运行工作集
以 FlushRequested 优先级记录 RunStarted
若后续 Sink 推送失败则更新 RecordingHealth 并通知宿主
发布首个 RuntimeSnapshot
开始调度
```

`RunStarted` 进入 Recorder 后立即开始 Run；后续推送失败不能阻止或终止运行。

### 16.2 节点开始

```text
创建 WorkflowExecutionIdentity
更新节点当前状态
Record NodeStarted
发布 RuntimeSnapshot
创建本次 ExecutionContext
```

普通 `NodeStarted` 使用 Buffered；设备节点的关键命令边界使用 FlushRequested 优先推送。任何推送失败都不改变节点执行结果。

### 16.3 输入解析

```text
解析来源和可见输出
完成类型转换
Record InputResolved
返回最终值给 Handler
```

解析失败则记录来源和失败原因，随后按现有异常路径进入 `NodeFailed`。

### 16.4 节点内部步骤

```text
Handler/Operation 调用 Trace，并按需要标记 Buffered 或 FlushRequested
Recorder 立即形成独立事件并进入有界队列
```

不得缓存到节点完成时统一提交。Trace 不参与节点数据提交回滚。

### 16.5 节点成功

保持现有核心提交顺序，不在本项中引入设备事务：

```text
Handler/Operation 返回成功
释放 Operation 自身资源
应用公共数据和变量变更
提交真实节点输出
Record Variable/PublicData Changes Committed
Record OutputCommitted
Record NodeCompleted
更新节点当前状态
发布 RuntimeSnapshot
沿控制出口调度
```

只有真实输出正式提交后才能记录 `OutputCommitted`。

### 16.6 节点失败

```text
丢弃本次暂存变量和公共数据
不提交节点输出
记录 NodeFailed/FaultOccurred
更新当前故障和节点状态
发布 RuntimeSnapshot
进入恢复或终止
```

### 16.7 恢复

恢复会话每个阶段独立记录，Snapshot 只保留当前阶段：

```text
RecoveryStarted
RecoveryDecision
RecoveryRejected / RecoveryApplied / RecoveryStopped
```

### 16.8 Run 结束

```text
停止产生新节点事件
清理 Pending Operations
确定真实 Run 终态
尝试 FlushRequested 推送 Run terminal event
Recorder 尽力 Complete/Flush
发布最终 RuntimeSnapshot
```

需要在实施时仔细确定“清理失败”和终态记录顺序，不能先记录 Completed 后又发现关键清理失败。终态事件或 Flush 失败只影响 RecordingHealth，不改写已经确定的 Workflow 终态。

## 17. 并行顺序

多分支会并发产生事件。Recorder 必须保证：

- 同一个 Run 的 Sequence 唯一；
- Sink 收到的批次按 Sequence 排序；
- FlushRequested 事件触发立即调度，并与它之前已接受的 Buffered 事件按 Sequence 一起推送；工作流不等待 Sink 确认；
- 不用 Sequence 推导不存在的业务因果。

并行真实因果还依赖：

```text
TokenId
ScopeIds
SourceOutputSequence
OperationId
RecoveryCaseId
```

Sequence 表达 Recorder 观察顺序；数据绑定关系表达 A 输出被 B 消费的因果关系。

## 18. 保留与清理

Runtime 只配置：

```text
最近事件内存窗口大小
Buffered 队列容量
最大单事件字节数
最大字符串长度
最大集合项数
```

内存窗口和待推送队列达到容量时始终淘汰最老数据，为最新事件腾出空间，并累计丢弃计数。

外部 Sink 自己配置容量和保留期。建议故障、恢复和中断 Run 比正常 Run 保留更久，但任何保留政策都不能以拒绝最新记录或阻止 Workflow 运行为代价。

## 19. 分阶段实施

### 阶段 A：缩小 Snapshot

- 删除 Snapshot 的完整 `NodeOutputs` 和 `Faults`。
- 增加 `CurrentFault`、`CurrentRecovery`。
- 已完成 ParallelScope 不再永久留在实时集合。
- 活动 ChildWorkflow 按执行身份实时传播，支持 Studio 双击进入查看；完成后每个父节点只保留最近一次状态，不按执行次数无限累计。
- 保留现有 Run Output Store，不改变绑定和恢复语义。

### 阶段 B：引入 Recorder 与推送 Sink

- 定义 Run Event、类别、写入模式和 Payload 编码规则。
- 使用无 Sink 和测试 Sink 两种模式跑通测试。
- 现有 Trace 最近窗口迁移到 Recorder。
- 定义 RecordingHealth、失败计数和宿主通知。
- 不引入本地数据库，先稳定语义、顺序和 fail-open 行为。

### 阶段 C：自动生命周期和节点事件

- 接入 Run、Node、Fault、Recovery 事件。
- 去除重复 Trace。
- 建立统一 Run Event Sequence。

### 阶段 D：数据血缘

- BindingResolver 返回来源身份。
- 编译或绑定阶段建立稳定输入槽元数据，普通 `ResolveInput(input)` 自动识别目标输入键。
- 显式命名接口只保留给动态或无法唯一识别的输入。
- `OutputCommitted` 自动提取稳定输出键与安全摘要。
- 记录 `InputResolved`、`OutputCommitted` 和成功提交的数据键变化。

### 阶段 E：顶层保存 Adapter（独立安排）

- Runtime 交付稳定的 `IWorkflowRunEventSink`。
- 顶层按部署需要选择 SQL、文件或外部分析 Adapter。
- Adapter 自己实现持久化、查询、清理和可选的中断 Run 检测。
- Adapter 故障通过 RecordingHealth 暴露，不改变 Workflow 运行。

### 阶段 F：Studio 和外部分析

- Snapshot 只驱动实时覆盖层。
- Trace 面板消费最近事件窗口和分页 Reader。
- 支持按节点、事件类型、故障 Case 查询。
- 验证长 Run 下 UI 不因完整历史复制退化。

### 实施状态（2026-09-22）

已完成阶段 A–E 的 Runtime 部分（含阶段 D 数据血缘）：

- 阶段 A：`WorkflowRuntimeSnapshot` 已删除完整 `Faults`/`NodeOutputs`，改为 `CurrentFault`/`CurrentRecovery`，子流程状态拆为 `ActiveChildWorkflows` + `LatestChildWorkflowByParentNode`；已完成 ParallelScope 与已完成子流程不再永久留在实时集合，绑定与恢复语义未改变。
- 阶段 B/C：新增 `IWorkflowRunRecorder`、`IWorkflowRunEventSink`、`WorkflowRunEvent` 系列类型、`WorkflowTracePayloadEncoder` 和 `WorkflowRunRecordingOptions`；引擎统一分配 Run 内序号，接入 Run/Node/Fault/Recovery 事件，原有 Trace 最近窗口迁移到 Recorder，并记录 `OutputCommitted` 以及变量和公共数据提交摘要。
- 阶段 D：绑定阶段按节点类型建立输入槽元数据（`WorkflowNodeInputLayout`），普通 `ResolveInput<T>(WorkflowInput<T>)` 自动识别稳定输入键并记录 `InputKey`/`InputMetadataStatus`/`SourceKind`/`SourceOutputKey`/`SourceOutputSequence`；动态或集合输入改用显式逃生口 `ResolveDynamicInput(inputKey, input)`；无法识别的输入仍正常解析，只把 `RecordingHealth` 降为 `Degraded`。`OutputCommitted` 由 `WorkflowOutputValueExtractor` 自动提取稳定输出键：标量和资源根值统一 `$`，普通结果 DTO 展开第一层公开属性，单个 getter 失败或属性超限只降级诊断。不把内置节点机械迁移到手写名称重载。
- 阶段 E：Runtime 只向顶层注入的 `IWorkflowRunEventSink` 推送，不在 Runtime 中预先绑定 SQL/SQLite/文件；查询、清理和保留策略由顶层 Adapter 负责。
- 记录链路完全 fail-open：`RunStarted`、FlushRequested 推送与 Flush 失败都不改变节点调度和 Run 终态，只更新 `RecordingHealth`、失败计数并通知宿主。
- 阶段 B 的推送颗粒度已按 §10.3 落地为批次级：前台在 `_recordSync` 短临界区内把当前批次分离成不可变批次入队，后台只消费完整批次；封箱条件为满 128 条、批次首条事件起 100ms、追加 FlushRequested 事件、Run 结束与宿主正常关闭。FlushRequested 的立即刷意图随批次携带（不再是全局标记位），通知按批次而不是按事件计数，待发送容量按事件数核算但淘汰单位是整批。公开契约未变。
- `WorkflowEventWriteMode.Durable` 已改名为 `FlushRequested`，明确它只要求立即封包和 Sink Flush，不承诺同步持久化。
- 验收：`WorkflowRunRecorderTests`、`WorkflowRunBatchPushTests`、`WorkflowTracePayloadEncoderTests`、`WorkflowRunRecordingEventTests`、`WorkflowNodeInputLayoutTests`、`WorkflowOutputValueExtractorTests`、`WorkflowRunRecordingLineageTests` 和 `WorkflowVisionOutputRecordingTests` 覆盖 §20 的 Snapshot、Event、崩溃与可靠性、Payload 条目以及实施细节 §13 的血缘验收标准。

尚未完成（后续工作）：

- 阶段 F（Studio 实时覆盖层与分页 Reader、按节点/事件类型/故障 Case 查询）和顶层保存 Adapter 未在本次实现。
- §20 中「100,000 次循环基准」和「具体 Adapter 强制终止子进程的崩溃持久性测试」属于基准与 Adapter 侧验证，未在本次执行。
- 实施细节 §14 明确不验收的事项仍未处理：Sink 写入超时/熔断/永久挂起、Ready 队列淘汰竞态、Sink 阻塞期间独立推进 100ms 封包、操作员身份审计和完整 NodeVersion 迁移体系。

## 20. 验收标准

### Snapshot

- 执行次数增长时，单次 Snapshot 大小不随历史线性增长。
- Snapshot 不含完整 Output、Fault、Recovery 或 Trace 历史。
- 100,000 次循环基准中，Snapshot 累计分配不再呈平方增长。
- Studio 节点状态、活动 Token、并行和子流程显示保持正确。

### Event

- 同一 Run 事件 Sequence 唯一且单调。
- 并行分支事件都带正确 Token/Scope。
- 循环事件带 NodeExecutionCount 和 LoopIteration。
- A 的输出被 B 绑定时，`InputResolved.SourceOutputSequence` 指向正确输出。
- 失败尝试不产生 `OutputCommitted`。
- 恢复重执行后，新输出和失效前输出可以通过事件身份区分。

### 崩溃与可靠性

- 测试 Sink 能收到按 Sequence 排序的事件批次；空批次永远不会写入 Sink。
- 第 128 条事件封装一个 128 条批次，第 129 条进入新批次；Sink 阻塞时也不会按第 129 条及后续每条事件累计空唤醒。
- 一批未满 128 条时，从首条事件起达到 100ms 会封装当前批次，后续输入不会无限延后截止时间。
- 已有 20 条 Buffered 后追加一条 FlushRequested，会封装一个包含 21 条事件且请求立即 Flush 的批次。
- FlushRequested 事件会触发立即封包和调度，不越过此前已接受的批次，并保持全局 Sequence 顺序。
- 事件记录调用不等待外部 Sink；RunStarted、FlushRequested 和 Flush 失败时，Workflow 的节点调度和最终结果不受影响。
- Sink 失败会更新 RecordingHealth、失败计数并通知宿主。
- Buffered 队列有明确上限；满时淘汰最老事件，不会无限占用内存。
- 正常停止会尽力 Flush 已接受事件。
- 具体 Adapter 若声明崩溃持久性，必须单独通过强制终止子进程测试验证。

### Payload

- 大型对象不会被递归完整序列化。
- 图像只记录身份、摘要和可选 Artifact 引用。
- 超长字符串和集合按策略截断并标记。
- 敏感字段被脱敏。
- Payload 编码失败不会覆盖原节点故障。

## 21. 已确认政策

1. 记录采用 fail-open：`RunStarted`、FlushRequested 推送或 Flush 失败都不能阻止、Fault 或改变 Workflow 运行。
2. FlushRequested 优先级用于 Run 生命周期、设备命令关键边界、故障和恢复；普通步骤使用 Buffered。FlushRequested 只触发立即调度，Workflow 不等待外部持久化确认。
3. Recorder 以前台不可变批次作为推送颗粒度：默认满 128 条或从首条事件起达到 100ms 时封包；FlushRequested 事件先加入当前批次再立即封包，Run 结束也封装尾批次。后台只消费完整批次，不按单事件累计唤醒。
4. Runtime 不直接决定 SQL、SQLite 或文件；只向顶层配置的 Event Sink 推送。
5. 有界窗口和有界存储始终优先保留最新数据，容量不足时淘汰最老数据，不因保护期拒绝新记录。
6. 图像、二进制和复杂对象只记录明确标识及必要摘要，不记录完整内容。
7. 当前不在 Workflow 运行事件中记录操作员身份；未来由顶层人工操作模块单独记录和关联。
8. Recording 故障通过健康状态、计数和宿主通知暴露，不能反向影响流程运行。
9. 外部 Adapter 可对正常与故障/恢复/中断 Run 使用差异化保留，但必须遵循“最新数据优先”。
10. 活动子流程快照必须实时传播，支持 Studio 双击进入查看；完成后每个父节点只保留最近一次快照，更早历史通过事件表达。

## 22. 后续实施前仍需给出具体数值的配置

以下不影响接口方向，可在实现和基准阶段确定默认值：

- Recorder 最近事件窗口容量；
- Buffered 待推送队列总容量；
- 单事件、字符串和集合的最大尺寸；
- RecordingHealth 通知的节流周期；
- 顶层保存 Adapter 的具体容量和保留天数。
