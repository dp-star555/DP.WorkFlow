# 节点数据传递与模型关系

本文记录**当前代码已经实现的语义**，用于后续讨论Node、Block、并行、恢复和资源生命周期。它不是未来设想，也不把控制连线称为数据连线。

## 1. 一张总图

```text
设计期
┌──────────────── WorkflowDocument ────────────────┐
│ NodeModel                                         │
│ ├─ 普通配置                                       │
│ └─ WorkflowInput<T>                               │
│    ├─ Literal                                     │
│    └─ Binding ── WorkflowBindingKey               │
│                                                   │
│ ControlConnections：只决定调度顺序和控制出口       │
└──────────────────────┬────────────────────────────┘
                       │ 校验、绑定分析、配置快照
                       ▼
             WorkflowExecutionPlan
                       │ Handler绑定、能力需求冻结
                       ▼
           WorkflowBoundExecutionPlan
                       │
运行期                 ▼
             WorkflowEngine / Token
                       │ 创建
                       ▼
       WorkflowNodeExecutionContext（一次尝试）
       ├─ 解析Literal/可见节点输出/公共数据
       ├─ 读取并暂存流程上下文变量
       ├─ 读取并暂存公共数据
       ├─ 获取运行能力
       ├─ 立即触发信号、Trace和设备副作用
       └─ Handler返回NodeExecutionResult
                       │
          ┌────────────┴────────────┐
          │                         │
        Fault                     Success
          │                         │
   不提交暂存数据            清理逻辑操作资源
   不产生节点输出                    ↓
   进入恢复或终止           提交公共数据和上下文变量
                                    ↓
                              提交节点标准输出
                                    ↓
                              按控制出口调度
```

## 2. 必须区分的七类信息

| 类型 | 表达什么 | 不表达什么 |
|---|---|---|
| 节点配置 | 设计者持久化的意图与参数 | 本次运行结果 |
| 控制连接 | 节点执行顺序和出口路由 | 数值传递 |
| `WorkflowInput<T>` | 一个强类型输入取固定值还是绑定值 | 控制顺序、局部变量名 |
| 节点标准输出 | 一次成功节点执行产生的事实值 | 公共状态、正常/异常路由 |
| 流程上下文变量 | 当前`WorkflowContext`中的命名暂存数据 | 节点输出历史、公开数据 |
| 公共数据 | 宿主或成功节点显式发布的共享值 | 所有变量的默认共享副本 |
| 工作流信号 | 本轮运行中的无载荷同步事实 | 业务数据、计数消息、节点输出 |

运行能力、Trace和故障记录也不是上述数据流：运行能力是依赖，Trace/故障是执行事实和诊断。

## 3. 控制流与数据流完全分开

控制连接：

```text
NodeA.Success ──→ NodeB.Input
```

表示A成功并选择`Success`后调度B。它不会把A的输出自动传给B。

B若要读取A的数据，必须在配置中声明：

```csharp
WorkflowInput<int>.FromBinding(new WorkflowBindingKey("NodeA", "Count"))
```

运行时先依据控制结构判断A的输出对B是否可见，再读取公开属性`Count`并转换为`int`。成员路径`$`表示整个输出对象。

因此：

```text
控制上游 ≠ 自动数据输入
存在数据绑定 ≠ 自动建立控制顺序
```

编译期绑定分析会检查来源是否存在、是否保证先执行、成员路径和类型是否合理。运行期仍使用真实Run/Token/Scope再次判断可见性。

## 4. 节点输入模型

`WorkflowInput<T>`当前只有两种来源：

```text
Literal：节点配置中的固定值
Binding：节点输出或公共数据
```

绑定键有两种编码：

```text
NodeId|Member.Path             节点标准输出
$global:PublicKey|Member.Path  公共数据（兼容历史名称）
```

注意：普通`WorkflowInput<T>`**不能直接绑定流程上下文变量**。Handler或脚本可以通过`TryGetVariable`读取变量；Block则提供专门的父子变量映射。

绑定解析规则：

- `$`读取整个根值；其他路径只读取公开、可读、非索引属性。
- 属性名当前不区分大小写。
- 支持直接赋值，以及枚举、Guid、TimeSpan和常见标量转换。
- `null`只能进入引用类型或可空目标。
- 对重复执行的来源节点，读取当前消费者可见的**最新有效输出**，不是绑定某个固定执行次数。

## 5. 节点输出模型

Handler返回：

```csharp
NodeExecutionResult.Continue(portKey, output)
NodeExecutionResult.Complete(output)
NodeExecutionResult.Fail(message, disposition)
```

一次输出事实为`WorkflowNodeOutput`：

```text
ExecutionSequence
RunId
NodeId
NodeExecutionCount
TokenId
ScopeIds
Timestamp
Value
```

只有成功执行才提交输出。失败尝试不会产生成功输出，`False`、未检出或业务不合格如果属于正常结果，应由结构化输出和控制出口表达，而不是伪装成节点故障。

