# 异常处理与恢复 V1

本版落地运行契约、处理子流程及恢复出口；后续已补充[真实人工交互与双平台演示](operator-interaction.md)。仍不是完整的现场故障工作台，也不宣称任意设备、任意图形都可恢复。

## 1. 工程师设计，现场有限选择

处理策略由宿主为所属运行装配 `IWorkflowFaultRecoveryCoordinator`，不由轴/视觉等故障节点挑选处理流程。V1提供 `WorkflowWarningHandlerCoordinator` 执行工程师编写的 `WarningHandlerBlockNodeModel.SubDocument`。

主流程示例：

```text
进料 → 恢复入口(OperationStart) → 准备 → 轴动作 → 后续
```

处理子流程示例：

```text
处理入口 → 人工选择（工程师配置的三个分支）
             ├─ 继续原操作 → ContinueOperation
             ├─ 人工回初始点 → 操作步骤确认 → 现场检查 → RestartFromEntry(OperationStart)
             └─ 停止 → StopCurrentStation
```

选择节点只沿配置的端口路由，不再根据“Stop”等按钮名字暗中写入停止裁决。大小写不同的返回键会归一到配置键；未配置的键拒绝。关闭窗口、取消任务不能被宿主适配器当作默认同意。

## 2. 新增三个节点

| NodeType | 显示名称 | 语义 |
|---|---|---|
| `RecoveryEntry` | 恢复入口 | 主流程中的命名入口，必须实际经过才可以重入 |
| `WarnContinueOperation` | 继续原操作 | 请求继续保留的逻辑操作，不重建输入意图 |
| `WarnRestartFromEntry` | 从恢复入口重执行 | 请求进入指定命名入口，不是任意NodeId跳转 |

`EntryKey` 在当前文档内非空且唯一；编译器检查重复键。三个节点通过现有 Process Module 注册，Process模块现为30种节点，使用已有属性编辑和端口机制。

## 3. 普通失败、异常与终止

- `NodeExecutionResult.Fail(message)` 默认采用 `HandleAtScope`：有协调器则进入处理，没有则终止。
- 普通Handler异常同样进入所属运行的处理策略，不要求节点声明可恢复或填写报警编号。
- `WorkflowRecoverableNodeFailure.Create` 的报警编号只是可选信息，没有编号也可处理。
- 显式 `StopRun` 仍是不可恢复的终止要求，不能被通用人工重试绕过；故障事实照常记录。
- `MaxRecoveryAttempts = 0` 仍禁止进入协调器。后续实现已让处理子流程使用专属受限步骤策略：只允许核实并继续保留操作或终止，不递归调用根处理图，见[联合恢复与处置步骤继续](joint-recovery.md)。
- 只有当前执行取消令牌实际取消时，OCE才解释为该次执行取消；无关OCE作为节点异常。
- Block返回子取消后先检查父取消令牌，不把正常取消包装成父Block代码故障。
- 代码异常进入人工处理不意味着可以忽略异常、产生虚假输出或热修改已冻结计划。

## 4. 继续原操作的扩展契约

需要保持意图的Handler实现可选 `IWorkflowNodeOperationFactory`：

```csharp
ValueTask<IWorkflowNodeOperation> CreateOperationAsync(
    Guid operationId,
    IWorkflowNodeModel node,
    IWorkflowNodeExecutionContext context,
    CancellationToken cancellationToken);
```

工厂固定输入、设备/业务身份及原始意图，返回独占的 `IWorkflowNodeOperation`。引擎调用：

```csharp
ValueTask<NodeExecutionResult> ExecuteAsync(
    IWorkflowNodeExecutionContext context,
    CancellationToken cancellationToken);
```

约束：

