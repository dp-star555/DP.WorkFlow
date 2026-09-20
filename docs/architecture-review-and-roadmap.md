# 大型节点流程系统：架构审阅与演进路线

## 审阅结论与方法

当前主干值得保留：Document → Compiler → ExecutionPlan → RuntimeBinder → BoundPlan → 准备 → Engine → RunState。真正的问题不是程序集数量或节点数量，而是若干关键保证依赖调用方自律：冻结、服务生命周期、编辑入口、运行控制、数据所有权和恢复监管。

本轮检查了Workflow项目引用、文档/编译、插件、运行与联合恢复、数据提交、视觉资源仓、持久化、脚本及双平台设计器。没有修改生产实现，没有重新执行全部测试。工程核对为45/45。上一轮725项测试通过是历史基线，不能作为本次发现已被覆盖的证明。

额外执行了最小探针 `.pi-tmp/architecture-review-probe/`（位于仓库根目录），日志为Git Bash `/tmp/architecture-probe.log`。结果：

```text
SNAPSHOT_SHARED_STRUCT_REFERENCE=True; SNAPSHOT_COUNT=2
FIRST_FREEZE=rejected_missing_handler
IS_FROZEN_AFTER_FAILURE=True
SECOND_FREEZE=returned_success
RECOVERY_RESUMED=True; PARENT_FRAME_DISPOSED_DURING_RESUME=True
```

最后一项探针在恢复后的读取节点内主动捕获ObjectDisposedException并记录标志，以便完成探测；它不表示生产图像读取会成功。

## 应保留的架构资产

- Kernel没有反向依赖具体领域节点或桌面SDK；Motion只依赖Abstractions，Vision依赖独立视觉算法契约。
- NodeModel与Handler分离；执行计划和Handler绑定区分，文档编辑不应直接影响运行。
- Token/Scope输出可见性、显式公共数据、Block输入输出映射已经形成明确语义。
- 未知节点原始配置保真、未来Schema拒绝、独立Vision和双平台领域Adapter是正确方向。
- 逻辑操作身份、命名恢复入口和处置不盲目重放可复用，不应为重新设计监管而删除。

Process/Composite引用Runtime并不自动等于依赖环；它说明这些包中包含运行编排职责，需要继续分辨节点能力与应用级编排，而不是机械地禁止全部引用。

## 风险分级

P0：继续扩大生产使用前需要明确修复的契约问题。
P1：节点、流程和团队规模扩大前应治理的架构问题。
P2：根据实际部署和性能指标推进，不提前建立分布式平台。

### AR-01 / P0：运行准备与资源作用域混淆【已复现】

证据：
- `src/Workflow/Nodes/DP.WorkFlow.Nodes.Process/Recovery/WorkflowWarningHandlerCoordinator.cs:36`：处置上下文复用原Services。
- `src/Workflow/Kernel/DP.WorkFlow.Runtime/Execution/WorkflowEngine.cs` 的 `RunTrackedChildAsync`：处置子运行再次调用准备服务。
- `src/Workflow/Nodes/DP.WorkFlow.Nodes.Vision/Acquisition/WorkflowVisionFrameScope.cs:119`：PrepareAsync清理本仓帧租约。
- `.../Acquisition/LoadVisionFileNode.cs:38`：节点输出正是仓持有的Retain句柄。
- 两个桌面样例均将同一FrameScope注册为帧仓和运行准备服务。

组合后，在主流程已有图像输出、进入处置子流程、再继续主流程时，原图像输出可能已经被准备服务释放。独立保留的UI租约仍可显示，反而可能掩盖运行数据已失效的问题。

建议：建立RunScope/子Scope的资源所有权契约。设备能力实例、根运行资源、子任务资源不能统称为Services。准备上下文至少要能区分根启动和处置/子任务；父资源只由其所有者释放。不能简单跳过全部子流程准备，因为能力检查和子任务自己的准备仍然必要。

验收：采图→主操作故障→执行处置→继续消费原图；父输出有效，子任务完成不清理父资源，结束后租约按所有权恰好释放。

### AR-02 / P0：插件冻结具有失败后的“成功外观”【已复现】

证据：`Kernel/DP.WorkFlow.Abstractions/Plugins/WorkflowRuntimePluginCatalog.cs:89`。Freeze先冻结两个目录并将 `_frozen=true`，然后才校验Handler。缺少Handler时首次抛错，但IsFrozen已为true，再次Freeze直接返回。