输出值允许为`null`。运行时保存对象引用，不深复制值对象，也不会通用地释放输出资源；图像等资源型输出必须由领域租约契约明确所有权。

## 6. 并行中的输出可见性

普通绑定不会读取任意Token的全局latest。候选输出必须：

1. 属于同一个Run；
2. 尚未被恢复标记失效；
3. 来自当前Token、当前Token的可见祖先，或已经完成的并行分支；
4. Scope路径满足结构化并行关系。

因此，一个并行分支不能在另一个分支尚未完成时随意读取其临时输出。汇聚完成后，后续Token可以读取已完成分支的输出。

同一节点在循环中执行多次时，绑定解析器从历史中反向查找，选择当前消费者可见的最新一次。

## 7. 流程上下文变量

`SetVariable`和`RemoveVariable`在节点执行期间只写入本次`WorkflowNodeExecutionContext`的暂存区；同一次尝试可以读到自己的暂存变更。节点成功后才应用到`WorkflowContext`。

当前实际生命周期是：

- 变量属于`WorkflowContext`，不是节点输出历史。
- 根Host如果复用同一个Context再次运行，变量**不会由`BeginRun`自动清空**。
- 同一Context中的并行Token共享变量字典。
- 并行分支写同一个键时没有冲突检测，最终值取决于提交顺序；不要用共享变量表达需要确定性合并的分支结果。
- Block子运行创建独立Context，默认不继承父变量。

“流程局部”表示不自动跨Context传播，不等于一定只存活一次Run。宿主若要求每轮清空，应创建新Context或显式初始化/删除。

## 8. 公共数据

公共数据通过`IWorkflowPublicDataStore`保存，节点使用`PublishData/RemovePublicData`暂存更新，成功后批量应用。它可以通过`WorkflowBindingKey.FromPublicData`成为普通强类型绑定来源。

当前关系：

- 根Context可以使用宿主提供的公共仓。
- Block子Context共享父Context的公共仓。
- 公共数据不受Token/Scope输出可见性限制。
- 公共仓是否跨运行或跨流程共享，取决于宿主是否复用同一个Store。
- 值对象只复制字典结构，不深复制对象本身。
- 恢复入口重执行不会自动回滚公共数据。

公共数据适合显式发布的工位/产品事实，不适合把所有中间变量都变成全局值。

## 9. 两套信号不能混用

### 9.1 Context运行内信号

`RaiseWorkflowSignal/WaitAllWorkflowSignalsAsync`操作当前`WorkflowContext`中的无载荷、幂等同步事实：

```text
Raise("Ready")
Contains("Ready")
WaitAll(["Ready", "Safe"])
```

根运行开始时，非继承信号域会重置；因此根运行启动前预先触发的信号也会被清除。Block子Context共享父信号域。触发信号是即时动作，不属于节点成功后的暂存提交：节点稍后失败，也不会自动撤回已经触发的信号。

这类信号没有数量、值、物料身份或协作轮次，不能单独承担可靠消息和跨恢复握手语义。

### 9.2 宿名命名信号能力

标准节点还使用另外两套宿主能力：

```text
IWorkflowSignalService       命名布尔状态
IWorkflowValueSignalService  带值和版本的命名状态
```

它们不属于`WorkflowContext`信号域，不会被`BeginRun`自动清空；生命周期由宿主注册的服务实例决定。若多个Workflow Host共享同一个服务实例和相同SignalKey，它们就在读写同一份状态。

当前批量初始化节点会无条件写入默认值，而不是“仅在不存在时创建”：

```text
SignalStateBatchInitialize  → Write(defaultState)
SignalValueBatchInitialize  → WriteValue(defaultValue)
```

因此后启动或重新执行初始化节点的流程会覆盖其他流程已经写入的同名信号。QueueInitialize同样会确保队列存在后直接清空已有内容。这是显式破坏性行为，不能与“声明”或“首次创建”混称为初始化。

## 10. 一次节点执行的提交边界

当前成功路径的顺序是：

```text
Handler/Operation返回成功
  ↓
释放该逻辑操作自身资源
  ↓
提交公共数据变更
  ↓
提交流程上下文变量变更
  ↓
提交节点标准输出
  ↓
节点完成并沿控制出口推进
```

失败路径丢弃本次执行上下文中的变量和公共数据暂存，不提交节点输出。但以下事情不在通用回滚范围：

- 已发送的设备命令；
- 数据库、文件、网络等外部写入；
- 已触发的工作流信号；
- 已写入的Trace；
- 通过共享可变对象引用发生的原地修改。

公共数据、上下文变量和节点输出也不是一个跨仓事务。代码通过先应用可能拒绝的公共数据、再更新内存变量、最后发布输出来降低错误暴露，但不能宣传为全局原子事务。

## 11. Block父子数据关系

Block拥有独立子文档和子Context：

