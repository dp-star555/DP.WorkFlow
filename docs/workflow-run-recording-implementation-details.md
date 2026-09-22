# Workflow 运行记录后续实施细节

> 状态：已实施（2026-09-22）
> 范围：自动输入血缘、自动输出键值、记录模式命名收敛
> 关联设计：[`workflow-run-recording-design.md`](workflow-run-recording-design.md)

## 0. 实施结果

§11 的五个阶段已全部落地，未改变节点调度、批次推送和恢复语义：

| 阶段 | 落地位置 |
|---|---|
| 1 术语收敛 | `WorkflowEventWriteMode.FlushRequested`（Abstractions）、Run/Recovery 使用点、Recorder 封箱判断、Sink 参数说明与全部测试/文档 |
| 2 输入槽元数据 | `Runtime/Preparation/WorkflowNodeInputLayout.cs`、`WorkflowRuntimeBinder.Bind`、`WorkflowBoundExecutionPlan.GetInputLayout` |
| 3 自动 InputResolved | `WorkflowNodeExecutionContext.ResolveInput`/`ResolveDynamicInput`、`IWorkflowRunRecorder.ReportDegraded`、`WorkflowRecordingHealth.DiagnosticCount` |
| 4 自动输出键值 | `Runtime/Recording/WorkflowOutputValueExtractor.cs`、`WorkflowEngine.RecordOutputCommitted`、`WorkflowRunRecordingOptions.MaxOutputProperties` |
| 5 内置节点回归 | 仅 `BlockNodeHandler`（映射目标名）和 `FitVisionRobustLineNodeHandler`（`Samples[i]` 集合元素）改用显式动态键，其余内置节点保持普通 `ResolveInput(input)` |

实施中发现并修正的一处偏差：反射 `GetProperties` 对 `new` 隐藏的同名属性只返回最派生一个，因此 `Discover` 改为沿继承链逐层 `DeclaredOnly` 读取，override 链视为同一个槽，真正隐藏同名基类属性才判定为重复稳定键。

测试：`WorkflowNodeInputLayoutTests`（§12.1）、`WorkflowOutputValueExtractorTests`（§12.3 提取规则）、`WorkflowRunRecordingLineageTests`（§12.2 引擎级血缘）、`WorkflowVisionOutputRecordingTests`（§12.3 图像根输出），
以及 §12.4 真实节点集成：`WorkflowRunRecordingNodeIntegrationTests`（Standard `StringCompareNode` 两个输入槽自动键 + 上游成员血缘）、
`WorkflowRunRecordingAxisIntegrationTests`（Motion `AxisActionNode` 绑定槽 + 公共数据血缘）、
`WorkflowRunRecordingRobotIntegrationTests`（Process `WaferRobotMoveNode` 三个绑定槽互不混淆）。
三例集成测试均已用"强制丢弃自动识别结果"的变异确认变红，不是碰巧通过。

## 1. 本轮目标

本轮把数据血缘能力收进 Runtime 深模块，使普通节点 Handler 继续使用最小接口：

```csharp
var value = context.ResolveInput(node.Value);
return NodeExecutionResult.Continue(output: result);
```

Runtime 自动完成：

- 识别目标节点的稳定输入键；
- 解析 Literal、NodeOutput、PublicData 来源；
- 关联来源节点输出提交序号；
- 记录 `InputResolved`；
- 从正式提交的输出中提取稳定键和值摘要；
- 记录 `OutputCommitted`；
- 记录元数据识别失败时保持 fail-open。

节点作者不需要重复手写输入或输出名称。

## 2. 已确认决策

1. 普通输入键自动取自节点模型的公开顶层 `WorkflowInput<T>` 属性名。
2. 不递归扫描嵌套对象和集合；动态、集合或无法静态识别的输入使用显式动态接口。
3. 同一个 `WorkflowInput` 实例不能同时属于两个普通输入槽；绑定阶段 fail-fast。
4. Literal、NodeOutput、PublicData 每次真实解析都记录 `InputResolved`。
5. 无法识别输入槽时，输入仍正常解析；记录降级诊断，不允许改变节点结果。
6. 输出 DTO 自动提取第一层公开可读属性；不递归展开未知对象。
7. 标量根输出使用统一键 `$`。
8. `Durable` 改名为 `FlushRequested`，明确它只要求立即封包和 Sink Flush，不承诺同步持久化。
9. 本轮不修改“Sink 阻塞时 100ms 定时封包不能独立推进”的行为。
10. 本轮不处理 Ready 队列超容量淘汰的窄并发竞态，也不增加 Sink 写入超时或熔断。

