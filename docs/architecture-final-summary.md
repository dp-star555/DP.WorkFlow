# DP.WorkFlow 架构评审最终总结

状态：**合并结论，取代以下三份文档**（最后更新 2026-09-20，AR-24 已完成 / AR-01 阶段 1 已修复 / 新增 AR-27、AR-28）
- `docs/architecture-smell-review.md`（依赖/内核/UI/构建异味）
- `docs/architecture-review-and-roadmap.md`（AR-01..AR-12 + 阶段 A–D）
- `docs/architecture-review-and-roadmap-critique.md`（对上一份的评审意见）

编号约定：**沿用 AR-xx**，新增项续编 AR-13 起，不引入第三套编号。每条问题的来源（AR / S / C）见 §10.2 溯源表。
本轮续编至 **AR-28**（AR-27 = AR-01 姊妹实例，AR-28 = UI.Windows 套件不确定）。
**进展以各条目标题的【状态标签】与 §1.1 的进展块为准**；未标注即仍为待处理。

本文的原始评审未修改任何生产代码、测试或构建脚本。文中的「已实施 / 已完成」块记录的是
**2026-09-20 另行执行**的修复结果（提交 `8fdd174` / `16f1c45` / `1164725`），与原始评审结论分开阅读。

---

## 1. 总体判断

**领域建模的成熟度远高于同类工业软件，工程的收敛速度远低于文档的产出速度。**

`CONTEXT.md` 的术语体系（工作流文档 / 执行计划 / 运行时绑定计划 / 运行组合目录 / 执行令牌 / 并行作用域 / 恢复准备）严谨自洽，16 篇 ADR 覆盖了关键取舍，主干 `Document → Compiler → ExecutionPlan → RuntimeBinder → BoundPlan → 准备 → Engine → RunState` 值得保留。

问题集中在**概念落地的收口处**，呈现三种同构形态：

| 形态 | 表现 | 典型条目 |
|---|---|---|
| **边界被声明但未被执行** | 依赖规则写在文档里，代码里没有约束；契约存在但没有作用域维度 | AR-01、AR-13、AR-14、AR-21 |
| **职责被命名但未被拆分** | 一个类型承担多个变更原因；一层代码承载两层语义 | AR-02、AR-17、AR-22 |
| **风险被识别但被固化** | 待办清单早已记录，但测试把风险行为写成期望，文档把过程指标写成完成度 | AR-24、AR-25、AR-26 |

一句话概括：**这不是一个"设计得不好"的系统，而是一个"设计得很好但没人守住"的系统。**

### 1.1 风险分布

| 等级 | 含义 | 数量 |
|---|---|---|
| **P0** | 继续扩大生产使用前必须修复，或阻塞其他工作 | 6 |
| **P1** | 节点、流程与团队规模扩大前应治理 | 14 |
| **P2** | 按实际部署与性能指标推进 | 8 |

> **进展（2026-09-20）**
> - **AR-24 已完成** → 阶段 0 关闭。`main` 分支，基线提交 `8fdd174`（768 文件）。
> - **AR-01 阶段 1 已修复**（`16f1c45`）→ 阶段 2 待评估。
> - 新增 **AR-27**（AR-01 的姊妹实例，同日已修复 `1164725`）。
> - 新增 **AR-28**（UI.Windows 套件不可作为门禁，本轮实测发现）。

### 1.2 四件最该先做的事

1. ~~**AR-24 纳入版本控制**~~ — **已完成**（`8fdd174`）。它是其他一切修复的安全网，且 AR-11 与阶段 D 的全部内容以它为前提。
2. **AR-02 冻结语义** — 当前"校验失败"与"已冻结有效"在 API 上不可区分，且子目录冻结不可逆。
3. ~~**AR-01 准备与资源所有权**~~ — **阶段 1 已完成**（`16f1c45`），**验收 4/5 条测试已补齐**（见 §10.5）；阶段 2 的接口拆分待评估。姊妹实例 AR-27 一并修复。
4. **AR-17 快照热路径** — 无条件全量构造 + 与调度共用一把锁，是并发扩展的直接瓶颈。

**下一个待办是 AR-02**（第 2 项）。注意 AR-25 已记录：修复 AR-02 必须同步修改 `WorkflowPluginLoaderTests.cs:53-60` 的测试期望，否则改完即红。

---

## 2. 运行契约与资源所有权

### AR-01 / P0：运行准备与资源作用域混淆【阶段 1 已修复 · 阶段 2 待评估】

**问题定位：不在实现里，在契约里。** `IWorkflowRunPreparationService` 的接口注释原文是：

> 由**根运行宿主**在**每次新运行真正开始前**调用，用于**释放上一轮资源**或准备运行级服务。

`WorkflowVisionFrameScope` 的类注释与之呼应：

> 作为准备服务时**每次新运行释放上一轮仓内租约**，UI 已 Retain 的快照不受影响。

设计意图清楚：**新一轮运行开始 → 清掉上一轮的帧**。这对"根运行"完全正确。但实际有**四处**调用点，其中三处不是"根运行"：

```text
Hosting/WorkflowRuntimeHost.cs:282                    ← 根运行启动          ✓ 符合契约
Execution/WorkflowEngine.cs:538                       ← 恢复子流程（prepare: true）  ✗
Execution/WorkflowJointRecoveryGroup.cs:149           ← 每个联合恢复参与者  ✗
Nodes.Process/Recovery/WorkflowWarningHandlerCoordinator.cs:55 ← 协调器兜底路径 ✗
```

**后三处都不是"新的一轮运行"，而是同一次逻辑运行内部的嵌套运行。** 处置子流程是当前这次运行**中途**为处理故障而起的，此时帧仓里的帧正是本次运行自己刚取、尚未用完的那张。于是"释放上一轮资源"实际执行成了**"释放本轮正在用的资源"**。

**接口为何拦不住**：`WorkflowRunPreparationContext` 只有一个字段 `Nodes`，没有 scope 身份、没有"根还是子"的标志、没有所有权令牌。**即使实现者想写对也写不了**——它无法区分两种调用。这才是"契约问题"的含义：不是某一行写错，而是**接口缺少表达作用域的能力，导致正确实现不可写**。

**完整时序**（生产路径的素材是真实的：`LoadVisionFileNode.cs:38` 把帧仓的 Retain 句柄作为节点输出返回；`WorkflowVisionFrameScope.PrepareAsync`（`:119`）会 `Clear()` 并 `Dispose` 全部帧租约；`WorkflowWarningHandlerCoordinator.cs:36` 用 `new WorkflowContext(context.Services)` 复用原 Services）：

```text
1  父运行 LoadVisionFileNode 取图 → frameScope.Retain(frame)
2  帧进入仓的 _frames，句柄作为节点输出交给下游绑定
3  父运行 fault 节点失败 → RecoverPathAsync → coordinator.RecoverAsync
4  处置子流程以 prepare:true 启动 → PrepareAsync → Clear() → 父帧被 Dispose   ← 破坏点
5  处置完成，父运行继续 → read 节点读取绑定，取回"父节点的输出"
6  该输出指向已释放的帧 → frame.Retain() → ObjectDisposedException
```

样例的实际装配证实了触发条件：`samples/DP.WorkFlow.WinForms.Sample/Form1.cs:96,112` 与 `samples/Legacy/WpfApptest/MainWindow.xaml.cs:66,68` 均把**同一个 `_frameScope`** 同时注册为 `IWorkflowVisionFrameScope` 和 `IWorkflowRunPreparationService`。

**两个让它长期未暴露的原因**：

1. **普通 Block 子流程不走这条路**。`RunChildWorkflowAsync`（`WorkflowEngine.cs:499-500`）传的是 `prepare: false`，只有恢复处置子流程传 `true`。恢复是 ADR-0015/0016 才加入的能力——**它是第一个以 `prepare: true` 运行嵌套子流程的特性**，因此该缺陷是被新功能"点亮"的，不是一直存在的。
2. **UI 会掩盖它**。`Capture(nodeId)` 返回 `current.Frame.Retain()`，是一个**新的独立租约**。UI 持有自己的租约，所以帧仓被清空后**界面仍正常显示图像**——运行数据已死，界面看着却是好的。这印证了"独立保留的 UI 租约可能掩盖运行数据已失效"这一判断。

**归因需收敛到三层**（这是本条的修复前提）：

| 层 | 内容 | 修复面 |
|---|---|---|
| (a) 内核 | 根/子/联合/协调器四处无差别调用扁平准备服务 | `WorkflowEngine` / `WorkflowRuntimeHost` / `WorkflowJointRecoveryGroup` / `WorkflowWarningHandlerCoordinator` |
| (b) 契约 | 准备服务无根/子作用域维度，无法表达所有权 | Abstractions |
| (c) 装配 | 有状态帧仓被注册为准备服务 | 样例与宿主 |

**证据强度**：机制已复现（探针，构造性）；**生产路径未复现**（探针用自定义 `ProbeNode` 模拟，未走 `LoadVisionFileNode`）。