```text
父Context
  │
  ├─ InputMappings
  │  ├─ Literal
  │  ├─ ParentVariable
  │  └─ ParentNodeBinding
  ▼
子Context的变量
  │
  │ 子流程运行（独立RunState和节点输出）
  ▼
  ├─ OutputMappings
  │  ├─ ChildVariable
  │  └─ ChildNodeBinding
  ▼
父Context的变量（在Block成功后暂存提交）
```

Block的额外关系：

- 子Context与父Context共享Services、公共数据仓和工作流信号域。
- 子Context不继承父变量和父节点输出。
- 父流程不能直接绑定子节点输出；必须通过输出映射。
- Block自身会提交一个`BlockNodeOutput`，包含ChildRunId、耗时和映射结果摘要。
- 输入/输出映射的值当前也是对象引用，不做通用深复制。

## 12. 故障与恢复中的数据

节点失败时：

```text
本次暂存变量       丢弃
本次暂存公共数据   丢弃
本次标准输出       不提交
外部副作用         不自动回滚
信号/Trace          不自动回滚
```

继续原逻辑操作时，操作实例和OperationId保留，但每次执行使用新的节点执行上下文，所以失败尝试的暂存数据不会泄漏到下一次尝试。

从命名入口重执行时，当前实现会：

- 保留历史输出事实；
- 将入口以来相关输出序号标记失效，使普通绑定不再读取它们；
- 删除该段已记录的流程上下文变量变更键；
- 不回滚公共数据、信号、设备动作或外部业务记录。

因此“重新从入口执行”是控制和局部数据重建，不是事务回滚。

## 13. 数据作用域速查表

| 数据 | 主要键/身份 | 成功后提交 | 并行可见性 | Block关系 | 新根运行 |
|---|---|---:|---|---|---|
| 节点输出 | Run+Node+Token+Scope+执行次数 | 是 | 结构化限制 | 子运行隔离，显式映射 | RunState重建 |
| Context变量 | 字符串键+Context | 是 | 同Context共享，无冲突检测 | 子Context隔离，显式映射 | 复用Context时保留 |
| 公共数据 | 字符串键+Store | 是 | 全Store可见 | 父子共享 | 复用Store时保留 |
| Context运行内信号 | 字符串键+SignalScope | 否，立即触发 | 同信号域可见 | 父子共享 | 根运行重置 |
| 宿名命名信号 | 字符串键+宿主Service实例 | 否，直接写入 | 共享Service全局可见 | 取决于Services共享 | 不自动重置；初始化节点会覆盖 |
| 图像运行仓/预览 | FrameScope实例+NodeId | Retain/Publish即时生效 | 共享Scope全局可见 | 当前父子共享Services | 每次Root准备清空整个Scope |
| 配置Literal | NodeModel属性 | 不适用 | 不适用 | 由文档快照决定 | 每次使用计划快照 |
| 运行能力 | Service类型 | 不适用 | 由宿主决定 | 当前父子共享Services | 由宿主装配决定 |
| Trace/故障 | Run+执行身份+序号 | 即时记录 | 监控读取 | 子运行独立快照 | 新RunState |

## 14. 后续讨论时统一使用的句式

- “A通过Success端口调度B”——描述控制流。
- “B的Target绑定A输出的Position”——描述数据绑定。
- “节点将中间值写入流程上下文变量X”——描述Context内暂存状态。
- “节点发布公共数据Product.Current”——描述显式共享事实。
- “Block将父变量P映射为子变量InputP”——描述作用域转换。
- “分支输出在汇聚完成后对后续Token可见”——描述并行输出可见性。
- “恢复使输出失效，但不删除历史、不回滚公共数据和设备动作”——描述恢复边界。

不要使用“连线自动传值”“所有变量都是全局的”“失败会回滚节点做过的事情”“Success等于业务合格”等说法。

## 15. 仍需明确的长期政策

以下不是当前代码事实，而是大型系统继续扩展前需要正式决定的政策：

1. 根Host重复运行时，Context变量默认保留还是默认创建新Context。
2. 并行分支写同一Context变量或公共数据键时，是拒绝冲突、声明合并器，还是继续接受最后提交者。
3. 公共数据键由哪个模块声明和拥有，是否需要版本、写权限和生命周期。
4. List、图像、设备结果等引用值进入输出/变量/公共数据后，是不可变值、借用租约还是所有权转移。
5. 长运行中节点输出历史、失效历史和资源型输出何时退役。
6. Block映射是否继续以变量为交换面，还是增加正式的强类型Block输入输出契约。
7. 宿名信号、队列和图像运行仓按Host、Station、Workflow、Run中的哪一级分区。
8. “声明”“不存在时创建”“每轮重置”“强制覆盖”是否拆成不同命令并执行所有权校验。

在这些政策确定前，新增节点应优先使用不可变结构化输出和显式绑定；跨分支、跨流程共享应保持少量且明确。共享的`WorkflowVisionFrameScope`不得同时服务多个独立根运行，批量初始化节点也不得在没有唯一所有权的公共键上执行。
