# 联合恢复与处置步骤继续：当前实现

本轮交付运行契约与SDK装配，不是“任意已经运行的Studio流程都能自动联合回退”。保持 Document → Compiler → BoundPlan → 运行准备 → Engine → RunState 主干，不引入超级节点或第二套节点调度器。

## 1. 已实现的两条链路

### 多流程联合恢复

`WorkflowJointRecoveryGroup` 显式绑定多个角色、各自已绑定的计划和独立上下文。B故障时：

1. 建立当前联合恢复轮次，立即拒绝旧轮次业务消息。
2. 请求其他参与者在本地节点退出后的边界参与恢复；这不是把B的设备故障复制给A。
3. 每个角色调用强制的 `IWorkflowJointRecoverySafety.EnsureQuiescentAsync`，由工位确认设备退出、操作权和现场条件。
4. 所有角色就绪后，只执行一次工程师配置的处理流程。
5. 核实共同恢复条件；各Engine验证自己的入口、清理原操作、失效相关输出和局部变量。
6. 全部本地准备完成后，再次核实共同条件，统一授权。各Engine仍遵守自己的Pause及其他Hold。

暂停中的参与者也可以响应联合边界中断、报告退出并准备恢复；这不会解除其原有暂停。正在执行的节点不被强行中止，也不被假定已经停止。节点或设备不响应取消时，不能宣称运行已安全结束；宿主必须保留设备停止与安全监管能力。

任一成员终止、取消、退出核实失败、处理失败、本地准备失败或最终共同条件改变，都会使联合恢复失败并取消域内运行，保留独立的 `JointRecoveryFailed:{CaseId}` 阻断。不会恢复已经结束的参与者，也不会自动从头重放处置。

### 处置流程中的操作受阻

`WorkflowWarningHandlerCoordinator` 为每次处置创建专属的 `TreatmentStepCoordinator`，而不是让处置故障重新调用根处理图。

- 当前步骤保留了允许继续的 `IWorkflowNodeOperation`：默认使用现有 `IWorkflowOperatorService` 展示“原故障＋当前步骤＋当前问题”。
- 人员选择“核实并继续当前操作”：同一个操作实例再次执行，前序处置不重放。
- 操作仍受阻：再次呈现当前步骤，不递归创建新的处理图。
- 关闭、不具备核实能力、提交/清理阶段失败或明确停止：保守终止。

处置步骤沿用原 `CaseId`，人工任务仍有各自独立ID。已有WinForms/WPF交互Adapter会呈现这些提示，不需要另一套嵌套弹窗实现。当前默认步骤策略只开放“继续/终止”；工程师替代处置路径的自动切换尚未实现。

## 2. SDK装配

以下变量由宿主提供：两个独立的已绑定串行计划、各自上下文，以及单独编译的工程师处理图。不要将含有处理子文档的整张根图直接作为参与计划传入；当前参与计划拒绝任何子计划。

```csharp
var treatment = new WorkflowWarningHandlerCoordinator(treatmentBlock, nodes, handlers);
using var group = new WorkflowJointRecoveryGroup(treatment, stationSafety);

var sender = group.AddParticipant("发送方", senderPlan, senderContext);
var receiver = group.AddParticipant("接收方", receiverPlan, receiverContext);
var feeder = group.AddParticipant("上游送料", feederPlan, feederContext);

group.AddRestartPlan("重新交接", new Dictionary<string, string?>
{
    ["发送方"] = "发送准备",
    ["接收方"] = "接收准备",
    ["上游送料"] = null // 只阻断等待，不回退该角色
});

var results = await group.RunAsync(stopToken);
```

处理图的 `WarnRestartFromEntry.EntryKey = "重新交接"` 在此表示选择联合方案，由协调模块映射为每个角色的本地命名入口；不是跨图节点ID。选择 `WarnContinueOperation` 则请求所有角色保持各自原路径/原操作，仍需共同核实和本地允许继续。

每个需要重入的角色还必须注册 `IWorkflowRecoveryEntryGuard`，并实际经过其本地入口。方案、角色和上下文在运行开始后冻结；同一组不能启动两次。同一上下文不能被两个角色共享。只有属于本次工艺协作的流程才注册到组内，不相关流程不应加入。

返回的是角色监控与阻断句柄，提供只读Snapshot、恢复事件和失效输出序号，不暴露单独启动、重配置或跳转。Group先预检所有参与计划的能力，再调用运行准备，最后启动各Engine。

### 工位核实不是空实现

`IWorkflowJointRecoverySafety` 的生产实现需要：

- 确认控制调用退出后，设备确实允许进入处置，不是只看流程状态Paused。
- 核实物料编号、持有方、夹持状态、危险区域和操作权限。
- 为本次选择的恢复方案管理设备操作权、握手清理及不可重复副作用。
- 共同条件校验只做核实；移动、复位和补偿放在预设处理步骤中。
- 响应取消并提供设备侧超时；框架不会靠中断线程伪造设备已退出。