**建议**：建立 RunScope / 子 Scope 的资源所有权契约。设备能力实例、根运行资源、子任务资源不能统称为 Services。**父资源只由其所有者释放**；清理动作应由所有者在**运行结束时**执行，而不是由任意调用者在**运行开始时**执行。不能简单跳过全部子流程准备——能力检查与子任务自身准备仍然必要。

**验收**：采图 → 主操作故障 → 执行处置 → 继续消费原图；父输出有效，子任务完成不清理父资源，结束后租约按所有权恰好释放。

---

**阶段 1 已实施（2026-09-20，commit `16f1c45`）**，方案与证据见 `docs/ar-01-fix-plan.md`。

改法：给准备请求补上它一直缺的作用域维度——`WorkflowRunPreparationContext` 增加**必填**
`WorkflowRunScopeKind ScopeKind`（`Root`/`Nested`）与 `ParentNodeId`，由编译器强制每个调用点表态。

| 层 | 改动 |
|---|---|
| (b) 契约 | `ScopeKind` 必填，无默认值；修正接口注释为"根运行开始、以及任何嵌套运行开始时调用" |
| (a) 内核 | `WorkflowRuntimeHost` → `Root`；`WorkflowEngine` → `Nested` + `parentNode.Id`；`WorkflowWarningHandlerCoordinator` → `Nested`；`WorkflowJointRecoveryGroup` → 暂 `Nested` |
| (c) 装配 | `WorkflowVisionFrameScope.PrepareAsync` 校验与链式调用照常，**仅 `Root` 才 `Clear()`** |

> 上表代码引用中的行号是**修复前**的快照，修复后已发生位移。

**红→绿证据**：新增 4 个回归测试，修复前 `嵌套运行准备不得释放根运行已保留的帧` 失败于
`ObjectDisposedException: ImageBuffer`（`ImageBuffer.Alive()` ← `ImageFrame.Retain()`），
其余 3 个通过；修复后 4/4 通过。全量 729 测试通过，Debug/Release 构建 0 警告 0 错误。

**未完成部分**：

- 阶段 2（拆成 `IWorkflowRunPreparationService` 只校验 + `IWorkflowRunResourceOwner` 只释放）
  尚未实施——这是把误用从"运行期 bug"变成"编译期错误"的关键一步。
- `WorkflowJointRecoveryGroup` 的"一轮"语义仍未定义，当前按安全方向取 `Nested`。若确实需要干净起点，
  应由联合组在"协作开始"单点显式触发一次，而不是每个参与者各触发一次。
- 生产路径端到端复现（验收第 4 条）仍未做：现有测试是构造性的，未走 `LoadVisionFileNode` 全链路。
- 验收第 5 条（结束后租约按所有权恰好释放）尚无对应测试。

### AR-27 / P0：文件夹采集游标被嵌套运行重置【已修复 · AR-01 姊妹实例】

`WorkflowVisionAcquisitionSession` 与帧仓**共用同一批调用点**，也把"新运行归零"无条件执行了：

```csharp
if (next is not null) await next.PrepareAsync(context, cancellationToken).ConfigureAwait(false);
_sequences = prepared;      // ← 无条件替换：清单重新冻结 + 全部游标归零
```

AR-01 只修了帧仓，这条当时被列为"同源遗留"。本轮用确定性假读取器复现，确认它是**独立缺陷**：

```text
根运行准备 → 读 01.png → 读 02.png → 嵌套准备 → 再读
修复前：得到 01.png     ← 游标被拨回起点，根运行重复消费已处理图像
修复后：得到 03.png
```

两个后果都属生产性问题：

1. **重复消费**：嵌套运行（恢复处置子流程）把根运行的文件夹游标拨回起点，根运行恢复后重新处理已经处理过的图像——在检测工位上就是重复判定同一张图。
2. **清单中途变化**：重新冻结目录清单，会让正在运行的文件列表在运行中途改变，与"运行准备时冻结"的设计意图相矛盾。

**修复**（`1164725`）：与 AR-01 阶段 1 同一模式——校验与 `next` 链式调用对所有作用域照常执行，
**只有 `Root` 才冻结清单并归零游标**。新增 3 个回归测试（嵌套不重置 / 根仍然重置 / 嵌套仍然执行 ID 重复校验），
修复前红灯 `Expected: 3, Actual: 1`。

**归因**：这是 AR-01 同一根因的第二个表现面，说明"契约缺少作用域维度"的影响范围**不止帧仓一处**。
排查结论：目前只有这两个类型实现了 `IWorkflowRunPreparationService`，均已修正；
但**根因（接口不表达作用域）仍在**，阶段 2 的接口拆分才能从结构上封住。

### AR-02 / P0：插件冻结具有失败后的"成功外观"【已复现】

`WorkflowRuntimePluginCatalog.cs:89` 的 `Freeze()`：

```csharp
var descriptors = Nodes.Freeze();   // 子目录已不可逆冻结
Handlers.Freeze();                  // 同上
_frozen = true;                     // ← 在验证循环之前置位
foreach (...) { ResolveWithRequirements(...); }   // 后置校验
```

缺少 Handler 时首次抛错，但 `IsFrozen` 已为 `true`；再次 `Freeze()` 在 `if (_frozen) return this;` 直接返回，**跳过全部校验**。

**关键结论（本条的核心）**：`Nodes.Freeze()` 与 `Handlers.Freeze()` 在赋值之前就已执行且**不可逆**，因此**只把 `_frozen = true` 移到后面不足以恢复已冻结的子目录**。修复方案只能是"**在候选目录上完整校验，通过后再发布**"。

同文件 `Register` 先记录 `ExtensionId` 再调用插件写入真实目录，插件中途失败会留下部分注册与已占用的 ID（AR-07 另见）。

**证据强度**：直接代码推导 + 探针（最强）。**且测试把风险行为写成了期望**：`WorkflowPluginLoaderTests.cs:56` 在 `Freeze()` 抛错后断言 `Assert.True(catalog.IsFrozen)`——修复必须同步修改该测试（见 AR-25）。

**建议**：目录具备明确的 `Building / Validated / Failed / Published` 语义。先对候选完整配置校验再发布；做不到事务注册时，应明确让失败目录永久失效。

**验收**：注册/冻结失败后不能得到可运行目录；重试必须得到明确错误，或在全新候选目录重建。

### AR-04 / P0：运行生命周期入口没有形成完整 Interface【竞态分析】

- `Runtime/Hosting/IWorkflowRuntimeHost.cs` 暴露可变 `Engine`，却没有 `WorkflowRuntimeHost` 已有的 `StopAsync`（方法在 `:133`）与 `ResetAsync`（`:231`）。
- `WorkflowRuntimeHost.cs` 中启动、等待停止、Reset 分开协调任务；Reset 等待旧任务期间允许另一个 Run 进入的交错需要专项验证。
- `WorkflowStudioRuntimeBinding.cs:76` 依据 Engine 的 Running/Paused 判断是否重新 Configure；准备期间 Engine 仍可呈 Idle，而 Host 任务实际已开始。
- `WorkflowJointRecoveryGroup.cs` 又维护一套启动、预检、准备、取消和任务监管。

**证据强度**：源码可构造的竞态风险，**本轮未做并发复现**，不能称为已发生的现场故障。

**建议**：以运行尝试身份 + 一个受监管生命周期串行化 `Configure / Start / Stop / Reset / Dispose`；显式呈现 `Preparing`、`Stopping` 及准备失败。UI 只获取控制 Interface 与只读监控视图，不拿 Engine 自行调度。单流程与联合运行复用一套准备/运行资源基础设施，但联合协议不塞进每个节点。

### AR-05 / P1：恢复协议进了内核，工位监管生命周期缺位【已确认结构】

`WorkflowJointRecoveryGroup`、`WorkflowEngine.Recovery.cs`、`WorkflowWarningHandlerCoordinator` 承担了协调职责：自建 Engine、保存方案、执行处置、校验共同条件、维护消息轮次，失败直接取消整组。但**处置任务结束后没有独立的维护阻断会话承接后续操作**，协调中断等待节点边界，不能据此宣称能退出任意在途设备或互相等待的节点。

联合恢复是有价值的受限协议验证，**不应因近期加入就认定它是长期工位架构**；它目前也未与 Studio 的 Host 生命周期统一。

**建议**：在应用级运行管理模块中拥有工位、运行集合、故障会话和生产准入；Engine 只保留本地调度与受控退出/准备/恢复的机制。**处置图是受监管任务，不是监管者。** 界面仍只有工位状态、当前处置和有限操作。

### AR-06 / P1：能力声明、实际实例、设备操作权脱节【已确认结构】

`WorkflowRuntimeCapabilityValidator` 冻结的是**能力需求**；执行时仍从可替换的容器取实例（`WorkflowNodeExecutionContext.cs:45` 的 `GetRequiredCapability` 未把读取限制在已声明集合）。节点包中 `GetService` / `GetRequiredCapability` 共 **93 处**，覆盖 Motion（18 文件）、Process（21 文件）、Standard（12 文件）、Vision（5 文件）。

存在实例也不意味着设备在线、适合当前工件、未被另一流程占用。

