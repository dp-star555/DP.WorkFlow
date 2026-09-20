# 节点异常、处理与恢复：当前机制及安全审查

状态：**下文保留改造前的代码审阅基线，不能再视为当前完整实现描述**。其后已落地[受限V1](nodes/fault-recovery-v1.md)：默认故障策略、原操作契约、命名入口、数据失效、受监管处置子流程和取消修正。FR各项不因此全部关闭；平台总清单见[Node平台架构问题](node-platform-review-backlog.md)。

## 1. 当前真实链路

### 故障入口

- 节点返回成功：执行暂存数据提交、记录标准输出、更新节点状态和Trace。
- 返回`NodeExecutionResult.Fail`：转换为带FaultDisposition的内部路径异常，记录WorkflowNodeFault。
- `WorkflowRecoverableNodeFailure.Create`：只有节点的Interrupt.AlarmCode > 0时生成RequestRecovery，否则StopRun。
- Handler直接抛普通异常：包装路径异常，默认StopRun。实现IRecoverableWorkflowNode、配置AlarmCode并不会自动把所有抛出异常转换成可恢复故障。
- `OperationCanceledException`：引擎当前直接按取消处理，未在catch上检查本次执行令牌是否真的取消。

### 恢复调度

`WorkflowEngine.RunAsync`仅拦截RequestRecovery且宿主存在IWorkflowFaultRecoveryCoordinator的路径异常。

1. 若故障身份包含并行Scope，拒绝恢复，不进入协调器。
2. 检查MaxRecoveryAttempts并递增本引擎运行的恢复次数。
3. 构造请求，增加Recovery Hold。
4. 等待协调器裁决；finally移除该Hold。
5. Stop：结束为故障；Retry：要求Idempotent或ResumeAware；Jump：目标存在并实现IWorkflowRecoveryTargetNode。
6. 从故障节点或指定目标重新调用ExecutePathAsync，复用根Token。

### Process告警分支

`WorkflowWarningHandlerCoordinator`编译专用告警子文档，创建独立上下文和引擎，注入InterruptContext；以WarningResolution变量表达Retry/Jump/Stop裁决，子流程恢复次数设为0。

已有操作包括告警提示、操作员选择/步骤确认、安全点登记、RetryCurrent、JumpToNode和StopCurrentStation。

**现有能力定位：受限的故障路由与恢复裁决，不是自动状态回滚，也不是任意位置断点续跑。**

## 2. 已经存在且应保留的保护

- 普通异常默认停止，不能因为用户设置报警就盲目重试程序错误。
- 节点报告失败时不提交该次暂存变量/公共数据和标准输出；这不代表外部设备动作、信号或前序成功节点可以回滚。
- Retry要求显式安全声明；Jump要求恢复目标标记。
- 并行Scope内恢复目前明确拒绝，不能直接移除保护开放功能。
- 有恢复次数预算；告警子流程不递归恢复自身故障。
- 故障事实有Run/Token/Scope/执行次数等身份，子流程运行也有父级关联快照。

## 3. 关键问题与证据