测试中的核实实现和假设备仅用于验证协议，不是可复制到生产的安全实现。

## 3. 气缸继续的明确语义

`CylinderControlNodeHandler` 已实现操作工厂，固定气缸名、命令、等待目标、超时和报警编号：

- 第一次调用发送气缸控制命令。
- 命令调用前就记录“已尝试发送”，响应丢失也不盲目重发。
- 继续时只调用 `WaitCylinderAsync` 核实原伸出/缩回目标。
- 不自动重新通气、重新驱动阀或变更原目标。
- `AllOff` 以及未要求等待目标的命令没有可靠的目标核实语义，`CanContinue=false`。

`IWorkflowNodeOperation.CanContinue` 默认保留已有操作工厂契约，但实现遇到无法核实的操作必须返回false。它只是是否允许尝试继续，成功仍必须由操作自身核实后返回。

AxisServo原操作继续不变。并非所有旧设备Handler都自动获得继续能力。

## 4. 协作消息隔离

`Group.Id` 是协作身份，`Group.CaseId` 是本协作预留的故障会话身份，`Group.Stamp` 包含协作身份和轮次。不要与各Engine的RunId或各操作的OperationId混淆。

发出交接命令时保存 `Stamp`；接收对应确认时携带原Stamp：

```csharp
var commandStamp = group.Stamp; // 命令发出时捕获，不能收到回复时重新盖章
// 将commandStamp随业务命令传递，并保留到响应处理

bool applied = group.TryApply(commandStamp, () =>
{
    // 仅短小的内存状态提交，例如写入本轮交接确认。
    // 禁止I/O、等待、启动其他流程或重入Group。
});
```

`TryApply` 将轮次检查和内存提交放在同一把轮次锁内，避免先 `Accepts` 再写入的竞态。提交委托抛异常不回滚它已经写入的状态。`Accepts` 仅适合状态观察，不是独立事务授权。

旧轮次、恢复进行中、失败、结束或释放后的消息均不调用提交委托。这不是消息去重、恰好一次或持久化协议；重复有效消息仍须由业务按交接/消息ID去重。既有普通布尔Signal节点不会自动补上协作身份；相关设备/消息Adapter必须显式接入。

安全传感器和设备事实反馈不能被当作旧业务消息丢弃。此隔离针对推进工艺协作的确认，不代替持续安全监测。

## 5. 观察与失败事实

- `Group.Events`：最多2000条联合阶段事实，返回只读快照，含CaseId、Epoch和角色。
- 每个角色句柄：提供原有本地运行快照、恢复事件、失效输出序号及子流程快照；底层仍使用现有Engine。
- 处置步骤：人工提示展示原故障和当前受阻步骤；不是新的递归处理图。
- 失败后的局部数据可能已部分失效，历史输出仍保留；没有自动回滚公共数据、设备动作和外部业务记录。

当前没有新增多流程Studio工作台或联合策略可视化编辑器，SDK配置不能描述为已经完成桌面集成。现有 `--recovery-demo` 仍是单流程演示，不会悄悄切换成多流程。

## 6. 当前限制与验证入口

已支持：同进程、两个或更多显式角色、无环串行且无子计划的参与流程、统一工艺处置、按角色入口重建、只等待角色、处置步骤原操作继续、独立Hold保留和有身份的业务响应隔离。

未开放：接管任意已运行Host、重叠恢复域及共享资源自动扩散、跨进程、循环/并行/Block重建、外部故障主动上报接口、受控中断任意设备操作、故障会话持久化、动态替代处置方案、完整联合桌面工作台。

契约测试：

```powershell
dotnet test DP.WorkFlow/tests/Workflow/DP.WorkFlow.Nodes.Process.Tests/DP.WorkFlow.Nodes.Process.Tests.csproj --no-restore --filter FullyQualifiedName~JointRecoveryTests

dotnet test DP.WorkFlow/tests/Workflow/DP.WorkFlow.Nodes.Motion.Tests/DP.WorkFlow.Nodes.Motion.Tests.csproj --no-restore --filter FullyQualifiedName~TreatmentCylinderRecoveryTests
```

测试覆盖两/三流程、在途退出、单次联合处置、只等待角色、共同条件二次核实、局部Guard失败、明确StopRun、取消、独立Hold、旧轮次响应、处理图内再次受阻以及真实气缸Handler不重发命令。测试设备仍是软件替身，不是现场安全验收。

最终全量725/725（Process56、Motion12、Windows353），Solution Release、独立WPF Debug/Release零警告错误，工程45/45。Git Bash日志：`/tmp/joint-workflow-final.log`、`/tmp/joint-release.log`、`/tmp/joint-wpf-debug.log`、`/tmp/joint-wpf-release.log`、`/tmp/joint-projects.log`。