## 3. 目标事件模型

### 3.1 OutputCommitted

节点 A 正式提交：

```csharp
public sealed record AResult(int Output, bool Success);
```

目标事件：

```text
EventType                  = OutputCommitted
NodeId                     = A
OutputExecutionSequence    = 105
OutputType                 = AResult
OutputKeys                 = [Output, Success]
OutputValue.Output         = 10
OutputValue.Success        = true
```

标量输出：

```text
OutputKeys                 = [$]
OutputValue.$               = 10
```

复杂属性只记录安全摘要或引用：

```text
OutputValue.Frame          = { Kind=Reference, Type=ImageFrame, Text/Identity=FrameId... }
```

失败、取消或提交前异常不得产生 `OutputCommitted`。

### 3.2 InputResolved

节点 B 的属性：

```csharp
public WorkflowInput<int> Value { get; set; }
```

绑定到 `A.Output` 后，目标事件：

```text
EventType                  = InputResolved
NodeId                     = B
InputKey                   = Value
InputMetadataStatus        = Automatic
SourceKind                 = NodeOutput
SourceNodeId               = A
SourceOutputKey            = Output
SourceOutputSequence       = 105
PublicDataKey              = null
TargetType                 = System.Int32
ResolvedValueSummary       = 10
```

Literal：

```text
InputKey                   = Value
SourceKind                 = Literal
SourceOutputSequence       = null
ResolvedValueSummary       = 10
```

PublicData：

```text
InputKey                   = Value
SourceKind                 = PublicData
PublicDataKey              = Product.Count
SourceOutputSequence       = null
```

无法自动识别但解析成功：

```text
InputKey                   = null
InputMetadataStatus        = Unresolved
```

这种情况更新 RecordingHealth 为 `Degraded`，但不改变解析值、节点执行或 Run 终态。

## 4. 输入槽元数据模块

### 4.1 Seam 位置

输入槽发现放在 Runtime 绑定阶段，而不是分散到各节点 Handler：

```text
WorkflowRuntimeBinder
    └── WorkflowNodeInputLayout
            └── WorkflowNodeInputSlot[]
```

理由：

- `WorkflowInput<T>` 属于节点配置语义，但自动记录是 Runtime 职责；
- `WorkflowRuntimeBinder` 已经在 Run 前递归遍历全部根节点和子流程节点；
- 可以在启动前发现重复输入实例和不合法输入槽；
- Handler 接口保持不变，复杂度集中在一个深模块中。

### 4.2 内部类型

建议新增：

`src/Workflow/Kernel/DP.WorkFlow.Runtime/Preparation/WorkflowNodeInputLayout.cs`

```csharp
internal sealed class WorkflowNodeInputLayout
{
    public static WorkflowNodeInputLayout Discover(Type nodeType);

    public WorkflowNodeInputMap Bind(IWorkflowNodeModel node);
}

internal sealed record WorkflowNodeInputSlot(
    string Key,
    Type ValueType,
    Func<IWorkflowNodeModel, object?> ReadInput);

internal sealed class WorkflowNodeInputMap
{
    public bool TryGetKey(object input, out string key);
}
```

`WorkflowNodeInputMap` 使用：

```csharp
Dictionary<object, string>(ReferenceEqualityComparer.Instance)
```

不能使用 `WorkflowInput<T>` 的值相等语义，否则两个内容相同但属于不同属性的输入会被错误合并。

### 4.3 发现规则

`Discover(Type nodeType)` 只接受：

- public instance property；
- getter 非空；
- 非 indexer；
- 属性类型的泛型定义是 `WorkflowInput<>`；
- 包括从节点基类继承的公开属性；
- 按属性名 Ordinal 排序，保证诊断和测试稳定。

不扫描：

- 私有字段；
- 嵌套配置对象；
- 数组和集合元素；
- Handler 局部变量；
- 运行时临时创建的 `WorkflowInput<T>`。

若继承层次中出现重复稳定键，应在 `Discover` 时拒绝。

### 4.4 绑定规则

`Bind(node)` 调用已缓存访问计划，读取当前冻结节点执行副本中的输入实例，并建立实例到键的映射。

以下情况在 Runtime Binder 阶段 fail-fast：

- 两个输入属性引用同一个 `WorkflowInput` 实例；
- 输入属性 getter 抛出异常；
- 同一个节点类型产生重复输入键。

错误信息必须包含：

```text
NodeId
NodeType
冲突属性名
```

空输入属性沿用现有配置验证语义，不在记录模块中发明第二套空值规则。