**建议**：运行准备生成**固定的能力解析结果**，分别管理设备网关、资源租约与本次操作意图。冻结对象引用不等于冻结物理状态；动作前仍需设备协议核实。节点 SDK 验收"声明了什么、实际取了什么、是否支持并发、何时释放控制权"。

### AR-08 / P1：暂存提交容易被误读为全局事务【已确认结构】

`WorkflowNodeExecutionContext.CommitDataChanges` 依次提交公共数据与局部变量，Engine 随后提交节点输出——**三者的批量更新不等于三个仓的共同原子提交**。且 `WorkflowContext.TryGetVariable` 与公共数据仓读取直接返回对象引用，节点取得 `List`/业务对象后直接修改可绕过暂存写入；"失败不提交"不能自动覆盖这类引用副作用。

**建议**：明确值的不可变/借用/拥有语义、提交范围与并行写冲突政策。可保守保持 `CommitStarted` 后禁止重执行，但 SDK 必须解释部分提交与结果不明。**外部设备/数据库写入属于独立副作用，不能宣传成可自动回滚事务。**

---

## 3. 冻结、快照与不可变表示

### AR-03 / P0：反射快照不能保证任意插件配置的隔离【已复现·构造性】

`WorkflowNodeConfigurationSnapshotter.cs:220` 的 `IsSharedImmutable` 把**所有 `ValueType`** 与 `Delegate` 判定为可共享值：

```csharp
type.IsValueType || value is string or Type or Uri or Version
    || value is Delegate || type == typeof(System.Text.Json.JsonDocument)
```

后果：含引用型成员的 struct 被整体共享（探针以 `readonly record struct Payload(List<int> Items)` 复现：源侧 `Add(2)` 后快照可见）；闭包可能捕获可变状态；共享 `JsonDocument` 还有释放生命周期问题。此外 `MemberwiseClone` 绕过构造函数，`Restore` 逐字段反射回填同样绕过不变量，且**无深度/大小预算**。

另：`WorkflowRuntimeBinder.cs:60` 把能力列表以 `pair.Value.ToArray()` 转 `IReadOnlyList<T>` 暴露，**可被强转回数组修改**。

**严重度待定项**：探针为构造性复现，**未回答"现有节点配置是否含引用型成员的 struct"**。若为空，本条应降为 P1。**需补受影响节点清单**——这比探针本身更有说服力。

**建议**：定义允许进入配置的值类型、集合与资源限制；允许插件提供明确的配置快照/编解码实现；拒绝句柄、服务、委托与不受约束的对象图。补深度/大小预算，逐步形成真正的不可变编译表示，而不是每次执行依赖任意对象图反射复制。

### AR-07 / P1：Document 唯一所有者尚未成为唯一写入口【已确认结构】

`Core/Documents/WorkflowDocument.cs` 的 `CanvasProjection` 仍是过渡投影；`Graph.Nodes` 返回可变模型引用；`UI.Shared/Authoring/Designer/WorkflowDesignerSession.cs` 持有编辑与 Undo 闭包；`WorkflowLayout` 仍用连接顺序索引关联布局。

外部能绕过 Session 修改模型或集合，撤销、脏标记、诊断缓存与导航不一定同时感知。后台批处理、未来脚本编辑和多人协作会进一步放大问题。

**建议**：Document 变更命令/事务、Revision、语义与布局分类变更通知成为共同入口；用**稳定连接 ID 替代长期依赖列表位置**。先让现有 Session 委托该入口，保留过渡投影读取，不一次性重写编辑器。

---

## 4. 监控与性能

### AR-17 / P1：快照在热路径上无条件全量构造，且与调度共用锁【代码推导】

```csharp
private void PublishSnapshot(string? message = null) =>
    SafeInvoke(SnapshotChanged, GetRuntimeSnapshot(message));   // WorkflowEngine.cs:785-786
```

`GetRuntimeSnapshot()`（`:193-230`）在 `lock (_stateSync)` 内用 LINQ `ToDictionary` 构造 4 个只读字典并复制 `Faults` 与 `NodeOutputs`。三个叠加问题：

1. **无订阅者也构造**：`SafeInvoke` 对 null 处理器有保护，但实参**提前求值**——没有订阅者时仍付出全量分配；
2. **每节点两次**：`MarkNodeStarted`（`:733`）与 `MarkNodeFinished`（`:766`）各发布一次，并行分支内每个节点都触发；
3. **与调度共用一把锁**：`GetRuntimeSnapshot` 立刻重入 `_stateSync`，并行分支在此排队。

配合 `MarkNodeFinished`（`:757-763`）每次清空并重建 `_activeNodeIds`（O(活动令牌数)），整体呈现 **O(节点数 × 令牌数) 的分配与锁竞争**。

**建议**：投影职责独立为 `RunStateProjector`；**无订阅者不构造**，有订阅者时按阈值合并发布；调度状态与投影状态**分锁**。

### AR-09 / P1：快照成本随输出历史增长【结构性性能风险】

`Execution/WorkflowRunState.cs` 的 `NodeOutputs => _nodeOutputHistory.ToArray()`（每次访问复制全部历史）；`WorkflowEngine.cs:193,785` 构建快照包含完整输出历史，节点状态变化频繁发布。Studio 合并接收到的快照**发生在构建之后**。

长运行中若每次新增输出都反复复制全部历史，累计复制工作可接近二次增长。`MaxNodeExecutions` 不是大对象、历史保留或发布频率预算。

**证据强度**：未跑性能基准，**不能给出实际吞吐退化数值**。

**建议**：当前状态与审计历史分开；高频监控只发差量/轻量快照，历史按序列分页读取。为输出、故障、Trace 与资源租约分别定义保留策略。**任何优化都不能静默淘汰后续绑定仍需使用的输出。**

### AR-18 / P1：进程级静态可变状态

| 位置 | 内容 | 风险 |
|---|---|---|
| `Runtime/Data/WorkflowBindingResolver.cs:13` | `static ConcurrentDictionary<BindingPlanKey, BindingPathPlan> PathPlans` | **无上界、无失效**；长驻进程持续增长；跨运行实例共享，与"运行实例独立状态"冲突 |
| `Nodes.Standard/Scripting/CSharpScriptNode.cs:179` | `RoslynScriptService.Shared` | 脚本服务成为进程级单例，宿主无法替换或隔离 |
| `UI.Shared/Editors/WorkflowCSharpScriptEditorModel.cs:11` | `static readonly RoslynScriptService Service = new()` | 设计期与运行期各持一套脚本服务，配置漂移 |
| `UI.Shared/.../WorkflowDesignerSession.cs:64` | `static readonly object ClipboardSync` | 同进程多文档共享剪贴板锁 |

`CONTEXT.md` 明确"运行实例：拥有自己的身份、状态、输出历史和故障记录"。进程级静态缓存是**无人拥有的第三层状态**，既不是文档也不是运行实例，却会影响两者行为。

### AR-19 / P2：观察者异常被静默吞掉，无诊断出口

`WorkflowEngine.SafeInvoke`（`:814-829`）吞掉所有观察者异常。"不影响执行"的取舍是对的，但**没有任何诊断出口**——宿主无法得知自己的监控订阅正在持续抛异常，只能看到监控面板空白。应至少计数并通过 `RunState`/Trace 暴露。

---

## 5. 依赖边界

### AR-13 / P1：节点包编译期依赖宿主，插件方向被反转

```text
Nodes.Composite  →  Abstractions + Core + Runtime
Nodes.Process    →  Abstractions + Runtime
Nodes.Standard   →  Abstractions + Platform/Scripting/ScriptEngine
Nodes.Vision     →  Abstractions + 仓外 ../DP.Vision.Algorithms
```

`docs/project-structure.md` §5 声明的目标方向是 `Workflow.Nodes ──→ Workflow.Abstractions`。

`Runtime` 是宿主、节点是插件；插件反向依赖宿主具体程序集后，"启动期成组收集、完成后校验并冻结"的插件模型失去意义——节点包无法脱离内核版本独立编译与发布。

**关于 Composite/Process 引用 Runtime 的正确态度**（沿用你文档第 29 行的判断）：**并不自动等于依赖环**，需要分辨"节点能力"与"应用级编排"，而不是机械禁止全部引用。但**判定标准必须写进 `docs/project-structure.md` §6**——缺少标准正是这条债长期不还的原因。

**ScriptEngine 的隐式内核化**（与上表同行）：`CSharpScriptNode.cs:5` 直接 `using ScriptEngine;`，`:179` 默认构造用 `RoslynScriptService.Shared`。`ScriptEngine` 是多目标（`net48;net8.0`）、自带 Roslyn、自带 Worker 进程隔离与 Workspaces 的**平台级库**——它一旦进入内核引用链，内核编译面被 Roslyn 绑架，`net48` 兼容分支沿链渗透，脚本能力无法替换。

值得注意的是 `Abstractions/Scripting/IWorkflowScriptHostContext.cs` **已经存在**——设计意图本是对的，只是没有落地。

**建议**：能力在 Contracts 中定义为 port（`IAxisController`、`IImageSource` 等）；属于"运行宿主服务"的能力则定义 `Runtime.Hosting.Abstractions`（**只含接口**）。脚本能力经 `IWorkflowScriptHost` 注入，实现与装配留在宿主侧。