同文件Register先记录ExtensionId，再调用插件写入真实目录；插件中途失败会留下部分注册和已占用的ID。

建议：目录具备明确的Building/Validated/Failed/Published语义。先对候选完整配置校验再发布；做不到事务注册时，应明确让失败目录永久失效。只把 `_frozen=true` 移到后面不足以恢复已冻结的子目录。

验收：注册/冻结失败后不能得到可运行目录；重试必须得到明确错误或在全新候选目录重建。

### AR-03 / P0：反射快照不能保证任意插件配置的隔离【已复现】

证据：`Kernel/DP.WorkFlow.Core/Compilation/WorkflowNodeConfigurationSnapshotter.cs:220` 把所有ValueType和Delegate当作可共享值。包含List的struct已复现源修改污染快照。闭包可能捕获可变状态；共享JsonDocument还有释放生命周期问题。

`WorkflowExecutionPlan` 已通过复制读出防止普通类被直接篡改，不应误判为直接暴露内部NodeModel；问题是底层复制契约不完备。此外 `WorkflowRuntimeBinder.cs` 将能力列表数组作为IReadOnlyList暴露，仍可被强转修改。

建议：定义允许进入配置的值类型、集合和资源限制，允许插件提供明确的配置快照/编解码实现；拒绝句柄、服务、委托和不受约束的对象图。补深度/大小预算，逐步形成真正的不可变编译表示，而不是每次执行依赖任意对象图反射复制。

### AR-04 / P0：运行生命周期入口没有形成一个完整Interface【代码与竞态分析】

证据：
- `Runtime/Hosting/IWorkflowRuntimeHost.cs` 暴露可变Engine，却没有具体Host已有的StopAsync。
- `WorkflowRuntimeHost.cs:93,130,229`：启动、等待停止、Reset分开协调任务；Reset等待旧任务期间允许另一个Run进入的交错需要专项验证。
- `WorkflowStudioRuntimeBinding.cs:76` 依据Engine的Running/Paused判断是否重新Configure；准备期间Engine仍可呈Idle，而Host任务实际上已经开始。
- `WorkflowJointRecoveryGroup.cs:140` 又维护一套启动、预检、准备、取消和任务监管。

风险包括准备中重复操作报错、Reset误清新运行引用、调用方先释放设备而任务尚未退出。这里部分为源码可构造的竞态风险，本轮未做并发复现，不能称为已发生的现场故障。

建议：以运行尝试身份和一个受监管生命周期串行化Configure/Start/Stop/Reset/Dispose；显式呈现Preparing、Stopping及准备失败。UI只获取控制Interface和只读监控视图，不拿Engine自行调度。单流程与联合运行复用一套准备/运行资源基础设施，但联合协议不塞进每个节点。

### AR-05 / P1：恢复协议进入了内核，但工位监管生命周期仍缺位【已确认结构】

证据：`Runtime/Execution/WorkflowJointRecoveryGroup.cs`、`WorkflowEngine.Recovery.cs`、`Nodes.Process/Recovery/WorkflowWarningHandlerCoordinator.cs`。

Group自己创建Engine、保存方案、执行处置、校验共同条件、维护消息轮次，失败直接取消整组；处置任务结束后没有独立的维护阻断会话承接后续操作。协调中断等待节点边界，不能据此宣称能退出任意在途设备或互相等待的节点。

新增联合恢复是有价值的受限协议验证，不应因近期加入就直接认定它是长期工位架构。它目前也没有与Studio的Host生命周期统一。

建议：在应用级运行管理模块中拥有工位、运行集合、故障会话和生产准入；Engine只保留本地调度、受控退出/准备/恢复的机制。处置图是受监管任务，不是监管者。界面仍然只有工位状态、当前处置和有限操作，不必让用户配置内部会话对象。

### AR-06 / P1：能力声明、实际能力实例、设备操作权仍然脱节【已确认结构】

证据：`Runtime/Preparation/WorkflowRuntimeCapabilityValidator.cs`、`Infrastructure/WorkflowServiceProvider.cs`、`Execution/WorkflowNodeExecutionContext.cs:45`。

绑定冻结的是能力需求；执行时仍从可替换的容器取实例。GetRequiredCapability未将读取限制在已声明集合。存在实例也不意味着设备在线、适合当前工件、没有被另一流程占用。

建议：运行准备生成固定的能力解析结果，并分别管理设备网关、资源租约和本次操作意图。冻结对象引用不等于冻结物理状态；动作前仍需设备协议核实。新增节点SDK验收“声明了什么、实际取了什么、是否支持并发、何时释放控制权”。