### 4.5 缓存和生命周期

- Type 到 `WorkflowNodeInputLayout` 的发现结果可用 `ConcurrentDictionary<Type, WorkflowNodeInputLayout>` 缓存。
- `WorkflowBoundExecutionPlan` 按 NodeId 保存 Layout，并递归保存子计划 Layout。
- `WorkflowExecutionPlan.GetNodeOrThrow` 每次返回新的节点快照，因此不能缓存某一次节点快照中的输入对象引用。
- 每次创建 `WorkflowNodeExecutionContext` 时，使用 Layout 对本次实际节点快照执行一次 `Bind(node)`，得到只属于本次执行的引用映射。
- 节点执行完成后，映射随 ExecutionContext 释放，不进入全局状态。

## 5. Bound Plan 修改

修改：

`src/Workflow/Kernel/DP.WorkFlow.Runtime/Preparation/WorkflowRuntimeBinder.cs`

`WorkflowRuntimeBinder.Bind` 在解析 Handler 和 Capability 的同时：

1. 按节点运行时类型获取 `WorkflowNodeInputLayout`；
2. 使用 `plan.GetNodeOrThrow(nodeId)` 做一次预绑定验证；
3. 将 Layout 按 NodeId 写入 `WorkflowBoundExecutionPlan`；
4. 对 ChildPlans 递归执行相同逻辑。

`WorkflowBoundExecutionPlan` 增加 internal 查询：

```csharp
internal WorkflowNodeInputLayout GetInputLayout(string nodeId);
```

不把 Layout 暴露给 Studio、Persistence 或节点插件。

## 6. ExecutionContext 修改

修改：

`src/Workflow/Kernel/DP.WorkFlow.Runtime/Execution/WorkflowEngine.Execution.cs`

创建 Context 时传入：

```csharp
_boundPlan.GetInputLayout(node.Id)
```

修改：

`src/Workflow/Kernel/DP.WorkFlow.Runtime/Execution/WorkflowNodeExecutionContext.cs`

构造时执行：

```csharp
_inputMap = inputLayout.Bind(node);
```

### 6.1 普通解析接口

现有接口保持节点调用方式：

```csharp
T? ResolveInput<T>(WorkflowInput<T> input);
```

实现顺序：

1. `ArgumentNullException.ThrowIfNull(input)`；
2. 用引用映射查询 `InputKey`；
3. 创建 `WorkflowBindingSourceDescriptor`；
4. 调用 `ResolveWithSource`；
5. 记录成功或失败的输入解析事件；
6. 返回原解析结果或重新抛出原解析异常。

记录异常不得覆盖绑定解析的原始异常。

### 6.2 动态输入接口

将当前命名重载收敛为语义明确的逃生口：

```csharp
T? ResolveDynamicInput<T>(string inputKey, WorkflowInput<T> input);
```

规则：

- `inputKey` 不能为空白，进入事件前 Trim；
- `InputMetadataStatus = ExplicitDynamic`；
- 不要求该实例存在于静态输入布局；
- 普通内置节点禁止为了省事调用该接口；
- 动态端口、集合元素或脚本输入可以使用。

软件尚未发布，删除原 `ResolveInput(string, input)`，不保留重复的两个显式命名入口。

### 6.3 无法识别输入实例

普通 `ResolveInput(input)` 查不到输入槽时：

1. 仍调用 `ResolveWithSource`；
2. `InputKey = null`；
3. `InputMetadataStatus = Unresolved`；
4. 记录结构化事件；
5. 报告 RecordingHealth Degraded；
6. 不抛出新的记录异常。

该路径用于第三方插件防呆，不作为内置节点正常路径。

## 7. Binding 来源字段

保留 `WorkflowBindingSourceDescriptor`，并把事件字段语义收敛为：

```text
SourceKind
SourceNodeId
SourceOutputKey
SourceOutputSequence
PublicDataKey
TargetType
```

映射规则：

- Literal：`SourceOutputKey = null`；
- NodeOutput：`SourceOutputKey = Binding.MemberPath`；
- PublicData：`PublicDataKey = Binding.PublicDataKey`，`SourceOutputKey = Binding.MemberPath`；
- 根输出：`SourceOutputKey = "$"`。

当前 `MemberPath` 可在本轮直接改名为 `SourceOutputKey`。软件尚未发布，不增加双字段兼容负担。

## 8. 自动输出键值模块

### 8.1 Seam 位置

建议新增：

`src/Workflow/Kernel/DP.WorkFlow.Runtime/Recording/WorkflowOutputValueExtractor.cs`