### AR-14 / P1：UI.Shared 名为模型层，实为组合根

```text
UI.Shared  →  Abstractions + Core + Runtime + Persistence.Json + ScriptEngine
```

`docs/project-structure.md` §3 描述它是"框架无关的 Authoring、Editors、Diagnostics 和 Monitoring 模型"。同时引用 `Runtime`（执行）与 `Persistence.Json`（存储）意味着：任何编辑体验改动都可能牵动 Runtime 引用面；无法为"纯设计期宿主"复用 Authoring 模型。

**建议**：拆为 `Studio.Model`（纯模型，只依赖 Contracts/Core）与 `Studio.Host`（组合根）。

### AR-15 / P2：平台耦合（WPF 拖入 WinForms；Platform 与 Workflow 共构建）

- `UI.Wpf → ScriptEngine.Wpf → ScriptEngine.WinForms`：WPF 宿主最终拖入 `System.Windows.Forms`，封死跨平台宿主、无 WinForms 精简部署、编辑器组件独立成包三条路线。
- `Platform/Desktop/ModernUI.WinForms`（123 文件 / 20337 行，多目标）与 `Platform/Scripting/*`（6 项目）与 `Workflow` 共用同一 sln 与同一 `Directory.Build.props`。于是工作流的构建需求变成平台的约束（HALCON 探测、`x64`、`TreatWarningsAsErrors` 全量生效）。**两个发布节奏不同的产品被绑成一次构建。**
- `DP.WorkFlow.sln` 含 61 个 `Project(` 条目（40+ 可构建项目），混入 `WinFormsApp_test`（与 `DP.WorkFlow.WinForms.Sample` 语义重复）、`WindowsFormsApp1`（保留默认工程名）、`WpfApptest`（Legacy）。`docs/project-structure.md` §2 自称"WpfApptest **不在主 Solution 中**"，与 sln 实际不符。
- 六种 TFM 组合（`net8.0` × 11、`net8.0-windows` × 6、`net48;net8.0` × 2、`net48;net8.0-windows` × 4、`net8.0;net48` × 1、`netstandard2.0;net8.0` × 1），条件编译分支 ModernUI 8 处、ScriptEngine 11 处以上。而**工作流内核全部是 `net8.0`**。

**建议**：明确回答"`net48` 是业务硬需求还是历史兼容"；若为前者，隔离在 Platform 的独立发布单元。Legacy 样例移出主 sln。

### AR-16 / P1：DP.Vision 跨仓源码引用，无版本锁定

`Nodes.Vision` 与 `Vision.UI*` 使用 `..\..\..\..\..\DP.Vision\src\...` 形式的 `ProjectReference`：构建要求同级目录存在源码，版本无法锁定，两侧改动互相击穿，没有版本号可回溯。单向依赖规则是好的，缺的是**单向的版本化契约**。

**建议**：改为包引用或子模块 + 显式版本锁定。

---

## 6. 节点层、UI 层与工程治理

### AR-20 / P1：设备动作 1:1 映射为节点类型

`Nodes.Motion` 24 个源文件中 18 个是"设备动作 = 一个节点类型"：Axis(4)、CodeReader(4)、IO(6)、Pneumatic(4)；`Nodes.Process` 38 个文件中 ProductFlow(10)、Recovery(7)、Robot(3) 同构。每个类型都需稳定 `NodeType` 键、持久化 schema、工具箱条目、属性面板描述。

**对照**：Vision 侧已用"统一范围能力与公开解析入口"取代"节点类型白名单"（`README.md`），支持外部扩展而不增加节点类型。**模式已被验证，只是没有推广。**

`CONTEXT.md` 已给出方向：**视觉操作**是"跨视觉实现保持稳定的一项原子能力"，节点只是宿主。设备动作同理。

**建议**：节点声明所需能力，具体设备/通道作为**配置 + 绑定**；新增设备 = 新增能力实现 + 配置，**不新增节点类型**。

### AR-21 / P1：UI 关注点进入内核契约

`Abstractions/Nodes/` 中与 UI 直接相关：`WorkflowPropertyAttribute`、`WorkflowPropertyEditorAttribute`、`WorkflowPropertyVisibleWhenAttribute`、`IWorkflowNodeEditorCapabilities`。

但"字段是否可见、用哪种编辑器渲染"是纯粹的呈现决策。放进内核契约意味着：新增一种编辑器控件就要动内核程序集，内核 schema 版本被迫跟随 UI 演进。

**建议**：迁出为独立 `Workflow.Editing.Contracts` + Provider 扩展包，与 Vision 的 `PageProvider` / `RendererKey` 统一为同一套"扩展描述 + 平台 Renderer"机制（`NP-07` 已提出方向）。

### AR-10 / P1：双平台 Adapter 重复承载交互状态机

| 文件 | 行数 |
|---|---|
| `UI.WinForms/.../WorkflowDesignerControl.cs` | 1925 |
| `UI.Wpf/.../WorkflowDesignerControl.cs` | 1621 |
| `UI.Shared/.../WorkflowDesignerSession.cs` | 1456 |

两平台各 73 个方法，方法名高度一一对应：`OnMouseDown/Move/Up/Wheel/DoubleClick`、`OnDragEnter/Over/Drop`、`HitNode/HitPort/HitConnection/HitWaypoint`、`DrawGrid/DrawConnections/DrawNode/DrawPorts/DrawMarquee/DrawOverviewMap`、`BuildOrthogonalPath/AvoidNodeObstacles/Compact/AddDistinct/Offset/DistanceToSegment/NearestSide/StateColor`。

共享的 `WorkflowOrthogonalRouter`（368 行）**确实被两平台调用**，但两平台各自仍保留私有几何副本——例如 WinForms `:1726-1798` 整段 `BuildOrthogonalPath` / `Offset` / `AddDistinct`。**即：纯算法共享了，占约九成的交互状态机、命中测试与绘制组织全部重复。** 拖动、连线、折点、端口落点状态（`_dragNodeOrigins`、`_connectionStart`、`_segmentWorkingWaypoints`）两平台各一份。

**行数不是错误证明**——实际问题是：新增一种交互需要跨两套实现同步状态规则，且同类缺陷容易平台间漂移。

**建议**：共享"手势输入 → 交互状态 → 文档命令"模块，平台只做坐标/DPI、命中所需呈现信息、捕获与绘制。**保留各自渲染器，不做万能跨平台控件基类。** 项目已有正确范式可循：`PageProvider` / `PageId` / `Priority` / `RendererKey`。

### AR-22 / P1：UI 层职责过载

- `WorkflowDesignerSession`（1456 行）承载文档事务、Undo/Redo、选择集、剪贴板、视口、工具箱、运行覆盖层、诊断、脏状态、最近文件、面包屑——**相互独立且变更频率不同的关注点**。
- 属性面板 `UI.WinForms` 1145 + `UI.Wpf` 747 行，而 `WorkflowPropertyInspectorModel`（502 行）已存在——呈现层混入类型推导、候选枚举、可见性判定。
- `ScriptEngine.WinForms/RoslynScriptEditorControl.cs`（1711）+ `RoslynScriptProjectEditorControl.cs`（1189）+ `ScriptCompletionPopup`（376）构成自绘代码编辑器。

**建议**：Session 按文档编辑、选择/视口、布局、运行视图逐步分工。

### AR-23 / P1：构建依赖开发机注册表，不可复现

`Directory.Build.props:2-9` 用 `$(registry:HKEY_CURRENT_USER\Environment@HALCONROOT)` 兜底，注释自述"避免 HALCON 项目被**静默编译成无 SDK 桩**"——即**构建结果取决于当前用户注册表状态**，且该属性组对**所有**项目生效（含与视觉无关的 ModernUI 与 ScriptEngine）。

后果：CI 或新开发机得到"能编译通过但行为不同"的产物；`TreatWarningsAsErrors` + `GenerateDocumentationFile` 全局启用会放大跨框架告警面。

**建议**：改为显式属性 + CI 注入（或 `Directory.Build.user.props` 不入版本控制）；HALCON 边界收敛到 `DP.Vision` 仓。

### AR-24 / P0（阻塞）：项目不在版本控制下【已完成】

```text
DP.WorkFlow/   无 .git（同级 Base/、Halcon_DP.../ 有）
git status   → fatal: not a git repository
```

而 AR-11 与阶段 D 的全部内容以此为前提：「运行制品记录文档修订/摘要、插件与节点版本」、「先支持锁定版本部署和**回退**」、「节点级版本迁移注册、旧文档语料测试」、「运行制品清单、故障审计、发布和回退」。

**没有 VCS，就没有修订、没有回退、没有制品身份。** 更直接的影响：AR-01/02/03/04 都涉及跨程序集、跨 `net48`/`net8.0` 的改动，**没有安全网**。

`.gitignore` 已存在（含 `bin/`、`obj/`、`.tmp/`），说明曾打算使用 VCS。需确认是漏初始化，还是整体拷贝时丢失。