### AR-07 / P1：Document唯一所有者尚未成为唯一写入口【已确认结构】

证据：`Core/Documents/WorkflowDocument.cs:33` 的CanvasProjection；Graph.Nodes返回的是可变模型引用；`UI.Shared/Authoring/Designer/WorkflowDesignerSession.cs:92,927,1114` 持有编辑与Undo闭包；`WorkflowLayout`仍用连接顺序索引关联布局。

外部能绕过Session修改模型或集合，撤销、脏标记、诊断缓存和导航不一定同时感知。后台批处理、未来脚本编辑和多人协作会进一步放大问题。

建议：Document变更命令/事务、Revision、语义与布局分类变更通知成为共同入口；稳定连接ID替代长期依赖列表位置。先让现有Session委托该入口，保留过渡投影读取，不一次性重写编辑器。

### AR-08 / P1：暂存提交容易被误读为全局事务【已确认结构】

证据：`Execution/WorkflowNodeExecutionContext.cs` 的CommitDataChanges依次提交公共数据和局部变量；Engine随后提交节点输出。`WorkflowContext.TryGetVariable` 和公共数据仓读取直接返回对象引用。

各自的批量更新不等于三个仓的共同原子提交。并且节点取得List/业务对象后直接修改，可能绕过暂存写入；“失败不提交”不能自动覆盖这种引用副作用。

建议：明确值的不可变/借用/拥有语义、提交范围和并行写冲突政策。当前可以保守保持CommitStarted后禁止重执行，但SDK必须解释部分提交和结果不明。外部设备/数据库写入属于独立副作用，不能宣传成可自动回滚事务。

### AR-09 / P1：监控快照成本随历史增长，不只是UI绘制慢【结构性性能风险】

证据：`WorkflowRunState.cs:53` 的NodeOutputs每次ToArray；`WorkflowEngine.cs:193,785` 构建快照包含完整输出历史，节点状态变化频繁发布。Studio合并接收到的快照发生在构建之后。

在长运行中，若每次新增输出都反复复制全部历史，累计复制工作可接近二次增长。最大节点执行次数限制并不是大对象、历史保留或发布频率预算。本轮没有跑性能基准，不能给出实际吞吐退化数值。

建议：当前状态与审计历史分开；高频监控只发差量/轻量快照，历史按序列分页读取。为输出、故障、Trace和资源租约分别定义保留策略。任何优化都不能静默淘汰后续绑定仍需使用的输出。

### AR-10 / P1：双平台Adapter重复承载交互状态机【已确认结构】

证据：WinForms/WPF的 `Authoring/Designer/WorkflowDesignerControl.cs` 分别约1925/1621行，均持有拖动、连线、折点和端口落点状态，并实现OnMouseDown/Move/Up。Shared Session约1456行，混合编辑、选择、剪贴板、布局、Undo和运行视图。

行数不是错误证明；实际问题是新增一种交互需要跨两套实现同步状态规则，且同类缺陷容易平台间漂移。

建议：共享“手势输入→交互状态→文档命令”模块，平台只做坐标/DPI、命中所需的呈现信息、捕获与绘制。保留各自渲染器，不做万能跨平台控件基类。Session按文档编辑、选择/视口、布局、运行视图逐步分工。

### AR-11 / P1：文档保真已经有了，插件和节点演进协议仍不足【已确认结构】

证据：`Persistence/WorkflowDocumentJsonStore.cs:285` 对当前Schema内已知节点要求NodeVersion完全相等；PluginLoader支持依赖排序和清单，但依赖主要按PluginId组织，默认优先复用已加载程序集，加载上下文非collectible。

未知节点保真不等于已知旧版本能升级；插件目录能加载不等于能安全热更新。当前受信任、重启部署模式本身可以成立，不应为追求“平台化”立刻改热加载。

建议：节点级版本迁移注册、旧文档语料测试、兼容性清单；运行制品记录文档修订/摘要、插件与节点版本、脚本引用及工位配置版本。先支持锁定版本部署和回退，热更新只有出现真实部署需求后再考虑。

### AR-12 / P1/P2：扩展验收与运维要求落后于扩展能力【范围限制】

现有测试覆盖有价值，不能说“缺少测试”。但是本轮前三项探针说明，跨模块组合契约仍需要独立验收。代码中CSharpScript明确是进程内受信任执行，超时也是协作式；这不是沙箱。近期运行状态与联合事件主要在内存中，不是持久化故障监管。