| ID | 级别 | 代码观察 | 风险/待补约束 |
|---|---|---|---|
| FR-01 | P0 | 普通Exception与显式Fail的恢复入口不同，AlarmCode同时承担路由开关 | 明确业务不通过、运行失败、设备状态未知、配置/编程错误、取消分类；不能让报警编号单独代表可恢复性 |
| FR-02 | P0 | Retry只检查节点模型的安全枚举，再次执行Handler并重新ResolveInput | 声明不是设备状态证明；等待恢复期间公共数据可能改变。明确重用故障输入还是重新读取、部分完成如何确认、命令如何去重 |
| FR-03 | P0 | Jump只检查目标存在和恢复标记，再改变入口并复用根Token | 未在此入口建立完整恢复计划；循环帧、目标并行作用域、跳过的生产者、旧输出有效性等必须专项验证 |
| FR-04 | P0 | SafePoint保存键/节点/时间并可调用宿主登记；引擎Jump不自动调用ReturnToSafePointAsync | 安全点是逻辑标记，不是设备安全状态或完整检查点。跳到安全点不等于轴、产品、夹具、变量已恢复 |
| FR-05 | P0 | Engine无条件catch OperationCanceledException；Block把子运行非Success统一包装成InvalidOperationException | 设备内部超时可能被归为用户取消；子流程取消可能被提升为普通失败。需确定性测试保留取消来源与优先级 |
| FR-06 | P0 | 并行分支先取消同伴并等待WhenAll，恢复入口拒绝有Scope的故障 | 保留拒绝；未来恢复必须先确认同伴退出、设备状态、已提交数据与汇聚资格，不是仅重建分支Token |
| FR-07 | P1 | 协调器自身只编译告警计划，RecoverAsync直接new Engine；该引擎不是主引擎_activeChildEngines里的受监管子运行 | 不能假定告警计划已被主宿主预检/准备；核查Pause、Hold、停止等待、监控和资源释放的一致性 |
| FR-08 | P1 | IWorkflowRecoveryService有RequestRetryAsync/ReturnToSafePointAsync；Retry节点目前写裁决，最终由Engine调度 | 存在容易混淆的两个控制面；明确谁拥有调度权，设备服务只执行恢复动作并确认，不能与Engine各自推进流程 |
| FR-09 | P1 | 决策使用Action/Target/Message；Process侧有OperatorId等信息，但转换后核心裁决不携带完整审计信息 | 建立故障处理会话，记录允许动作、操作员、处理步骤、批准/拒绝和执行结果；防止旧会话响应影响新故障 |
| FR-10 | P1 | 恢复逻辑位于Engine.RunAsync catch块；协调器异常/无效目标可能落到通用终态异常处理 | 分离策略、验证与执行；恢复自身失败必须保留原始故障和新的恢复故障，不能仅用最后一条字符串覆盖 |
| FR-11 | P1 | FaultRecoveryContext可以直接写变量；事件SafeInvoke吞掉订阅异常；数据提交与标准输出写入分步执行 | 明确处理阶段可修改哪些数据及何时生效；监控失败需要独立诊断；提交阶段异常的原子性另建测试，不承诺通用事务 |

上述“未在此入口建立/校验”是代码观察，不声称每种风险都已复现。需通过下面的确定性测试确认，并修复后再宣称关闭。

## 4. 建议的职责分离（尚未实施）

### Fault：发生了什么

核心故障事实应具备稳定身份、完整节点路径/执行身份、阶段（准备/输入解析/执行/提交/恢复）、类别、原因编码、错误链，以及外部动作是否完成/未知的描述。业务NG通常是成功执行后的业务事实，不应默认进入恢复。

### Policy：允许怎么办

根据故障类型、节点契约、设备状态和控制作用域计算允许动作与拒绝原因。UI只展示合法动作，不能让“人工点击确认”绕过引擎安全验证。

### Handling：如何处置现场

告警子流程可执行提示、等待操作员、设备检查及必要恢复动作。处理完毕只提出裁决和证据，不直接修改Engine的下一节点。

### Validation / Execution：能否继续以及怎样继续

唯一恢复执行入口验证裁决属于当前故障会话、未取消、目标合法、输入有效、资源就绪。然后按明确的恢复计划推进；不能只改nextEntryNodeId。

可逐步引入RecoverySession/RecoveryValidator/RecoveryExecutor，但不为命名而增加接口。优先把已有分散职责收拢，不另造第二个调度器。

建议状态语义：故障已记录 → 等待处置 → 处置中 → 恢复验证 → 恢复执行 → 继续；任意阶段可停止/取消；恢复失败保留因果链。该语义尚未成为现有枚举或接口。

## 5. 先明确的安全原则