**建议**：新增**阶段 0**，并声明为阶段 A 的**前置条件**而非并列项。

---

**已完成（2026-09-20，commit `8fdd174`）**。

| 项 | 结论 |
|---|---|
| 性质 | **漏初始化**，不是拷贝时丢失（§10.4 第 3 问已关闭） |
| 分支 / 首次提交 | `main` / `8fdd174`，纳入 **768 个文件** |
| `.gitignore` | 已确认覆盖 `bin/`、`obj/`、`.vs/`、`.tmp/`、`.pi-tmp/`、`artifacts/`、`TestResults/`、`*.log`、`.workbuddy-ai/`；新增 `.gitattributes` 统一换行并声明二进制类型 |
| 洁净度核实 | 无厂商二进制、无敏感信息、无日志混入；最大文件 644KB（`docs/paddle-print-quality-workflow.html`） |
| 仓库配置 | 仓库级 `user.name` / `user.email` / `core.autocrlf=false` |

**未处理**：`../DP.Vision` 仍是跨仓源码引用（AR-16），纳入 VCS 后既不是子模块也不是包——
本次未改变其引用方式，AR-16 仍需独立决策。

**新发现（本机环境，与 AR-23 同族，2026-09-20 复核）**：`dotnet restore` 在本机必然失败，报
`NuGet.targets: error : Value cannot be null. (Parameter 'path1')`。根因是
`Environment.GetFolderPath(CommonApplicationData)` 在本机解析不出来，NuGet 随后
`Path.Combine(null, "NuGet")` 抛异常；该异常被静态 `Lazy` 缓存，**同进程内之后所有 NuGet 操作都会失败**，
所以读 `project.assets.json` 也挂。

机器现状：系统环境变量 `ProgramData` / `APPDATA` / `ALLUSERSPROFILE` **在注册表里就不存在**；
但 `HKLM\...\Explorer\Shell Folders\Common AppData` = `C:\ProgramData` 是有的——所以
.NET Framework 能解析、.NET 9/10 不能。
注意不对称：**用户级** NuGet 配置能正常解析（`C:\Users\25845\AppData\Roaming\NuGet\NuGet.Config`
存在且可用），只有**机器级**（`CommonApplicationData`）这一个解析不出来。

**为什么 `dotnet` CLI 崩、Visual Studio 不崩（2026-09-20 查清）**：

| 运行时 | `GetFolderPath(CommonApplicationData)` | 结果 |
|---|---|---|
| .NET Framework 4.0.30319（`MSBuild.exe` / `devenv.exe` / Windows PowerShell 5.1） | `C:\ProgramData` | ✅ 走注册表外壳文件夹，正常 |
| .NET 9 / 10（`dotnet` CLI） | 取不到 → `null` | ❌ `Path.Combine(null, "NuGet")` 崩 |

实测（本机）：Windows PowerShell 5.1（CLR 4.0.30319）下 `GetFolderPath` 六个文件夹全部正常返回；
而 `dotnet restore` 在 SDK 9.0.316 与 10.0.302 上**都**报同一个 `path1` 错误。

**这解释了那批 assets 的来历——它是在本机生成的，由 Visual Studio / `MSBuild.exe` 完成还原，
不是从别处拷来的。** 证据在 `project.assets.json` 自身：

- `project.restore.packagesPath` = `C:\Users\25845\.nuget\packages\`（本机用户目录）
- `project.restore.outputPath` = 本仓库 `obj\` 路径
- `project.restore.configFilePaths` 含本机用户级 `NuGet.Config`
- `project.restore.fallbackFolders` 含 Visual Studio 的 `Shared\NuGetPackages`
- 仓库根有 `.vs/`（2026-09-02 起），`Directory.Build.props` 同为 2026-09-02

且 `SdkAnalysisLevel` 一律 `10.0.300`（SDK 9.0.316 写 `9.0.300`、SDK 10.0.302 写 `10.0.300`；
该值由 SDK 自己写入，与 `TargetFramework` 无关——同一文件里 `targets` / `project.frameworks` 仍是 `net8.0`），
说明还原用的是 SDK 10——与 `global.json` 写的 9.0.308 不一致，间接说明 **`global.json` 是 2026-09-10 之后才加上的**。

注册表佐证：`HKLM\SYSTEM\...\Session Manager\Environment` 的**最后写入时间是 2026-08-11 09:49:55**，
即环境变量缺失这件事在 9 月 9–10 日那批构建之前就已存在——所以那批构建**只能**是 Visual Studio 干的。

**⚠️ 脆弱点（已修正）**：`obj/` 被 `.gitignore` 排除、**不在版本控制里（被跟踪的 obj 文件数为 0）**。
但**它并非不可再生**——用 Visual Studio 打开解决方案即可重新还原/构建。
只有 `dotnet` CLI 这条路不可再生。真正的风险是"**只有 VS 一条路能还原**"这一单点依赖。
可选处置：① 修机器让 CLI 的 `restore` 恢复（首选）；② 文档显式写明"此仓库须用 Visual Studio 还原"；
③ 把 assets 纳入版本控制（不常规）。

**决定性因素不是 SDK 版本，是 NuGet 版本**：

| SDK | 自带 NuGet | `restore` | `build --no-restore` |
|---|---|---|---|
| 9.0.316（仓库内，被 `global.json` 选中） | 6.14.3.1 | ❌ | ❌ `NETSDK1060` |
| 10.0.302（仓库外） | 7.6.0 | ❌ | ✅ |

即：**在仓库目录内连 `build --no-restore` 都做不了**；`restore` 在两个 SDK 上**都**失败。
**因此提升 `global.json` 到 SDK 10 并不能修复 `restore`**，它只能让"在仓库目录内构建"变得可行。
（早前记录的"SDK 10 的 CoreLib 有 `%ProgramData%` 回退、设环境变量即可绕过"**已证伪**：
.NET 10 CoreLib 里确有该字符串，但设了变量 restore 仍失败。）

这进一步支持 AR-23 的判断：**构建环境依赖开发机隐式状态**。详见 §10.4 第 6 问。

### AR-25 / P1：测试把缺陷写成了规范

`WorkflowPluginLoaderTests.cs:53-60`：

```csharp
var error = Assert.Throws<InvalidOperationException>(() => catalog.Freeze());
Assert.Contains("没有已注册的处理器", error.Message);
Assert.True(catalog.IsFrozen);     // ← 断言"失败后仍然是已冻结"
```

测试锁定了 AR-02 的风险行为。这意味着**修复 AR-02 必须同步修改测试期望**，否则改完即红。这不是单个测试的问题，而是**测试把待修缺陷固化为契约**的治理问题——同类风险应系统性排查。

### AR-28 / P1：UI.Windows 套件不确定，不能当作门禁【已复现·抖动】

`DP.WorkFlow.UI.Windows.Tests` 是本仓库最大的测试套件（353 例），但**同一份二进制重复运行结果不同**：

```text
2026-09-20 同一次会话，改动前后各跑一次全量：
  运行 1：353 通过 / 0 失败
  运行 2：352 通过 / 1 失败
  运行 3：350 通过 / 3 失败

对其中 3 个用例连跑 3 次（代码与二进制完全未变）：
  第 1 次：通过 3    第 2 次：通过 3    第 3 次：失败 1