建议：
- 统一节点SDK契约套件：配置快照、取消、晚响应、可变输出、资源释放、部分提交、操作继续及并发Handler。
- 组合契约：正常图＋处置图＋视觉帧仓＋真实UI控制取消，而非只分别测试四者。
- 固定并发调度点的生命周期测试，不靠Sleep碰撞。
- 图规模、长历史、高频快照、并行设备等待和内存预算基准；当前目录检查未发现Workflow专用基准工程，不能推断其他地方完全没有压测。
- 明确脚本/插件信任与发布权限；若要运行不受信任代码或强制终止失控SDK，评估进程隔离，而不是仅靠CancellationToken。

## 建议的长期架构：逻辑分层，不先增加程序集

1. **创作模块**：Document、命令/事务、Revision、图校验、配置与页面扩展。拥有可编辑意图。
2. **编译与发布模块**：目录快照、节点迁移、配置快照、计划、绑定、制品身份。拥有发布后的执行语义。
3. **应用级运行管理模块**：RunScope、统一Host生命周期、工位/运行集合、阻断与恢复监管。拥有生产准入和运行任务生命周期。
4. **执行内核**：本地Token/Scope调度、操作执行、局部数据可见性及执行事实。不能感知WinForms、WPF或具体气缸型号。
5. **领域模块与Adapter**：Motion、Vision、Process等提供动作与核实能力；设备实现负责完成确认、资源占用和设备级停止契约。
6. **诊断与存储**：消费执行事实形成监控读模型、历史和事故记录，不参与决定下一节点。
7. **桌面Adapter**：共享编辑/监控行为，各自原生呈现；不拥有运行权限或设备恢复规则。

工位监管高于Engine生命周期，但不替Engine执行节点。部署开始仍可保持单进程；以上不是微服务拆分建议。

## 演进路线与验收门槛

### 阶段A：先修成立条件（对应NP-01/02/03/05/09）

- 将三个已复现问题转成永久回归测试：插件失败状态、含引用struct隔离、处置不释放父帧。
- 明确根/子RunScope；统一启动、准备、等待停止与释放顺序。
- 为Stop/Reset与新Run的交错增加确定性测试。

退出门槛：文档修改不能污染计划；失败目录不能运行；子任务不能销毁父资源；停止完成前不允许新运行抢入资源释放窗口。

### 阶段B：形成最小工位运行闭环（对应NP-04/06）

- 收敛现有Host与联合组的启动/准备基础设施。
- 工位监管拥有持续的维护阻断状态；处置失败不等于监管任务消失。
- 明确设备操作权与在途退出协议，暂不开放任意并行回退。

退出门槛：A/B交接失败→统一处置→气缸受阻→人工核实→继续处置→按各自入口恢复；同时覆盖取消、设备不退出、新危险、准备失败和晚响应。没有证明安全的路径必须保留阻断。

### 阶段C：稳定大型节点生态（对应NP-07/08/10/11）

- 节点配置/迁移/能力/输出生命周期契约及SDK验收套件。
- Document命令入口与Revision、共享交互状态机、字段级编辑扩展。
- 参数化复用子流程先明确版本和输入输出契约，再提供跨文档流程库。

退出门槛：新增领域节点不必修改内核；新增常见编辑能力不必双平台复制业务规则；节点升级有可重复的文档迁移验证。

### 阶段D：面向长期运行与交付（对应NP-12/13/14）

- 轻量监控/分页历史、资源与性能预算、持续运行基准。
- 运行制品清单、故障审计、无界面宿主、发布和回退。
- 根据实际需求决定是否持久化恢复、跨进程隔离或跨机协作；不把“重放事故记录”做成重新驱动物理设备。

退出门槛由目标工位的图规模、执行频率、历史保留、停机预算和安全要求确定；本轮没有足够数据给出可信的季度工期或吞吐承诺。

## 不建议现在做的事情

- 重写整套平台或建立超级Node基类。
- 把每个内部模块都拆成一个程序集，或立即微服务化。
- 为所有异常统一增加自动重试/跳转按钮。
- 将Signal、设备状态或工位恢复隐含在字符串变量命名约定中。
- 用新增测试数量代替跨模块不变量、资源验收和长运行基准。

本报告补充并更新 `node-platform-review-backlog.md` 的证据，不把旧待办清单或历史全量绿灯当成问题已经关闭。