1. 工作流取消、节点超时、设备状态未知和产品NG不是同一件事。
2. 未确认命令是否完成时，不自动重发有副作用操作。
3. Retry必须明确输入策略；Idempotent保证通常依赖相同输入/命令身份。
4. Jump不是Skip，也不是回滚；输入缺失或状态未恢复时不得继续。
5. 当前不新增“忽略失败后伪造成功输出”。若以后支持跳过，必须有显式输出与业务契约。
6. 操作员决定和引擎授权分离；过期裁决、重复裁决和取消后的裁决不可重新启动流程。
7. Pause/Hold只控制调度，不证明设备停止；软件Stop不替代安全急停。
8. 故障事实保留，即使恢复成功，最终完成也不能抹去发生过的异常和处理记录。

## 6. 确定性验收矩阵

既有测试入口：`tests/Workflow/DP.WorkFlow.Nodes.Process.Tests/RecoveryNodeTests.cs`、`tests/Workflow/DP.WorkFlow.Runtime.Tests/WorkflowParallelRecoveryTests.cs`。已存在安全点、Retry/Jump/Stop、并行拒绝等测试，但不能据此覆盖下列全部情况。

下一轮待补/核对：

- 普通异常不会因AlarmCode自动恢复；显式可恢复失败正确路由；无协调器时安全停止。
- 未声明可重试节点、未知设备完成状态禁止Retry。
- 恢复期间上游公共数据改变，按明确输入策略执行，不意外重发不同命令。
- Jump进入循环体、退出循环、进入并行内部、跳过必需生产者的策略与拒绝。
- 前一次成功输出在后一次失败/跳转后的有效性，不能混入新尝试。
- 当前令牌取消与设备内部OCE区分；Block中的取消传播不变故障。
- 等待操作员时停止；操作员晚返回；重复裁决；处理子流程忽略取消或自身故障。
- 告警处理计划缺能力时在处理动作前拒绝，准备资源生命周期一致。
- 多个并行分支失败与同伴取消的故障归属；恢复不在同伴退出前开始。
- 达到恢复预算后停止且保存最初故障；明确预算是每故障、每节点还是每运行。
- 安全点跳转前的设备确认失败、输入不完整时不得运行后续节点。
- 模拟设备记录实际命令次数，证明没有重复物理动作；不只断言最终Success。

## 补充：场景与恢复去向设计

进一步的[运行场景与结构设计草案](node-recovery-scenarios-design.md)覆盖外部主动故障触发、停止要求、持续门禁、串行/循环/并行/Block、处置动作协议，以及恢复后继续原操作或回到前序步骤的差异。回退需要控制、设备/物料、数据、副作用和进入条件组成的恢复计划，不是任意Jump。完整场景仍是目标草案，已实现部分以[异常恢复V1](nodes/fault-recovery-v1.md)为准。

## 7. 本次记录后的推进次序

1. 先用确定性测试锁定FR-01/02/03/05，不放宽现有并行恢复保护。
2. 明确故障分类、Retry输入/副作用策略与Jump恢复计划。
3. 收拢告警子流程生命周期和唯一恢复执行入口。
4. 最后完善操作员工作台、审计记录及更高级的局部并行恢复。

实现与模型变更前另记录正式决策；当前文件仅作为专项基线和问题单。

## 代码依据

路径相对`src/Workflow/`：

- `Kernel/DP.WorkFlow.Runtime/Execution/WorkflowEngine.cs`：RunAsync恢复分支、ExecuteNodeAsync、ExecuteParallelScopeAsync、RunChildWorkflowAsync。
- `Kernel/DP.WorkFlow.Abstractions/Execution/IRecoverableWorkflowNode.cs`、`WorkflowRecoverySafety.cs`、`IWorkflowFaultRecoveryCoordinator.cs`。
- `Nodes/DP.WorkFlow.Nodes.Process/Recovery/WorkflowWarningHandlerCoordinator.cs`、`IWorkflowRecoveryService.cs`、`SafePointNode.cs`、`RetryCurrentNode.cs`。
- `Nodes/DP.WorkFlow.Nodes.Composite/Block/BlockNodeHandler.cs`。