```

已定位的抖动用例（均属 `ModernControlBehaviorTests`）：

| 用例 | 断言内容 |
|---|---|
| `CheckedListBoxClickDoesNotEraseTheWholeBackgroundOrQueueASecondFullRepaint` | 重绘次数落在 0–1 |
| `Select_KeyboardNavigation_HomeEndAndPageKeysUseVisibleRowCount` | 期望值 0，实际 3 |
| `DatePickerManagedPopupStaysOnScreenAndClosesWhenOwnerMoves` | 弹窗位置布尔断言 |

**为什么是治理问题而不是普通 flaky**：全量测试是阶段 A 的退出门槛（"失败目录不能运行"、"无订阅者时快照分配为 0"等都要靠它判定）。
当最大的套件本身不确定时，**门禁给出的"红"无法区分"改动引入了缺陷"与"机器今天心情不好"**——
本轮就实际发生过：一次全量出现 1 个失败，若直接采信会误判为 AR-27 修复引入回归，
实际证明是抖动（同二进制连跑三次结果不同）。

**建议**：

1. 把绘制次数、弹窗坐标这类**依赖时序与渲染的断言**从硬断言改为带容差或改为可重放的事件序列断言；
2. 抖动用例单独成组，与确定性套件分开报告，**不要混在同一个"全绿/非全绿"判定里**；
3. 在引入阶段 A 的退出门槛前，先让门禁本身可信——否则门槛会变成噪音来源。

这与 AR-25 是同一类问题的两个面：AR-25 是"把缺陷写成规范"，AR-28 是"把抖动写成通过"。

### AR-11 / P1：文档保真已有，插件与节点演进协议仍不足

`Persistence/WorkflowDocumentJsonStore.cs:285` 对当前 Schema 内已知节点要求 `NodeVersion` **完全相等**；`PluginLoader` 支持依赖排序与清单，但依赖主要按 `PluginId` 组织，默认优先复用已加载程序集，**加载上下文非 collectible**。

未知节点保真 ≠ 已知旧版本能升级；插件目录能加载 ≠ 能安全热更新。**当前受信任、重启部署模式本身可以成立，不应为追求"平台化"立刻改热加载。**

**建议**：节点级版本迁移注册、旧文档语料测试、兼容性清单；运行制品记录文档修订/摘要、插件与节点版本、脚本引用及工位配置版本。**先支持锁定版本部署和回退**，热更新等真实部署需求出现后再考虑。

### AR-12 / P1-P2：扩展验收与运维要求落后于扩展能力

现有测试覆盖有价值，**不能说"缺少测试"**。但跨模块组合契约仍需独立验收。代码中 `CSharpScript` 明确是**进程内受信任执行**，超时也是协作式——**这不是沙箱**。近期运行状态与联合事件主要在内存中，不是持久化故障监管。

**建议**：
- 统一节点 SDK 契约套件：配置快照、取消、晚响应、可变输出、资源释放、部分提交、操作继续及并发 Handler。
- **组合契约**：正常图 + 处置图 + 视觉帧仓 + 真实 UI 控制取消，而非只分别测试四者。
- 固定并发调度点的生命周期测试，**不靠 Sleep 碰撞**。
- 图规模、长历史、高频快照、并行设备等待与内存预算基准。当前目录检查未发现 Workflow 专用基准工程，**不能推断其他地方完全没有压测**。
- 明确脚本/插件信任与发布权限；若要运行不受信任代码或强制终止失控 SDK，评估进程隔离，而不是仅靠 `CancellationToken`。

### AR-26 / P1：评审与文档治理

- `progress.md` 45KB，按"当前/前轮/前前轮"倒序堆叠，无索引无状态列，且把"725/725 通过"与"尚未实现…"并列——**通过数量是过程指标，不是架构完成度指标**。
- 已识别的 P0（`NP-01/02/03/05`）在代码中**全部仍然成立**，但多轮"全量绿灯"读起来像已收敛。
- 三份评审文档并存，取代关系未声明；探针与日志**不在项目内**（`C:\Data\PiProgects\WorkFlow\.pi-tmp\` 为上级工作区，日志在 Git Bash `/tmp`），阶段 A 要求"转成永久回归测试"却无可复跑的探针；`.gitignore` 有 `.tmp/` 却**没有 `.pi-tmp/`**。

**建议**：`progress.md` 改为索引 + 状态表（`状态 / 对应 ADR / 验收证据`），轮次记录归档；探针移入 `tests/Workflow/DP.WorkFlow.ArchitectureProbes/`，输出写入仓库内 `artifacts/`；`.gitignore` 增补 `.pi-tmp/`。

---

## 7. 目标架构

### 7.1 分层与依赖规则

```text
        ┌──────────────────────────────────────────────┐
  L4    │ 宿主与组合根                                  │
        │ Host.Cli / Host.Desktop / Studio.WinForms/Wpf │
        └───────────────┬──────────────────────────────┘
        ┌───────────────▼──────────────────────────────┐
  L3    │ 组合与扩展装配                                │
        │ Plugin.Composition（目录/冻结/预检/装配）      │
        │ Studio.Model（UI 无关 Authoring 模型）         │
        └───────────────┬──────────────────────────────┘
        ┌───────────────▼──────────────────────────────┐
  L2    │ 应用级运行管理（AR-05 新增）                   │
        │ RunScope / Host 生命周期 / 工位与运行集合      │
        └───────────────┬──────────────────────────────┘
        ┌───────────────▼──────────────────────────────┐
  L2'   │ 执行内核                                      │
        │ Runtime（调度/状态/绑定/宿主契约）             │
        └───────────────┬──────────────────────────────┘
        ┌───────────────▼──────────────────────────────┐
  L1    │ 文档与编译 Core（Documents/Compilation）       │
        └───────────────┬──────────────────────────────┘
        ┌───────────────▼──────────────────────────────┐
  L0    │ 稳定契约（零依赖，不含 UI 关注点）             │
        │ Contracts：Node/Port/Handler/Capability/      │
        │            Identity/Result/Diagnostics        │
        └──────────────────────────────────────────────┘
```

**四条硬规则**（建议以架构测试写入 CI，防止回潮）：

1. **节点包只依赖 L0 Contracts**；需要宿主能力时，走 `Contracts` 中的 port 或只含接口的 `Runtime.Hosting.Abstractions`。
2. **外部能力经 port 注入**；`Contracts` 不引用 Roslyn、不引用视觉 SDK。
3. **UI 关注点不出 `Contracts`**；编辑描述走独立 Provider 扩展包。
4. **应用级运行管理高于 Engine 生命周期，但不替 Engine 执行节点**（沿用你文档第 161 行的边界）。

**与"不先增加程序集"的关系**：以上是**逻辑分层**，`L2/L2'` 与 `Studio.Model/Studio.Host` 可以先在现有程序集内以命名空间与可见性约束表达，只有出现真实依赖 seam 时才拆程序集。这与你文档第 151 行的立场一致。

### 7.2 内核职责拆分

`WorkflowEngine`（954 行，13 个可变集合 + 6 个计数器 + 9 个事件，全部由单一 `_stateSync` 保护）拆为：

| 协作对象 | 职责 |
|---|---|
| `ExecutionScheduler` | 路径推进、端口选择、安全计数上限 |
| `ParallelScopeCoordinator` | 作用域创建/汇聚/兄弟取消 |
| `RunStateProjector` | 快照与 Trace 投影，**含节流与惰性化**（先抽此项，收益最直接） |
| `ChildEngineSupervisor` | 子引擎创建、Hold/Pause 传播、子快照转发 |
| `AdmissionGate` | Pause / ExternalHold 合并与等待，与调度状态**分锁** |

### 7.3 领域演进方向

```text
现状：一台设备动作 → 一个新节点类型
目标：设备能力目录 → 参数化节点 + 能力声明
```

节点声明能力（`IAxisController`、`IIoPort`、`IPneumaticActuator`），设备/通道作为配置与绑定，能力由宿主装配并在运行前递归预检。**新增设备 = 新增能力实现 + 配置，不新增节点类型。**

---

## 8. 演进路线

### 阶段 0：纳入版本控制（AR-24）【已完成】

**前置条件**，无退出门槛，立即执行。

- ~~初始化仓库或确认丢失原因；确认 `.gitignore` 覆盖 `bin/`、`obj/`、`.tmp/`、`.pi-tmp/`、`artifacts/`；~~
  **已完成**（`8fdd174`）。性质为漏初始化；`.gitignore` 已补齐 `.pi-tmp/`、`artifacts/`、`TestResults/`、`*.log`、`.workbuddy-ai/`。
- 确认 `../DP.Vision` 的引用方式（AR-16）在纳入 VCS 后如何处理（子模块或包）。
  **未处理**，仍为跨仓源码引用，需独立决策。

### 阶段 A：先修成立条件（AR-01/02/03/04/17/19/25）

- 将已复现问题转为**永久回归测试**（插件失败状态、含引用 struct 隔离、处置不释放父帧、快照惰性化）；
- 明确根/子 RunScope；统一启动、准备、等待停止与释放顺序；
- 为 Stop/Reset 与新 Run 的交错增加确定性测试；
- 补 AR-01 的生产路径复现与 AR-03 的受影响节点清单。

**退出门槛**：文档修改不能污染计划；失败目录不能运行；子任务不能销毁父资源；停止完成前不允许新运行抢入资源释放窗口；无订阅者时快照分配为 0。

### 阶段 B：形成最小工位运行闭环（AR-05/06/08）

- 收敛现有 Host 与联合组的启动/准备基础设施；
- 工位监管拥有**持续的维护阻断状态**；处置失败不等于监管任务消失；
- 明确设备操作权与在途退出协议，暂不开放任意并行回退。

**退出门槛**：A/B 交接失败 → 统一处置 → 气缸受阻 → 人工核实 → 继续处置 → 按各自入口恢复。**每种异常情形需写出期望终态**（保留阻断 / 终止 / 继续），否则测试无法判定：

| 情形 | 期望终态 |
|---|---|
| 取消 | 待定 |
| 设备不退出 | 保留阻断 |
| 出现新危险 | 保留阻断 |
| 准备失败 | 待定 |
| 晚响应 | 待定 |

**没有证明安全的路径必须保留阻断。**

### 阶段 C：稳定大型节点生态（AR-07/10/11/13/20/21/22）

- 节点配置/迁移/能力/输出生命周期契约及 SDK 验收套件；
- Document 命令入口与 Revision、共享交互状态机、字段级编辑扩展；
- 参数化复用子流程先明确版本与输入输出契约，再提供跨文档流程库。

**退出门槛**：新增领域节点不必修改内核；新增常见编辑能力不必双平台复制业务规则；节点升级有可重复的文档迁移验证。

### 阶段 D：面向长期运行与交付（AR-09/12/15/23）