```csharp
internal sealed class WorkflowOutputValueExtractor
{
    public WorkflowOutputValueSet Extract(object? output);
}

internal sealed record WorkflowOutputValueSet(
    string? OutputType,
    IReadOnlyDictionary<string, object?> Values,
    IReadOnlyList<string> Diagnostics);
```

Engine 只调用一次 `Extract`，不自行反射或判断复杂对象。

### 8.2 根值分类

以下根值使用 `$`，不展开属性：

- null；
- string、char、bool、数字、enum；
- Guid、日期时间、TimeSpan；
- byte[] 和其他二进制数组；
- Array、集合和字典；
- Stream、SafeHandle、IntPtr/UIntPtr；
- Exception；
- 已注册为引用摘要的资源类型。

其他普通结果 DTO 展开第一层公开可读属性。

### 8.3 DTO 属性规则

只提取：

- public instance property；
- 非 indexer；
- getter 非空；
- 第一层；
- 按属性名 Ordinal 排序；
- 最多 `MaxOutputProperties` 个，建议默认 32。

每个属性形成：

```text
OutputValue.<PropertyName>
```

例如：

```text
OutputValue.Distance
OutputValue.AngleRadians
OutputValue.Frame
```

属性值继续交给 `WorkflowTracePayloadEncoder`，由统一策略决定 Scalar、Summary 或 Reference；Extractor 不递归序列化。

### 8.4 Getter 失败

某个属性 getter 抛出时：

- 其他属性继续提取；
- 当前键记录 `[读取失败]` 摘要；
- RecordingHealth 变为 Degraded；
- 不改变已经提交的节点输出和 Run 终态。

节点输出 DTO 的公开 getter 属于节点输出接口，必须是无副作用、非阻塞读取；Runtime 只能捕获异常，不能安全终止恶意或永久阻塞的 getter。

### 8.5 Payload 形状

不把 `OutputValues` 作为普通 `Dictionary` 值交给现有 Encoder，因为 Encoder 会把字典压缩成“n项”摘要。

`RecordOutputCommitted` 应把输出值展开到事件 Payload 顶层稳定键：

```text
OutputExecutionSequence
OutputType
OutputKeys
OutputValue.$
OutputValue.Output
OutputValue.Success
```

`OutputKeys` 只用于枚举；真实值由 `OutputValue.*` 保存。

### 8.6 图像示例

若根输出是 `ImageFrame`：

```text
OutputValue.FrameId = frame-20260922-001
OutputValue.Image   = { Kind=Summary/Reference, Type=IImageSource }
```

不得记录像素内容。后续可由 Workflow-Vision Adapter 注册专用引用格式化器补充 Width、Height、PixelFormat、ArtifactId；Runtime 不直接依赖 DP.Vision。

## 9. RecordingHealth 降级入口

自动元数据和输出摘要失败必须 fail-open，但需要可见。

建议扩展 Recorder 深模块接口：

```csharp
void ReportDegraded(string message);
```

语义：

- 不抛异常；
- `Healthy -> Degraded`；
- 增加独立的 `DiagnosticCount`，不要伪装成 Sink `FailedWriteCount`；
- 更新 LastError/LastFailureAt 或后续改名为 LastDiagnostic/LastDiagnosticAt；
- 复用现有 `HealthNotificationInterval` 节流宿主通知；
- 不写回同一个可能故障的 Sink 作为唯一告警路径。

若不希望扩大 `IWorkflowRunRecorder`，可以增加 Runtime 内部诊断接口，但不要通过具体类型转换调用 `WorkflowRunRecorder`。

## 10. 写入模式改名

修改：

`WorkflowEventWriteMode.Durable`

为：

`WorkflowEventWriteMode.FlushRequested`

同步更新：

- Run 生命周期事件；
- Fault 和 Recovery 事件；
- Trace 重载注释；
- Recorder 封箱判断；
- Receipt 注释；
- Sink 参数说明；
- 全部测试和设计文档。

不改变行为：

```text
Buffered       = 等待正常批次条件
FlushRequested = 先加入当前批次，再立即封包；该批次到达 Sink 时请求 Flush
```

不保留已废弃枚举别名，避免继续传播错误的持久化承诺。

## 11. 分阶段实施顺序

### 阶段 1：术语收敛

- 将 `Durable` 改名为 `FlushRequested`。
- 只做符号和文档迁移，不改变批次行为。
- 运行 Runtime、Standard、Process、Motion、Composite、Vision 测试。

### 阶段 2：输入槽元数据