1. 创建阶段不得发出产生业务副作用的命令；创建失败时由创建者清理尚未交给引擎的租约。
2. 正常进入节点创建新操作；继续故障操作复用实例和OperationId，每次传入新的暂存上下文。
3. 实现自行保存不可变意图、完成证据和必要租约，不能保留旧尝试的执行上下文，也不能存到共享Handler或NodeModel。
4. 再次Execute表示完成原操作；不能在未知状态下盲目重发相对运动、取料或写入。
5. 成功、放弃、取消、终止时引擎释放操作。成功输出必须有独立生命周期：先释放操作自身资源，再正式提交输出，避免清理失败后仍发布本次结果；清理失败不应掩盖原故障，不能继续重放已经开始提交/清理的操作。
6. OperationId可以关联外部任务，但不自动提供恰好一次语义；跨新操作/新Run的业务去重仍由领域实现。

`AxisServoNodeHandler` 已接入：固定轴地址、目标使能状态、等待参数、输出键和能力实例。任意 `AxisAction` 步骤、机器人取料等尚未自动变成可继续操作，不能因为装配了处理流程就安全重发。

旧 `RetryFaultedNode` 保留显式安全声明约束；新工程优先采用 `ContinueOperation`。普通第三方Handler仍需对自己的Retry输入和副作用负责，平台不声称已经能通用深复制任意绑定值。

## 5. 命名入口重执行

V1只开放：**当前文档、无环串行计划、当前Token已经经过的入口**。

拒绝：循环/普通回边图、含并行作用域的计划、跨已执行Block范围、缺少入口、未经过入口及缺少工位验证能力。

运行时生成 `WorkflowRecoveryEntryPlan`，包含入口、故障、已经受影响的节点和需要失效的局部变量键。`AffectedNodeIds` 是已观察的执行范围，不是所有未来分支的静态重放列表；重新执行仍遵循原图控制出口。

必须由宿主提供 `IWorkflowRecoveryEntryGuard.ValidateAsync`，核实最新的：

- 初始位置、互锁、夹持与工件身份。
- 已完成副作用是否允许重复、去重或已经按工艺处理。
- 信号、共享资源和公共数据是否仍满足重入条件。
- 被删除的局部数据及派生输入能否在入口之后重新建立。

Guard只验证；移动、补偿、重新放置等动作由处理子流程执行。不能用恒返回true的实现冒充设备安全验证，验证与后续命令之间的资源/互锁约束仍需设备领域保证。

验证通过后：

1. 放弃并释放故障操作。
2. 入口以来的标准输出标记失效，但保留历史。
3. 删除该段成功节点暂存写入/删除过的局部变量键，不恢复旧值。
4. 清除该段旧入口登记，重新经过入口后重新登记。
5. 从该入口重新运行，新的逻辑操作获得新身份。

入口之前的输出仍有效；旧失效输出不能通过latest查询或正常绑定再次读取。公共数据、设备动作、外部数据库、已发信号**不自动回滚**，直接修改共享对象也无法自动撤销。

## 6. 拒绝、失败与审计

- 恢复请求被拒绝时保持原故障，携带 `RecoveryFailure` 再调用所属处理策略；CaseId保持不变，不先重跑主流程。
- 处理策略必须读取上次拒绝原因，从检查/有限人工选择重新开始；不能把重新进入处理入口理解为允许盲目重复处置动作。
- **处理子流程不自动从头重放。** 后续已支持可核实操作在当前处置步骤原地继续；没有保留操作、`CanContinue=false`、提交/清理阶段失败、显式StopRun或步骤策略自身失败，仍记录处置失败并保守终止。原故障会话保持关联，见[最新实现](joint-recovery.md)。
- 继续后节点再次故障是新的故障事件，可通过稳定OperationId关联；不是清除历史。
- `RunState.RecoveryEvents` 记录Waiting、Rejected、Applied、Stopped；Trace同步记录CaseId和OperationId。
- `RunState.InvalidatedOutputSequences` 显示哪些历史输出已失效。
- 处理次数有全Run上限，重复拒绝不会无限运行。

## 7. 处理子流程的监管