- 轻量监控/分页历史、资源与性能预算、持续运行基准；
- 运行制品清单、故障审计、无界面宿主、发布和回退；
- 按实际需求决定是否持久化恢复、跨进程隔离或跨机协作；**不把"重放事故记录"做成重新驱动物理设备**。

**退出门槛**由目标工位的图规模、执行频率、历史保留、停机预算和安全要求确定。**本轮没有足够数据给出可信的季度工期或吞吐承诺。**

### 依赖关系（不是工期，是可判断的先后）

```text
AR-24 ──→ 全部（安全网）
AR-04 ──→ AR-05        监管需要先有受监管的生命周期
AR-07 ──→ AR-10        手势层需要命令入口做落点
AR-01 ←→ AR-06         共享"所有权契约"，应合并设计再分别实现
AR-02 ∥ AR-03          相互独立，可并行
AR-17 ∥ AR-19          投影拆分后可一并处理
```

---

## 9. 不建议现在做的事情

沿用你文档的清单，保留并补充：

- 重写整套平台或建立超级 Node 基类；
- 把每个内部模块都拆成一个程序集，或立即微服务化；
- 为所有异常统一增加自动重试/跳转按钮；
- 将 Signal、设备状态或工位恢复隐含在字符串变量命名约定中；
- 用新增测试数量代替跨模块不变量、资源验收和长运行基准；
- **把 `net48` 兼容当作必须保留的约束而不追问其业务来源**（AR-15）；
- **在没有版本控制的前提下开始阶段 A 的重构**（AR-24）。

---

## 10. 附录

### 10.1 本轮已核对且未发现问题的范围

为避免读者误判覆盖面，以下已核对且**未发现结构性问题**：

- Kernel 未反向依赖具体领域节点或桌面 SDK；Motion 只依赖 Abstractions；`DP.Vision` 未反向依赖 Workflow；
- `IWorkflowNodeModel` 保持精简（`Id`/`Title`/`NodeType`），NodeModel 与 Handler 分离；
- Token/Scope 输出可见性、显式公共数据、Block 输入输出映射语义自洽；
- 未知节点原始配置保真、未来 Schema 拒绝策略正确；
- 端口基数与编译期图规则检查、循环安全上限、并行汇聚互斥（`WF022`）设计正确；
- 参数化复用子流程的输入输出映射契约明确；
- `WorkflowDocumentJsonStore.cs:285` 的 `NodeVersion` 全等校验逻辑本身正确（问题在缺少迁移注册，见 AR-11）。

### 10.2 溯源表

| 最终编号 | 来源 | 说明 |
|---|---|---|
| AR-01 | AR | 补充三处调用点与三层归因 |
| AR-02 | AR + S1-3 + C | 强化"子目录冻结不可逆"结论 |
| AR-03 | AR + S2-6 | 补充"受影响节点清单"待办 |
| AR-04 | AR | 行号校正（StopAsync 实为 `:133`，ResetAsync `:231`） |
| AR-05..AR-12 | AR | 原样保留 |
| AR-13 | S1-1 + S1-2 | 合并节点包依赖与 ScriptEngine 泄漏 |
| AR-14 | S2-1 | — |
| AR-15 | S2-2 + S2-4 + S4-1 + S4-2 | 合并平台耦合、sln、TFM |
| AR-16 | S2-3 | — |
| AR-17 | S1-4 | — |
| AR-18 | S3-2 | — |
| AR-19 | S3-4 | — |
| AR-20 | S3-5 | — |
| AR-21 | S3-6 | — |
| AR-22 | S3-8 + S3-9 + S3-10 | — |
| AR-23 | S4-3 | — |
| AR-24 | C | 新增（阻塞） |
| AR-25 | C | 新增 |
| AR-26 | S4-4 + C | 合并文档治理与探针入库 |
| AR-27 | 本轮实施 | AR-01 的姊妹实例：文件夹采集游标被嵌套运行重置（已修复 `1164725`） |
| AR-28 | 本轮实施 | UI.Windows 套件不确定，不能当作门禁（实测抖动） |

来源标记：`AR` = 你的 `architecture-review-and-roadmap.md`；`S` = `architecture-smell-review.md`；`C` = `architecture-review-and-roadmap-critique.md`；`本轮实施` = 2026-09-20 执行 AR-24 / AR-01 阶段 1 时发现。

### 10.3 已核对的代码引用

| 引用 | 核对结果 |
|---|---|
| `WorkflowRuntimePluginCatalog.cs:89` Freeze 起始 | 精确 |
| `WorkflowNodeConfigurationSnapshotter.cs:220` `IsSharedImmutable` | 精确 |
| `WorkflowNodeExecutionContext.cs:45` `GetRequiredCapability` | 精确 |
| `LoadVisionFileNode.cs:38` 返回 Retain 句柄 | 精确 |
| `WorkflowVisionFrameScope.cs:119` `PrepareAsync` 起始 | 精确 |
| `WorkflowWarningHandlerCoordinator.cs:36` 复用 `context.Services` | 精确 |
| `WorkflowDocumentJsonStore.cs:285` NodeVersion 全等校验 | 精确 |
| `WorkflowPluginLoaderTests.cs:56` `Assert.True(catalog.IsFrozen)` | 精确 |
| `WorkflowRuntimeBinder.cs:60` `ToArray()` 转 `IReadOnlyList` | 成立（可强转回数组） |
| `WorkflowRuntimeHost.cs` 有 `StopAsync`/`ResetAsync`，`IWorkflowRuntimeHost` 无 | 成立 |
| `WorkflowRuntimeHost.cs:282` / `WorkflowEngine.cs:538` / `WorkflowJointRecoveryGroup.cs:149` / `WorkflowWarningHandlerCoordinator.cs:55` 四处准备调用 | 成立 |
| `IWorkflowRunPreparationService` 契约注释声明"由根运行宿主调用"，实际三处为嵌套调用 | 成立（契约被违反） |
| `WorkflowRunPreparationContext` 仅含 `Nodes`，无作用域/所有权信息 | 成立 |
| `RunChildWorkflowAsync`（`WorkflowEngine.cs:499-500`）传 `prepare: false` | 成立（解释缺陷为何长期未暴露） |
| `WorkflowVisionFrameScope.Capture` 返回独立新租约（掩盖效应） | 成立 |
| 样例双注册（`Form1.cs:96,112` / `MainWindow.xaml.cs:66,68`） | 成立 |
| `WorkflowVisionFrameScope.PrepareAsync` 会 `Clear()` 并 `Dispose` 全部帧租约 | 成立 |
| `WorkflowRuntimeHost.cs:130` / `:229` / `WorkflowDocument.cs:33` / `WorkflowRunState.cs:53` | 偏移 1–3 行，且指向 XML 注释；建议改用符号引用 |

### 10.4 待确认事项