- 新增 `WorkflowNodeInputLayout`、`WorkflowNodeInputSlot`、`WorkflowNodeInputMap`。
- Binder 建立并验证 Layout。
- Bound Plan 保存 internal Layout。
- 暂不改 `ResolveInput` 行为，先用模块测试固定发现规则。

### 阶段 3：自动 InputResolved

- Engine 向 ExecutionContext 传 Layout。
- 普通 `ResolveInput(input)` 自动记录。
- 命名重载替换为 `ResolveDynamicInput`。
- 未识别输入走 fail-open 降级。
- 删除仅为测试血缘而手写输入名的调用。

### 阶段 4：自动 OutputCommitted 键值

- 新增 `WorkflowOutputValueExtractor`。
- `RecordOutputCommitted` 写入 `OutputKeys` 和 `OutputValue.*`。
- 图像和复杂对象仍经过统一安全编码。

### 阶段 5：内置节点回归

- 不机械修改所有内置 Handler。
- 通过真实 Standard、Motion、Process、Vision 节点证明现有 `ResolveInput(input)` 自动产生血缘。
- 搜索并审查动态创建 `WorkflowInput` 的特殊节点，只在真实动态场景使用 `ResolveDynamicInput`。

## 12. 测试细节

### 12.1 Input Layout 模块测试

必须覆盖：

- 识别一个和多个顶层 `WorkflowInput<T>`；
- 识别继承的公开输入属性；
- 忽略普通属性、indexer、嵌套对象和集合；
- 两个值相同但实例不同的输入得到不同键；
- 同一个实例分配给两个属性时绑定失败；
- 属性输出顺序稳定；
- 子流程节点递归建立布局。

### 12.2 自动输入事件测试

必须覆盖：

- Handler 只调用 `ResolveInput(node.Source)`，仍记录 `InputKey=Source`；
- Literal 记录值和 `SourceKind=Literal`；
- NodeOutput 的 `SourceOutputSequence` 指向真实 `OutputCommitted`；
- PublicData 记录 `PublicDataKey`；
- 根绑定记录 `SourceOutputKey=$`；
- 成员绑定记录 `SourceOutputKey=Output`；
- 解析失败保留原异常，同时产生失败诊断事件；
- 动态输入记录 `InputMetadataStatus=ExplicitDynamic`；
- 未识别输入正常解析，Health 变为 Degraded，Workflow 不 Fault。

### 12.3 自动输出测试

必须覆盖：

- 标量输出记录 `OutputValue.$`；
- null 输出记录 `$=null`；
- DTO 第一层属性自动成为键；
- 属性顺序稳定；
- 嵌套对象不递归展开；
- byte[]、集合、Stream 不展开；
- ImageFrame 至少记录 FrameId，不记录像素；
- 某个 getter 抛出时其他属性仍被记录且 Run 成功；
- 超过 `MaxOutputProperties` 时截断并标记；
- 失败执行不产生 `OutputCommitted`。

### 12.4 真实节点集成测试

至少选择：

- Standard：`StringCompareNode`；
- Motion：一个 Axis 绑定输入节点；
- Process：一个 Robot 或 ProductFlow 节点；
- Vision：一个 Frame 输入节点。

测试不得在 Handler 中添加手写 InputKey；必须通过现有普通 `ResolveInput(input)` 获得事件。

## 13. 验收标准

实施完成必须同时满足：

1. 内置普通 Handler 不需要手写输入名。
2. 每次普通 `ResolveInput(input)` 都能自动产生稳定 `InputKey`，或明确记录 Unresolved 降级。
3. NodeOutput 输入能关联到准确的 `SourceOutputSequence` 和 `SourceOutputKey`。
4. Literal 和 PublicData 输入同样可查询。
5. `OutputCommitted` 自动提供稳定输出键和值摘要。
6. 标量输出统一使用 `$`。
7. 图像、二进制、集合和资源对象不被完整序列化。
8. 输入元数据或输出摘要失败不改变 Workflow 业务结果。
9. `WorkflowEventWriteMode` 不再使用会暗示同步持久化的 `Durable` 名称。
10. Snapshot、批次队列、节点提交和恢复语义不发生变化。
11. Runtime 及所有内置节点模块测试通过。

## 14. 明确不验收的内容

本轮完成不代表以下事项完成：

- Sink 写入超时、熔断和永久挂起处理；
- Ready 队列淘汰竞态修正；
- Sink 阻塞期间独立推进 100ms 封包；
- SQL、文件或远程保存 Adapter；
- Studio 历史分页查询；
- 操作员身份审计；
- 完整 NodeVersion 迁移体系。