`WorkflowWarningHandlerCoordinator` 预先绑定Handler。通过引擎调用时，子流程进入父引擎监控，执行能力预检与运行准备，继承取消、人工Pause和其他外部Hold，但不继承本会话用于停驻主流程的Recovery Hold。

`FaultRequest` 变量提供CaseId、OperationId、循环帧、异常类型和上次拒绝原因；原 `InterruptContext` 仍用于提示与已有节点。

StopCurrentStation的宿主停止请求使用原故障执行身份，而不是把处理子运行身份误当成故障运行。软件停止不等于设备已安全停止。

## 8. 宿主装配

```csharp
var coordinator = new WorkflowWarningHandlerCoordinator(
    warningBlock, nodeCatalog, handlerCatalog);

services.Add<IWorkflowFaultRecoveryCoordinator>(coordinator);
services.Add<IWorkflowOperatorService>(operatorAdapter);
services.Add<IWorkflowRecoveryEntryGuard>(stationEntryGuard);
```

`warningBlock` 是工程师编写的处理文档；V1不会仅因主文档中放置一个WarningHandlerBlock就自动装配它。不同工位/子流程的策略路由仍由宿主配置。

`operatorAdapter` 需要实现真实、可取消的有限选择和确认。现已提供WinForms/WPF交互适配器，样例自动确认DemoOperatorService已移除；见[人工交互装配](operator-interaction.md)。尚未提供远程操作员权限系统或完整故障工作台。

## 9. 明确未开放的能力

- 外部故障事件的统一Case接口、持续条件句柄和受控中断；现有Pause/Hold/Cancel保留，不等价于这些新能力。
- 并行故障恢复、跨循环/Block的检查点重建；现有并行恢复拒绝保留。
- 完整物理设备安全停止、资源租约交接、人工移动路径规划。
- 通用已完成结果重建、补偿事务、断电/进程重启恢复。
- 全部设备节点的操作工厂接入、全功能故障工作台、自动策略选择器。

循环体在现有Token上继续原操作已验证，但不能据此宣称支持循环回退。

## 10. 兼容变化与验证入口

- 旧 `JumpToNode` 已被引擎拒绝，旧标记不再提供跳转权限；模型和稳定NodeType保留以显示旧配置，工具箱标注已禁用。
- 未填报警编号和普通异常现在可进入配置的处理策略；显式StopRun保持终止。
- Retry/Jump出口只写裁决，不再错误要求未使用的IWorkflowRecoveryService能力。
- Process模块由27种增至30种节点。

可执行范例和契约测试：

- `tests/Workflow/DP.WorkFlow.Nodes.Process.Tests/RecoveryV1Tests.cs`：工程师选择分支、假轴命令次数、继续时输入不重读、人工回初始点、数据失效、守卫拒绝、取消晚返回、处理失败不重放、预检、Pause/Hold、循环第7轮及非法恢复入口。
- `tests/Workflow/DP.WorkFlow.Nodes.Motion.Tests/AxisActionNodeTests.cs`：真实伺服Handler接入原操作契约。
- `tests/Workflow/DP.WorkFlow.Nodes.Composite.Tests/BlockNodeTests.cs`：Block取消不转父故障。
- `tests/Workflow/DP.WorkFlow.Runtime.Tests/WorkflowParallelRecoveryTests.cs`：并行保护仍生效。

V1运行内核基线验证：全量测试685/685（Windows339）；Solution Release、独立WPF Debug/Release零警告错误；Solution工程43/43。未改独立DP.Vision，未重跑其独立verify。

日志：`/tmp/recovery-v1-workflow-final.log`、`/tmp/recovery-v1-release.log`、`/tmp/recovery-v1-wpf-debug.log`、`/tmp/recovery-v1-wpf-release.log`、`/tmp/recovery-v1-projects.log`（Git Bash /tmp）。

假轴命令计数验证调度与恢复语义，不代替现场运动、机器人或安全互锁验收。