1. `net48` 是业务硬需求还是历史兼容？（决定 AR-15 的范围）
2. 现有节点配置中是否存在含引用型成员的 struct？（决定 AR-03 的等级）
3. ~~`.git` 是漏初始化还是拷贝时丢失？（决定 AR-24 的处理方式）~~ **已关闭：漏初始化。** 已建立 `main` 分支与基线提交 `8fdd174`。
4. 桌面样例的 `FrameScope` 双注册是权宜之计还是预期用法？（决定 AR-01 的 (c) 层修复面）
5. 工作区上级目录的 9 个畸形日志与 `NUL` 文件是否需要清理？（本次未代为删除）
6. **`global.json` 是否提升到 SDK 10？** 当前固定 `9.0.308`（+ `rollForward: latestPatch` → 实际选中 9.0.316）。

   **先澄清一个前提，避免误解**：本仓库**不存在"要不要支持 9.0 或 10.0"的问题**。这是两件不同的事：

   | 层 | 是什么 | 本仓库现状 |
   |---|---|---|
   | **目标框架**（产品要"支持"的） | 程序跑在哪个运行时上 | 全部工程 `net8.0` / `net48` / `netstandard2.0`，**没有一个工程是 net9.0 或 net10.0** |
   | **构建工具链**（`global.json` 管这层） | 用哪个 SDK 编译 | `9.0.308`（仓库内）／`10.0.302`（仓库外） |
   | **运行时**（部署目标） | 产物在哪里跑 | .NET 8 |

   SDK 版本**只影响怎么编译，不影响程序跑在哪**。所以提升 `global.json` 不是"升级产品到 .NET 10"，
   只是"换一把编译器"。之所以这个选择会冒出来，纯粹是因为这台机器同时装了 SDK 9 和 SDK 10。

   **"需要 9 或 10"是假象**——把四条来源分开看就清楚了：

   | 要求来自哪里 | 它要求什么 | 是"需求"吗 |
   |---|---|---|
   | 项目本身（`TargetFramework=net8.0`） | SDK ≥ **8.0.100** | ✅ **唯一真实的需求** |
   | 仓库配置（`global.json`） | SDK = `9.0.308` | ❌ 手写的声明，无项目理由 |
   | 这台机器装了 | `9.0.316` / `10.0.302` | ❌ 环境事实 |
   | 这台机器能用 | 只有 `10.0.302` | ❌ NuGet 版本差异所致 |

   即：**项目要 8，配置写着 9，机器只有 9 和 10，而机器只能用 10。** 没有任何一条是"项目需要 9 或 10"。
   已验证：全部 45 个解决方案内工程均为 `net8.0` / `net48` / `netstandard2.0`；
   用 C# 12（SDK 8 的默认语言版本）编译整个解决方案 **0 警告 0 错误**——项目层面降到 8.0.100 无障碍。
   唯一真正的项目级约束是 `LangVersion=latest`（它约束的不是版本号，而是"必须固定"），
   把它写死（如 `12`）即可解除 SDK 敏感性。

   **`rollForward` 实测**（2026-09-20，本机已装 SDK `9.0.316` / `10.0.302`，在仓库目录内执行）：

   | `global.json` version | `rollForward` | 仓库内解析结果 |
   |---|---|---|
   | `8.0.100` | `latestPatch` | ❌ `A compatible .NET SDK was not found` |
   | `8.0.100` | `latestMinor` | ❌ 同上（**不跨主版本**） |
   | `8.0.100` | `latestMajor` | ✅ `10.0.302` |
   | `9.0.308`（现状） | `latestPatch` | ✅ `9.0.316` |

   所以"降成 8 会砍掉可构建路径"这句话要拆开说：**砍掉的是"仓库内解析出 SDK"这件事**，
   而**仓库外（cwd = `C:\Data`）那条路完全不受 `global.json` 影响**——`global.json` 只在仓库目录树内生效。
   现状是"仓库内解析出 9.0.316 但编不动（NuGet 6.14）"，降成 8 变成"仓库内根本解析不出 SDK"，
   两者在仓库内都不可用；真正的差别只在于报错更早、更直白。
   `latestMajor` 能让它解析到 10.0.302，但那等于"写着 8、实际用 10"，声明就失去意义了。

   **为什么需要 pin 一个 SDK 版本**：本仓库 `Directory.Build.props` 里
   `LangVersion=latest`（**C# 语言版本跟着 SDK 走**：SDK 9 → C# 13，SDK 10 → C# 14）
   且 `TreatWarningsAsErrors=true`（**多一条分析器警告就构建失败**）。
   同一份源码在不同 SDK 上可能一个绿一个红——pin 的用意就是让"我这儿能编过"在团队里成立。

   **但本仓库的 pin 与事实不符**：`project.assets.json` 的 `SdkAnalysisLevel` = `10.0.300`，
   是 SDK 10 生成的，说明实际开发中早已在用 SDK 10。所以真正的问题不是"要不要支持 10"，
   而是"**这个 pin 还要不要留**"。

   **另一个误区**：提升到 SDK 10 **不能**修复 `dotnet restore`——`restore` 在 SDK 9 和 SDK 10 上**都**失败，
   因为它本来就要读机器级 NuGet 配置。提升的真实收益只有一个：**让"在仓库目录内构建"变得可行**
   （目前仓库内连 `build --no-restore` 都报 `NETSDK1060`）。
   支持提升的两条事实：① 现有 assets 本来就是 SDK 10 生成的，与 pin 矛盾；
   ② 不提升则所有构建都必须 `cd` 出仓库，容易忘、容易错。
   **无论是否提升，本机都无法执行任何 `dotnet restore`；根治要修机器（补回 `ProgramData` 等系统环境变量
   或修 Known Folder 注册项），而不是改仓库。** 详见 AR-24 一节的新发现。

   **关键补充（2026-09-20 查清）：本机真正可用的构建入口是 Visual Studio，不是 `dotnet` CLI。**
   `devenv.exe` / `MSBuild.exe` 跑在 .NET Framework 上，`GetFolderPath(CommonApplicationData)`
   走注册表外壳文件夹能正常返回 `C:\ProgramData`，所以它们的 NuGet **不崩**；
   仓库里那 46 个 assets 正是 2026-09-10 由它们生成的（路径字段全部指向本机）。
   因此"改 `global.json` 换 SDK"解决的是 **CLI 的便利问题**，不是"能不能构建"的问题——
   能不能构建取决于是否用 Visual Studio。详见 AR-24 一节。

7. ~~阶段 1 的两条验收是否补测？~~ **已关闭：已补齐。** 生产路径端到端（`WorkflowVisionFrameScopeRecoveryEndToEndTests`）
   与"结束后租约按所有权恰好释放"（`WorkflowVisionFrameScopeLeaseOwnershipTests`）均已落地，并各自用故意改坏生产代码
   的方式证明了有效性。另补 `WorkflowRunPreparationScopeDeclarationTests` 锁定 AR-01 破坏点
   （`WorkflowEngine` 嵌套调用）声明的作用域——此前把 `WorkflowEngine` 改回 `Root` 时全部测试仍然绿灯，
   属于覆盖盲区。详见 §10.5。
8. **另 2 个准备调用点是否需要声明级覆盖？** `WorkflowWarningHandlerCoordinator.cs:55` 与
   `WorkflowJointRecoveryGroup.cs:150` 都传 `Nested`，但都没有测试锁定其声明。后者的"一轮"语义
   尚未定论（见 `ar-01-fix-plan.md` §5），现在写断言会把待定行为固化成契约。建议与阶段 2 一并处理。

---

### 10.5 阶段 1 验收补齐与覆盖盲区（2026-09-20 续）

阶段 1 修复后只有 4 个"给定 ScopeKind 时帧仓行为"的测试。补测时发现一个更关键的缺口：
**没有任何测试锁定 4 个调用点声明的作用域是否正确**。把 `WorkflowEngine` 的 `Nested` 改回 `Root`，
4 个测试仍然全绿——因为它们直接构造 `WorkflowRunPreparationContext`，根本不经过调用点。

本轮补 3 个测试文件，覆盖阶段 1 验收第 4、5 条并封堵上述盲区：

| 测试文件 | 覆盖 | 有效性验证方式 | 结果 |
|---|---|---|---|
| `Nodes.Vision.Tests/WorkflowVisionFrameScopeRecoveryEndToEndTests` | 验收 4：采图 → 主操作故障 → 处置 → 继续消费原图 | 移除帧仓守卫 → 红灯 | 红灯信息直指 `ObjectDisposedException: ImageBuffer`（`ImageBuffer.Alive()` → `CopyTo()`），未被重试安全守卫掩盖 |
| `Nodes.Vision.Tests/WorkflowVisionFrameScopeLeaseOwnershipTests` | 验收 5：租约按所有权恰好释放 | 变异 A/B → 红灯 | 见下 |
| `Runtime.Tests/WorkflowRunPreparationScopeDeclarationTests` | 破坏点（`WorkflowEngine` 嵌套调用）声明的作用域 | 把 `WorkflowEngine` 改回 `Root` → 红灯 | `Expected: Nested / Actual: Root` |

**覆盖范围如实说明**：上表第 3 行只覆盖了 1 个调用点。4 个调用点的声明级覆盖现状：

| 调用点 | 声明 | 声明级覆盖 |
|---|---|---|
| `WorkflowRuntimeHost.cs:283` | `Root` | ✅ `WorkflowRuntimeHostTests.TestRunPreparation` |
| `WorkflowEngine.cs:539` | `Nested` + `parentNode.Id` | ✅ 本轮新增（破坏点） |
| `WorkflowWarningHandlerCoordinator.cs:55` | `Nested` | ❌ 无 |
| `WorkflowJointRecoveryGroup.cs:150` | 暂 `Nested`（语义待定） | ❌ 无 |

后两处是残留缺口，已记为 §10.4 第 8 问。**不在本轮补**，因为 `WorkflowJointRecoveryGroup` 的"一轮"
语义尚未定论，现在写断言等于把待定行为固化成契约。

#### 验收 5 的观测手法

帧仓不暴露租约计数。改用**容量为 1 的 `FrameBufferPool` 当探针**：采集时借走唯一槽位，
只要还有任何一个租约没释放，槽位就回不来，`TryRent` 必然失败。于是"租约是否恰好释放"
成为确定性的布尔断言，不依赖 GC 与计时。探针用完立即归还槽位，可重复调用。

两个变异验证（改坏生产代码后必须变红）：

| 变异 | 预期红灯 | 实测 |
|---|---|---|
| `Clear()` 漏掉 `_previews` 释放（租约泄漏） | 两条都红 | `根运行开始后上一轮仓内租约必须全部释放。` / `全部租约释放后底层存储必须归池。` |
| `Capture()` 不 `Retain`（UI 快照与仓共用句柄） | 第 2 条红 | `ObjectDisposedException`，落在读快照像素那一行 |

#### 验收 5 的措辞修正

原文写"运行结束后帧仓内租约数为 0"，与设计不符。设计是：
**本轮结束后仓仍持有租约**（供结果查看窗口使用），**下一轮根运行开始时才归零**。
测试按设计语义断言，并把这个区别显式写进两个测试名。

#### 这两条测试的性质

与验收 4 不同，验收 5 的两条是**特征锁定**测试——它们锁定"释放语义不能被过度削弱"
（例如为了修 AR-01 干脆删掉 `Clear()`）。它们不揭示 AR-01 缺陷本身，但正是
防止"修过头"的反向门禁。
