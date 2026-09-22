# DP.WorkFlow 重构进度

## 当前：运行记录与实时快照（V2-10 服务端收口）

按[运行记录设计](workflow-run-recording-design.md)完成阶段 A–D 与阶段 E 的 Runtime 侧交付，实现"实时快照是当前仪表盘、运行事件是执行行车记录"的职责分离。

快照收口：`WorkflowRuntimeSnapshot` 删除完整 `Faults`/`NodeOutputs`，改为 `CurrentFault`/`CurrentRecovery`；子流程状态拆为 `ActiveChildWorkflows` + `LatestChildWorkflowByParentNode`，完成后每个父节点只保留最近一次快照；已完成 ParallelScope 与已完成子流程不再永久留在实时集合。绑定与恢复语义未改变。

记录链路：新增 `IWorkflowRunRecorder`/`IWorkflowRunEventSink`、`WorkflowRunEvent` 系列类型、`WorkflowTracePayloadEncoder` 和 `WorkflowRunRecordingOptions`。引擎统一分配 Run 内序号，接入 Run/Node/Fault/Recovery 事件，原 Trace 最近窗口迁移到 Recorder；记录 `InputResolved`、`OutputCommitted` 及变量和公共数据提交摘要。Runtime 只向顶层注入的 Sink 推送，不预先绑定 SQL/SQLite/文件；查询、清理与保留策略由顶层 Adapter 负责。

数据血缘自动化：绑定阶段按节点类型建立输入槽元数据（`WorkflowNodeInputLayout`，含子计划递归），普通 `ResolveInput<T>(WorkflowInput<T>)` 自动识别稳定输入键并记录 `InputKey`/`InputMetadataStatus`/`SourceKind`/`SourceOutputKey`/`SourceOutputSequence`，内置 Handler 不再手写输入名；动态映射、集合元素与嵌套对象属性（Vision 定位绑定 `Node.Coordinates.System`）改用显式逃生口 `ResolveDynamicInput(inputKey, input)`；无法识别的输入仍正常解析，只把 `RecordingHealth` 降为 `Degraded`（独立的 `DiagnosticCount`，不伪装成 Sink 写入失败）。`OutputCommitted` 由 `WorkflowOutputValueExtractor` 自动提取稳定输出键：标量与资源根值以及无公开可读属性的对象统一 `$`，普通结果 DTO 展开第一层公开属性，单个 getter 失败或超过 `MaxOutputProperties` 只降级诊断而不改变已提交输出。`WorkflowEventWriteMode.Durable` 改名 `FlushRequested`，不再暗示同步持久化。

记录完全 fail-open：`RunStarted`、FlushRequested 推送和 Flush 失败都不改变节点调度与 Run 终态，只更新 `RecordingHealth`、失败计数并通知宿主；引擎侧统一经 `TryRecordRunEvent` 提交，调用方 `Trace` 数据的枚举异常也在此被吸收，不会让节点 Fault；健康通知只在状态变化时发出，Degraded 期间的成功写入不重复推送。有界内存窗口与待推送队列始终淘汰最老数据，不因旧记录保护期拒绝最新记录，更不阻塞流程。**现实取舍：因为记录不能阻塞运行，Runtime 无法保证闪退前最后几条事件一定已落盘，崩溃持久性取决于顶层 Sink 的实现和实际确认进度。**

验收：新增 `WorkflowRunRecorderTests`（序号单调、有界窗口/队列、FlushRequested 立即调度、非阻塞、健康状态与通知、收尾 Flush）、`WorkflowRunBatchPushTests`（批次封箱、立即刷意图归属、通知粒度、按批次淘汰）、`WorkflowTracePayloadEncoderTests`（摘要/引用编码、截断标记、敏感脱敏、编码失败吸收）、`WorkflowRunRecordingEventTests`（生命周期与单调序号、并行 Token/Scope、循环 ExecutionCount/LoopIteration、失败不产生 `OutputCommitted`、快照不随历史增长）、`WorkflowNodeInputLayoutTests`（输入槽发现顺序、继承属性、忽略嵌套/集合/索引器、同实例歧义拒绝、子计划递归）、`WorkflowOutputValueExtractorTests`（标量 `$`、DTO 第一层、不递归、集合/二进制/流不展开、getter 失败、超限截断）、`WorkflowRunRecordingLineageTests`（自动 InputKey、根绑定 `$`、Literal/PublicData、显式动态键、未识别降级、失败保留原异常、标量与 DTO 输出键）和 `WorkflowVisionOutputRecordingTests`（真实 `ImageFrame` 只记录 FrameId 不记录像素），以及 §12.4 真实节点集成三例：`WorkflowRunRecordingNodeIntegrationTests`（Standard `StringCompareNode` 两个输入槽自动键 + 上游成员血缘）、`WorkflowRunRecordingAxisIntegrationTests`（Motion `AxisActionNode` 绑定槽 + 公共数据血缘）、`WorkflowRunRecordingRobotIntegrationTests`（Process `WaferRobotMoveNode` 三个绑定槽互不混淆）——集成三例已用"强制丢弃自动识别结果"的变异确认变红。评审修正轮另新增 5 例（Vision 嵌套坐标绑定真实管线集成、Degraded 期间不重复通知宿主、空属性输出回退 `$`、Trace 数据枚举器抛异常不 Fault）并逐条变异确认变红。全量构建 0 警告 0 错误；全仓 907 例、0 失败（Runtime 74、Core 46、Standard 66、Composite 7、Motion 13、Process 57、Vision 44 等）。

**边界：**阶段 F（Studio 实时覆盖层与分页 Reader、按节点/类型/Case 查询）与顶层保存 Adapter 未在本次实现；§20 的 100,000 次循环基准与 Adapter 子进程崩溃持久性测试属于基准/Adapter 侧验证；实施细节 §14 明确不处理的 Sink 超时/熔断/永久挂起、Ready 淘汰竞态与"阻塞期间独立推进 100ms 封包"仍未做。

## 前轮：联合恢复协议与处置步骤原操作继续

已实现[联合恢复SDK](nodes/joint-recovery.md)：按工艺角色注册独立的串行运行，统一阻断和设备退出核实，仅运行一次工艺处置；本地命名入口验证、操作清理和数据准备全部完成后，再次核实共同条件并授权。只等待的角色可保留原路径；原有Pause及独立Hold不被解除。角色句柄只提供监控与阻断，不暴露单独启动或跨流程跳转。

处置子流程使用本次调用专属步骤策略和原CaseId，允许核实并继续保留的操作，不递归重启工艺处理图。CylinderControl接入操作工厂：固定意图，首次发送阀命令，继续只等待原目标反馈；AllOff和不等待到位的命令不声明可继续。失败、停止、取消、准备拒绝及晚响应仍保守处理。新增协作Stamp与原子内存提交入口，旧轮次消息不能被重新盖章推进新操作。

最终全量 **725/725**（Process56、Motion12、Windows353）；Solution Release、独立WPF Debug/Release零警告错误，工程45/45。17项新增测试包含两/三流程、只等待角色、共同条件二次核实、处置图内部再次受阻、不重发气缸命令、取消、独立Hold和旧响应隔离。

**边界：本轮是同进程、无环串行且无子计划的参与运行及SDK装配，不是任意现有Host的自动接管。** 尚无联合策略可视化编辑器/多流程Studio工作台；重叠资源域、外部异常入口、循环/并行/Block恢复、动态替代处置、跨进程及持久化恢复仍未实现。既有普通Signal不会自动获得协作身份；现场安全核实和设备取消能力必须由宿主实现。独立DP.Vision未修改，未重复其独立验收。

## 前轮：真实人工交互与双平台恢复演示

已补齐[人工交互](nodes/operator-interaction.md)：UI无关任务控制器串行管理选择/确认，按任务身份拒绝过期与重复响应，当前和排队任务均可取消。WinForms/WPF增加真实非模态所属窗口，保留主界面停止操作，关闭提示不默认确认；隐藏重显及WinForms句柄重建不丢失待确认任务。

两个桌面样例可通过 `--recovery-demo` 启动工程师预设的三分支处理流程。真实窗口按钮驱动“继续原操作”和“回初始点重做”，软件模拟结果分别为位置110/10、命令1/2次、进料均1次。下次根运行前重编译当前处理文档，避免使用编辑前的旧快照。自动确认DemoOperatorService已移除；演示仍不连接任何物理设备。

新增两个Process桌面适配程序集，Solution **45/45**；最终全量 **708/708**（Windows353），Solution Release、独立WPF Debug/Release零警告错误。原生截图已检查。新增项目使用本机NuGet缓存还原，未声称修复网络源问题；未修改或重新独立验证DP.Vision。外部故障Case、受控设备中断、复杂并行回退及完整操作员授权仍待后续。

## 前轮：异常处理与恢复V1（运行内核）

已实现[受限V1](nodes/fault-recovery-v1.md)，决策见[ADR-0015](decisions/0015-engineered-fault-treatment-and-recovery-entries.md)：

- 普通失败和代码异常交给所属运行处理策略，不要求报警编号；显式StopRun仍终止。
- 工程师编写处理子流程，人工按预设少量选项路由，不根据按钮名称暗中产生恢复裁决。
- 新增命名恢复入口、继续原操作、从入口重执行三个Process节点，模块共30种。
- 可选操作工厂固定意图并保留OperationId；伺服Handler已接入。循环第7轮原操作继续、假轴命令次数及输入不重读已验证。
- 命名入口限无环串行，需实际经过且通过工位Guard；失效旧输出及相关局部变量，保留历史，不回滚公共数据或外部动作。
- 处理子流程纳入监控、预检、准备、Pause/Hold/取消；拒绝请求保留会话及原因。处置子流程自身异常保守终止，不盲目从头重放。
- 修正无关OCE分类、Block取消转故障、操作工厂晚返回后仍执行的问题；操作清理失败不发布成功输出。
- 旧任意Jump禁用，保留旧模型显示并标注已禁用；并行恢复保护不放宽。

最终全量 **685/685**（Windows339），Solution Release与独立WPF Debug/Release均零警告错误，项目核对43/43。本轮未修改独立DP.Vision，也未重跑其独立verify。假轴测试不代替现场设备与安全互锁验收；未新增真实操作台，已有自动确认Demo适配器不可视为人工验收。

下一步仍包括：外部故障Case与持续条件、受控中断、更多设备操作契约、并行/跨循环/Block恢复计划、真实现场交互。完整目标见[场景草案](node-recovery-scenarios-design.md)；[历史恢复审查](node-fault-recovery-review.md)及[平台清单](node-platform-review-backlog.md)不因本版局部落地而全部关闭。

## 当前：全部面积ROI算子与父子定位

范围能力声明和公开解析入口取代节点类型白名单；线圆测量、平移定位、旋转尺度定位已接入精确ROI随动，支持父子定位且不重复补偿。完整Workflow663项（Windows339）通过；Vision两框架各115/67/5及原生探针/Demo smoke通过，构建零警告/错误。具体语义见[定位坐标系](nodes/vision-coordinate-systems.md)，间歇UI测试记录见[实施记录](dp-vision-integration-plan.md)。

## 前轮：模板定位坐标系与ROI随动

共享定位定义身份/模板签名/本帧矩阵、局部ROI制作与运行转换、双坐标事实已经落地；两平台面积ROI页面显式绑定，不在打开或刷新时改正式配置。8项新增Workflow集成通过，完整Workflow661项（Windows337）；Vision两框架各115/63/5，原生探针及Demo smoke通过。构建零警告/错误。支持节点和边界见[定位坐标系](nodes/vision-coordinate-systems.md)。

## 视觉主线：18节点算子链路

已增加8个真实算子节点，覆盖预处理、Region分割/形态学、Blob特征筛选、亚像素采样卡尺、鲁棒直线、离散旋转/尺度定位及正反坐标映射。双宿主默认文件算子流程可直接运行；用法见[新增算子](nodes/vision-operators.md)。相机现场工作延期。

## 旧兼容退役基线

旧视觉的11个解决方案工程、节点、页面、兼容Adapter、Paddle专用工具和引用已删除；只保留独立DP.Vision的十种节点和原生双平台页面。相机由DP.Vision.Halcon直接实现；旧文档不静默转换。当前状态见[实施记录](dp-vision-integration-plan.md)与[ADR-0014](decisions/0014-vision-clean-break.md)。下方较早Phase中的旧视觉实现描述仅是历史进度，不代表仍保留这些API。

## Phase 1：基础契约与最小运行闭环

状态：**基础闭环已完成，进入数据契约深化阶段**

### 已完成

- [x] 创建独立 `DP.WorkFlow/DP.WorkFlow.sln`；
- [x] 创建 `DP.WorkFlow.Abstractions`；
- [x] 创建 `DP.WorkFlow.Core`；
- [x] 创建 `DP.WorkFlow.Runtime`；
- [x] 创建 `DP.WorkFlow.Nodes.Standard`；
- [x] 创建 Core、Runtime、Standard Nodes 三个 xUnit 测试项目；
- [x] 统一 net8.0、Nullable、x64、中央包版本和警告即错误；
- [x] 建立旧类型迁移表与第一份 ADR；
- [x] 将节点配置与 Handler 执行职责分离；
- [x] 使用显式 `FromPort/ToPort` 替代节点直接指定后继节点；
- [x] 实现 `WorkflowDocument`、Graph/Layout/Entry 分层、结构校验和 `WorkflowCompiler`；
- [x] 实现结构化 `WorkflowContext`，禁止以 null 表达变量删除；
- [x] 实现节点 Handler Catalog；
- [x] 实现 Start → Action → End 顺序运行闭环；
- [x] 实现节点输出、基础 Trace、取消和运行结果；
- [x] 首次构建达到 0 warning / 0 error；
- [x] 创建 `DP.WorkFlow.Persistence.Json` 和持久化测试项目；
- [x] 增加线程安全节点目录、稳定 Key 和重复注册诊断；
- [x] 引入 `WorkflowBindingKey`、`WorkflowInput<T>` 和固定值/绑定值来源模型；
- [x] 增加画布尺寸、输入端口和 NodeType 校验；
- [x] 增加不可达节点编译警告；
- [x] 实现 Schema 4 JSON 文档、显式入口、子文档、严格节点版本和原子文件保存；
- [x] 实现旧 Schema 1/2/3 迁移，旧 Label 映射为 PortKey；
- [x] 插件缺失时使用 `UnknownWorkflowNodeModel` 无损保留原始配置；
- [x] 实现运行时绑定成员路径解析和访问计划缓存；
- [x] 实现数字、枚举、Guid、TimeSpan 等绑定类型转换；
- [x] 绑定空值、缺少输出和无效成员使用结构化 `WorkflowBindingException`；
- [x] 实现 `DecisionNodeModel`、函数/绑定条件及 True/False 端口；
- [x] 实现 `WaitFunctionNodeModel`、Success/Timeout 端口及无限等待语义；
- [x] 等待不再使用“3650 天”模拟无限超时；
- [x] 增加 `WorkflowConditionRegistry`；
- [x] 修复 `WorkflowBindingKey` 默认 JSON 反序列化丢值问题，改用稳定字符串 Converter；
- [x] 增加 `WorkflowPortDescriptor`、输入/输出方向和连接基数；
- [x] 节点目录开始公开稳定端口元数据；
- [x] 图编译器可拒绝未声明端口和超基数连接；
- [x] 实现可取消的 `DelayNodeModel`；
- [x] 实现固定次数 `LoopNodeModel` 及 Loop/Completed 端口；
- [x] 实现端口化 `JumpNodeModel`，目标由连接表达；
- [x] 引擎增加单节点和总节点执行次数安全上限；
- [x] 错误循环会以明确 Faulted 结果终止，不再无限占用线程；
- [x] 添加 ADR-0002 记录端口与循环安全决策；
- [x] 增加带 RunId、Sequence 和时间戳的不可变运行快照；
- [x] 增加节点状态、执行次数、开始/结束时间和耗时；
- [x] 增加有界内存 Trace 队列及不可变 `WorkflowTraceBatch`；
- [x] 实现无轮询线程占用的异步 Pause/Resume 调度门；
- [x] 实现可去重、可独立解除的 ExternalHold 原因集合；
- [x] 多个 Hold 未全部解除前不会错误恢复运行；
- [x] 快照和 Trace 数据使用只读副本，避免 UI 修改运行数据；
- [x] 事件观察者异常与引擎执行结果隔离；
- [x] 添加 ADR-0003 记录快照与 Hold 决策；
- [x] 实现 `ParallelAllNodeModel` 与 `WaitAllInputsCompletedNodeModel`；
- [x] Core 使用共同可达、后支配和分支互斥规则解析并行汇聚；
- [x] 缺少共同汇聚节点时以 WF021 阻止运行；
- [x] Runtime 并发执行多个分支并确保汇聚节点只执行一次；
- [x] 支持嵌套并行作用域；
- [x] 任一分支失败时取消同作用域兄弟分支并保留原始错误；
- [x] 快照增加多活动节点和并行作用域完成进度；
- [x] Pause、ExternalHold、取消和执行次数保护可作用于并行分支；
- [x] 添加 ADR-0004 记录结构化并行决策；
- [x] 节点输出增加 RunId、TokenId、ScopeIds 和节点执行次数；
- [x] `WorkflowRunState` 保存完整输出历史、并行可见性、故障事实和最新快照；
- [x] 当前 Token 可读取自身和祖先 Token 输出；
- [x] 并行兄弟分支在汇聚前不能互相读取输出；
- [x] 分支输出在汇聚后对父 Token 可见；
- [x] 嵌套并行输出按已完成作用域逐层开放；
- [x] Trace 和快照增加 Token/Scope 身份及活动 Token 视图；
- [x] 实现 `IWorkflowRuntimeHost` 和 `WorkflowRuntimeHost`；
- [x] RuntimeHost 负责 Configure、编译、引擎创建、任务复用、取消和 Reset；
- [x] 添加 ADR-0005 记录 Token 级输出可见性；
- [x] 新增独立 `DP.WorkFlow.Nodes.Composite` 和对应测试工程；
- [x] 实现 UI 无关 `IWorkflowSubDocumentNode`、`BlockNodeModel` 与 Handler；
- [x] 父流程编译时递归编译子画布，不在节点执行时临时解释配置；
- [x] 编译器和 JSON 存储检测子画布循环引用；
- [x] 子 Context 复制父变量并隔离 NodeOutputs 和变量写入；
- [x] 父引擎监管子引擎并传播 Pause、Resume、ExternalHold 和取消；
- [x] 父快照通过 `ChildWorkflows` 汇总嵌套子运行快照；
- [x] Schema 4 JSON 支持递归 SubDocument、独立入口和布局往返保存；
- [x] 添加 ADR-0006 记录受监管子流程决策；
- [x] Block 增加显式 InputMappings 和 OutputMappings；
- [x] 输入映射支持 Literal、父变量和父节点绑定；
- [x] 输出映射支持子变量和子节点绑定；
- [x] 映射采用先解析后提交，拒绝重复目标、缺失来源和 null Variable；
- [x] BlockNodeOutput 暴露已映射输出摘要；
- [x] JSON 完整往返保存 Block 映射及 BindingKey；
- [x] 实现 ValueCompare 和 StringCompare 节点及 Handler；
- [x] 数值和日期使用 InvariantCulture，字符串使用 Ordinal 规则；
- [x] 修复旧版容差可能使 Equal 与 GreaterThan 同时成立的问题；
- [x] Like 使用 NonBacktracking 正则和执行超时；
- [x] 添加 ADR-0007；
- [x] WorkflowNodeDescriptor 增加标准输出 CLR 类型元数据；
- [x] 实现 WorkflowBindingAnalyzer 和设计器候选模型；
- [x] 自动提取 WorkflowInput<T> 及节点声明的复杂绑定；
- [x] 使用支配分析判断条件图中的来源必然可用性；
- [x] 支持 ParallelAll 汇聚后的跨分支输出候选；
- [x] 校验来源节点、成员路径和目标类型兼容性；
- [x] 校验 Block 子输出映射在所有完成路径上的可用性；
- [x] 增加 WFB001-WFB005 诊断；
- [x] RuntimeHost 支持运行前 Pause 和 ExternalHold；
- [x] 添加 ADR-0008；
- [x] 新增 DP.WorkFlow.UI.Shared、DP.WorkFlow.UI.WinForms、DP.WorkFlow.UI.Wpf；
- [x] 双 UI 共享 WorkflowDesignerSession 和同一 Canvas；
- [x] 实现节点创建、删除、拖动、选择及 Undo/Redo；
- [x] 实现显式端口绘制、拖动连线和连接基数检查；
- [x] 实现平移、指针中心缩放和 FitToView；
- [x] 实现运行状态颜色覆盖和跨线程刷新；
- [x] 实现 WinForms/WPF 原生工具箱；
- [x] 添加 ADR-0009 和 UI 使用文档；
- [x] 实现共享 WorkflowPropertyInspectorModel；
- [x] 实现 WinForms/WPF 原生属性面板；
- [x] 支持标量、布尔、枚举及 WorkflowInput<T> 编辑；
- [x] 属性面板接入设计期强类型绑定候选；
- [x] 实现 WorkflowDesignerNavigator；
- [x] Block 双击进入子画布、上一级及面包屑导航；
- [x] 子画布自动映射对应 ChildWorkflows 快照；
- [x] 实现两套组合式 WorkflowStudioControl；
- [x] WPF Session 使用依赖属性并处理加载/卸载订阅；
- [x] 增加 Windows STA 控件冒烟测试；
- [x] 实现共享 WorkflowDiagnosticsModel；
- [x] 实现 WinForms/WPF 编译诊断列表和节点定位；
- [x] 存在编译错误时阻止 Studio 启动运行；
- [x] IWorkflowRuntimeHost 公开快照事件；
- [x] 实现 WorkflowStudioRuntimeBinding；
- [x] 双 Studio 增加运行、暂停、继续、停止和状态显示；
- [x] 实现连接命中选择、高亮、Delete 删除和 Undo；
- [x] Session 增加 Waypoint 添加、移动、删除和 Undo/Redo；
- [x] 双画布使用持久化 Waypoints 绘制折线路径；
- [x] 双击连接添加 Waypoint；
- [x] 拖动 Waypoint 调整路径，Shift+单击删除；
- [x] Waypoint 参与连接命中检测；
- [x] Session 支持节点多选和批量选择；
- [x] 双画布支持 Ctrl 多选和拖动框选；
- [x] 多节点整体拖动和批量删除作为单次 Undo；
- [x] 支持六种对齐和水平/垂直等距分布；
- [x] 实现连接驱动的稳定分层自动布局；
- [x] 实现 WorkflowRuntimeMonitorModel；
- [x] 双 UI 增加 Token、ParallelScope、ChildWorkflow 监视页；
- [x] 实现 WorkflowDocumentWorkspace；
- [x] 支持新建、打开、保存、另存为和原子写入；
- [x] 支持脏状态、放弃修改确认入口和最近 10 个文件；
- [x] 打开旧文档时保留 WorkflowMigrationReport；
- [x] 双 Studio 集成原生文件对话框和文件命令；
- [x] RuntimeMonitor 增加有界 Trace 实时页；
- [x] Trace 显示 Run 序号、节点、Token、Scope、步骤和消息；
- [x] 实现 WorkflowBlockMappingEditorModel；
- [x] Block 输入输出集合整体替换支持单次 Undo；
- [x] 父输入绑定使用消费者可达性候选；
- [x] 子输出绑定使用子画布静态输出候选；
- [x] 实现 WinForms Block 映射表格对话框；
- [x] 实现 WPF Block 映射表格窗口；
- [x] 双属性面板为 Block 提供专用编辑入口；
- [x] 实现 WorkflowBindingTreeModel；
- [x] 候选按来源节点和成员路径分层；
- [x] 支持按节点、成员、路径和 CLR 类型搜索；
- [x] WinForms/WPF WorkflowInput 使用原生绑定树选择器；
- [x] Trace 支持节点、Token、Scope、步骤和消息筛选；
- [x] Trace 当前筛选结果支持 UTF-8 CSV 导出；
- [x] 新增 `docs/USAGE_GUIDE.md` 整体使用指南；
- [x] 使用指南覆盖无 UI 运行、双 Studio、绑定、并行、Block、持久化、迁移、监视和自定义节点；
- [x] 工具箱改为分类树并支持拖放创建节点；
- [x] 双画布连线改为正交折线并同步命中检测；
- [x] 端口及多输出连线显示稳定 PortKey 标签；
- [x] 节点实例支持四边端口布局、右键切换、Undo 和 JSON 往返；
- [x] 修复 WinForms WorkflowInput 属性行重叠；
- [x] 空 Block 首次双击时自动创建 Start，不再抛出缺少入口异常；
- [x] 修复多选后按住任一已选节点拖动会丢失其他选择的问题；
- [x] 双画布支持 Ctrl+A 全选并整体移动；
- [x] RuntimeBinding 支持运行前用最新根画布重新 Configure；
- [x] Studio 切换新建/打开文档时同步 RuntimeBinding 导航器；
- [x] samples/DP.WorkFlow.WinForms.Sample 已接入完整 Handler、RuntimeHost 和运行前配置；
- [x] 修复双击 Block 后父画布 MouseUp 在子画布查找节点导致的 Sequence 异常；
- [x] 正交路由按端口所在边增加 28px 出入线净空；
- [x] 隐藏节点正文重复英文 NodeType，并将端口标签移到节点外侧；
- [x] 移除 WinForms/WPF 节点和端口的右键端点选择菜单；端口所在边仅通过直接拖动端口调整；
- [x] 修复 Block 成功后错误返回 CompleteCurrentPath，现改为 Success 端口继续父流程；
- [x] 增加 Block 后继 ParentEnd 实际完成的回归断言；
- [x] 节点显示 ID、执行次数、动态耗时和活动 Token；
- [x] WinForms/WPF 以 100ms 刷新运行中节点耗时；
- [x] 修复 ContextMenuStrip 在 Closed 回调中提前 Dispose 的异常；
- [x] 实现选中节点四边落点和端口直接拖动换边；
- [x] 输出端口拖离节点后无缝切换为连线手势；
- [x] 四边落点缩小并贴合节点边缘，仅选中时显示；
- [x] 进入 Block 后立即从已保存根快照映射子节点运行状态；
- [x] WinForms 正交连线支持拖动中间线段并整体生成可撤销 Waypoint；
- [x] 自动正交路由检测中间节点并选择较短的上/下或左/右绕行通道；
- [x] RuntimeBinding 支持 RootWorkflow/CurrentCanvas 两种运行目标；
- [x] 进入 Block 后运行按钮切换为“运行当前子流程”；
- [x] 当前子流程独立运行不会启动根流程，并将快照直接映射到当前画布；
- [x] 框选并整体拖动连接两端节点时，手工 Waypoint 与中间线段实时同步平移；
- [x] MoveNodes 将同位移端点和连接 Waypoint 合并为一次 Undo/Redo；
- [x] Waypoint 增加 6 画布单位邻近吸附和重复点合并；
- [x] 自动删除连续共线的冗余路径点，防止反复拖动后端点累积；
- [x] Add/Move/Remove/Replace Waypoint 统一执行路径规范化；
- [x] 节点空闲时不再显示 `ID xxx`；
- [x] 节点运行信息精简为 `#次数  耗时`；
- [x] 输入/输出端口定义移动到节点边界内部；
- [x] WinForms/WPF Studio 移除常驻右侧属性栏，扩大画布宽度；
- [x] 双击节点弹出原生模态参数窗口，Block 窗口提供“进入子流程”；
- [x] WinForms 属性提交期间抑制同步全量重建，减少控件闪烁和焦点丢失；
- [x] 新增共享 `WorkflowOrthogonalRouter`，替换按全局包围盒绕行的简单算法；
- [x] 使用稀疏可见性网格和 A* 选择正交路径；路由代价改为“先最少转角、再最短距离”的字典序优化；
- [x] 起点和终点端口方向参与转角计数，避免端口净空之后立即产生多余折返；
- [x] 路由同时考虑端口方向、28px 出入线、14px 节点安全区和手工 Waypoint；
- [x] 路由结果自动删除重复点与共线折点，WinForms/WPF 绘制和命中统一使用同一路径；
- [x] 障碍物搜索限定在连线附近 160px，避免远处无关节点导致大范围绕行；
- [x] 移动连接单端节点时实时清除旧手工路径约束并重新执行自动路由；
- [x] 同时移动连接两端时仍整体平移 Waypoint，保持人工路径形状；
- [x] 单端移动、路径清理和节点坐标合并为同一个可撤销操作，Undo 恢复原路径；
- [x] 取消拖动或切换设计会话时恢复临时节点坐标和 Waypoint，避免未提交修改残留；
- [x] 节点运行摘要移至标题栏右侧，避免与四边端口说明重叠；
- [x] 标题栏按运行摘要实际宽度预留独立区域，长标题使用省略号裁剪，不再覆盖 `#顺序 耗时`；
- [x] 运行摘要 `#x` 改为当前运行中的节点启动顺序号，而不是节点累计执行次数；
- [x] 循环节点重复执行时覆盖为最近一次当前工作流执行顺序，便于定位流程停留位置；
- [x] 标题、运行摘要、端口说明、连接标签、状态点和端口半径随 Zoom 缩放；
- [x] 工具箱节点拖放到连线 14px 命中区时自动拆分原连接并插入节点；
- [x] 自动插入会按命中线段方向配置新节点输入/输出端口边，并作为一次 Undo 操作；
- [x] 长节点名称不再使用省略号；Session 根据中英文标题宽度自动扩展节点并预留运行摘要区域；
- [x] 节点新增、直接移动、拖动以及手工 Waypoint/中间线段统一吸附 24 单位可见栅格；
- [x] 节点吸附基准由左上角改为几何中心，宽度随标题变化后仍能与不同尺寸节点和垂直连线居中；
- [x] 标题触发节点尺寸变化时可双向扩宽/缩窄，保持原中心并重新吸附到最近栅格交点；
- [x] 拖放节点插入水平/垂直连线时以落点为中心，不再因动态宽度产生横向偏移；
- [x] 顶部及左右端口说明限制在标题栏下方的正文区域，避免与节点名称重叠；
- [x] 画布、节点、工具栏、参数弹窗和主要输入控件采用固定的中性深色配色；主题枚举、切换入口、广播服务和动态主题分支已删除，待未来由整个应用统一设计；
- [x] WinForms 固定样式器覆盖 TreeView 节点前景色和 DataGridView 单元格/表头/选择态，修复白底白字；
- [x] WinForms/WPF 参数下拉项选中态统一使用蓝底白字，修复选中内容不可见；
- [x] 连接模型新增 `FromSide/ToSide` 实例级端点边覆盖，同一输入端口的不同入边可落在上、左、右或下边；
- [x] 创建连线时目标节点显示四边临时输入端点，释放到哪一边就保存该连接的 `ToSide`；
- [x] 多入边端点、端口标签、正交路由和命中检测使用同一个连接级边配置；
- [x] 连接级端点边进入 JSON 往返，旧 JSON 缺省时继续使用节点端口布局；
- [x] 连线箭头改为目标端点前方的内缩三角箭头，不再被节点端点圆覆盖；
- [x] 连接级附加输入端点改在节点填充和边框之后绘制，四边均显示完整圆点；
- [x] 节点标题栏使用圆角裁剪，边框最后绘制并提升为普通 2px/选中 3px，修复顶部圆角缺失；
- [x] 单一通用 `Input`/`Success` 不再显示节点内端口名，仅多端口节点显示具有分支语义的端口名称；
- [x] 多输出连线标签默认位于整条折线中点，支持沿路径拖动并通过 `LabelPosition` 持久化和 Undo/Redo；
- [x] 四边端口的路由轴坐标吸附 24 单位栅格，多输出端口减少因非栅格坐标产生的额外小折线；
- [x] 调整节点端口边时，同步迁移仍使用旧边的连接级 FromSide/ToSide，其他独立多入边保持不变；
- [x] 单节点和框选节点支持 Ctrl+C/Ctrl+V，内部连接、Block 子画布、端口边、Waypoint 和标签位置一并克隆；
- [x] 每次粘贴偏移一个 24 单位栅格，生成稳定新 ID，并作为一次 Undo 操作；
- [x] 连线不再显示红色删除符号或菜单确认；连线已选中时右键直接删除，未选中时右键仅选中；同时保留 Delete 快捷键；
- [x] 未连接端口不再常驻显示；只有实际建立连接后才显示绿色输出端点或紫色输入端点；
- [x] 连接端点缩小为原尺寸约 72%，节点四边的中性拖拽锚点同步缩小；
- [x] 连接级边覆盖端点按“端口 + 边”去重，修复拖动端点后默认边和新边同时出现的问题；
- [x] 单输出节点可直接从选中节点的四边中性锚点拖出连接，未连接时不需要显示彩色端点；
- [x] 单个左右端点严格位于节点整条边的几何中点，多端点才沿边分布并吸附栅格；
- [x] 多输出节点的参数窗口新增“输出端口”复选项；未勾选端口不显示、不可连线，并自动删除该端口已有连线；
- [x] 输出端口可见性进入 JSON、复制粘贴和 Undo/Redo；旧文档默认所有输出端口可见；
- [x] 单进单出节点继续通过四边中性锚点人工确定输出边和输入边，多输出节点仅对已勾选端口进行分布；
- [x] 考虑多输出端口正文占用，运行顺序与耗时摘要恢复到标题栏右侧；
- [x] 修复左右侧多输出端口名称与圆点纵向错位：多端点在正文区域分布，标签始终以对应端点纵坐标居中；
- [x] 节点程序集按均衡职责固定为 Standard、Composite、Motion、Process，并记录依赖边界；
- [x] 新建 `DP.WorkFlow.Nodes.Motion`，首批实现 IORead/IOWrite/IOWait、`IWorkflowIoService` 和统一注册入口；
- [x] 新建 `DP.WorkFlow.Nodes.Process`，首批实现 SafePoint/WarnStopStation、`IWorkflowRecoveryService` 和统一注册入口；
- [x] Standard 新增 SignalSet/SignalWait 和基于异步通知、无轮询的 `WorkflowSignalService`；
- [x] WinForms 测试宿主已加载新节点工程及模拟 IO/恢复/信号服务；
- [x] 每个具体节点独占一个 `*Node.cs`；拆分 Signal、IO、Recovery 及原并行/汇聚混合文件，并统一重命名旧 `*NodeModel.cs`；
- [x] Motion 补全 IOMultiCheck/IOMultiWait、AxisAction/Servo/Stop/Wait、CylinderControl/Wait、VacuumControl/Wait；
- [x] Motion 补全 CodeReaderOpen/Close/Trigger/WaitScan，并增加 Axis、Pneumatic、CodeReader 注入服务契约；
- [x] Standard 补全 ConvertValue、QueueAdd/Remove/Read/Wait，并新增非轮询线程安全命名队列服务；
- [x] Process 补全 WarningPrompt、OperatorChoice、OperatorStepConfirm 和 UI 无关操作员服务；
- [x] Standard 补全 SignalValueSet/Wait、WaitSignals、SignalState/ValueBatchInitialize；
- [x] Process 补全 WarnRetryCurrentNode、WarnJumpToNode、WarningHandlerBlock/Start；
- [x] Process 补全 ProductMove、ProductCreate、Station 判断/等待/完成全部旧节点；
- [x] Process 补全 WaferRobot Initialize/Home/Snapshot/Stop/WaitIdle/Move/Pick/Place；
- [x] 每个具体节点继续独占一个 `*Node.cs`，当前测试基线 163 个测试全部通过；
- [ ] 保真审计发现后续批量节点中存在参数和业务语义失真；“旧 NodeType 全部完成”结论已撤销；
- [ ] 正按 `docs/nodes/fidelity-audit.md` 从 IO、Axis、气动、CodeReader 开始逐节点对照旧代码重写；
- [x] AxisAction 已撤销错误的“单轴绝对位置”模型，恢复 StepKey/绑定来源、WaitForCompleted、OverrideTimeoutMs、ResultVarKey 和 AxisStep 结果语义；
- [x] AxisServo/AxisStop 恢复 DeviceId/AxisId 来源、等待完成、超时、轮询、结果变量和旧结果字段；
- [x] AxisWait 恢复 InPosOff/Homed/ServoOn/AlarmOff/PositionReached/PositionPassed、目标位置来源及容差；
- [x] IORead 恢复 DriveId/Index/Input-Output 类型和非布尔值结果；IOWrite 恢复七种命令、等待、超时、动作延时及绑定值；
- [x] IOWait 恢复 Instant/Hold、HoldMs、PollIntervalMs、TimeoutAsFalseBranch；IOMultiCheck/Wait 恢复 DriveId/Index/IOType 条件、All/Any、保持与结果明细；
- [x] CylinderControl/Wait 恢复 CylinderName、Extend/Retract/AllOff、等待到位与超时；
- [x] VacuumControl/Wait 恢复 VacuumOn/Off/OffWithBlowOff/BlowOffPulse、破真空时长、到位等待及轮询；
- [x] CodeReader Open/Close 恢复布尔成功判定，Trigger 恢复 AutoOpenWhenClosed；
- [x] CodeReaderWaitScan 恢复 TimeoutMs、TriggerBeforeWait、AutoOpenWhenClosed、五种 MatchMode、ExpectedCode、ResultVarKey 和 Timeout 分支；
- [x] ProductCreate 已移除通用 Operation/Payload 骨架，恢复 Station/Slot/ProductId/Recipe/Reason 各自来源、自动产品 ID、结果变量和旧结果字段；
- [x] ProductMoveStart/Success/Failed 已恢复 Normal/VirtualCreate、来源/目标工站槽位、会话事务、失败回滚参数和强类型结果；
- [x] StationCanReceive/CanSend/SlotHasProduct/Finished/WaitCanReceive/WaitCanSend 已恢复独立强类型服务、True/False 或 Success/Timeout 分支、轮询和结果结构；
- [x] 旧 Schema 属性迁移支持将 `*BindingKey` 映射到新版 `WorkflowInput<T>` 的 `*Binding`；
- [x] WaferRobot Initialize/Home/Stop/Move/Pick/Place/ReadSnapshot/WaitIdle 已恢复 RobotKey、目标站位/槽位/手臂来源、停止模式、等待完成、状态轮询和完整结果字段；
- [x] 删除 Process 中临时 `WorkflowProcessNodeModel`、`WorkflowProcessRequest`、`WorkflowProcessResult`、`WorkflowProcessNodeHandler` 及 `Operation/Payload` 执行路径；
- [x] SignalValueSet/Wait 恢复 String/Int32/Int64/Double/Boolean、Set/Increment/Decrement、NextWrite/ExistingOrNextWrite、六种比较运算、版本快照和完整结构化结果；
- [x] WorkflowValueSignalService 改为强类型版本化存储，并使用广播式异步变更通知，保留旧字符串 API 兼容层；
- [x] QueueInitialize/Add/Remove/Read/Wait 恢复固定值类型、自动创建、防重复、版本快照、四种等待条件和完整首项/受影响值结果；
- [x] WorkflowQueueService 改为强类型版本化命名队列，并保留旧字符串队列 API 兼容层；
- [x] SignalState/SignalValue 批量初始化恢复初始化项、默认值类型转换、KeysText 和结构化结果；
- [x] WaitSignals 恢复工作流 Context 一次性信号语义，父子流程共享信号，并以异步广播通知替代旧 50ms 轮询；
- [x] ConvertValue 删除自行增加的 Guid/TimeSpan/DateTimeOffset 等目标类型，恢复五种 E_SignalValueType、Literal/Binding、ResultVarKey 和全部旧结果字段；
- [x] WarningHandlerBlock 删除 HandlerKey 骨架，恢复不可连接的专用告警子画布、默认告警入口模板和禁止普通主流程执行语义；
- [x] WarningHandlerStart 恢复完整 WorkflowInterruptContext 输出；补齐中断来源、严重度、报警、现场数据及安全点契约；
- [x] SafePoint 恢复空键回退节点 ID 和 `$CurrentSafePoint` 上下文；恢复 WarnRetryCurrentNode、WarnJumpToNode、WarnStopStation 的 WarningResolution 出口协议；
- [x] OperatorChoice 恢复 OptionsText 任意选项 Key 和动态输出端口；WarningPrompt/OperatorStepConfirm 恢复确认后单出口语义及中断上下文提示文本；
- [x] 新增 IWorkflowDynamicPortProvider，编译校验、连接基数和共享设计器统一读取实例动态端口；配置删除端口时自动清理失效连线；
- [x] 当前 Schema 兼容临时 RetryCurrentNode/ReturnToSafePoint 类型，迁移为 WarnRetryCurrentNode/WarnJumpToNode，并迁移 SafePointKey；
- [x] Runtime 新增 IWorkflowFaultRecoveryCoordinator，仅对 RequestRecovery 故障启动恢复，支持 RetryFaultedNode/JumpToNode/Stop 及恢复次数上限；
- [x] WorkflowWarningHandlerCoordinator 可编译并执行专用告警子画布，将 WarningResolution 转换为引擎恢复裁决；
- [x] 恢复期间引擎自动进入 `Recovery:{NodeId}` ExternalHold，裁决完成后解除；StopRun/StopRun 不会误入恢复流程；
- [x] Axis、可中断 IO、气动、CodeReader、WaferRobot 命令及等待节点重新接入 RecoverableInterruptOption；等待节点恢复 TimeoutAsFalseBranch 分流；
- [x] 恢复请求和 WorkflowInterruptContext 保留 AlarmCode、故障节点标题、恢复次数及执行身份；
- [x] 增加 Retry/Jump/Stop、恢复上限、恢复中取消、ExternalHold 清理和中断上下文完整性测试；
- [x] RecoverableInterruptOption JSON 兼容旧版整数、字符串、对象及字符串 AlarmCode；
- [x] 清理可恢复节点 Handler 的全异常兜底：配置错误、缺少服务和代码缺陷不再被误判为设备可恢复故障；
- [x] Axis、IO、气动、CodeReader、WaferRobot 和工站等待的预期设备失败改为显式 NodeExecutionResult，不再用异常表达正常失败分支；
- [x] ConvertValue/Queue 结果投影删除无条件 catch(Exception)，仅处理预期的格式、类型和溢出异常；
- [x] 清理 samples/DP.WorkFlow.WinForms.Sample 收尾缺陷：删除 WPF/测试工程反向引用，补齐示例 Action/Condition，增加进程级异常记录，关闭时取消运行、解绑事件并幂等释放资源；
- [x] WinForms/WPF Studio 对返回 Faulted 的 WorkflowRunResult 显式报告，不再只处理抛出的异常而静默遗漏运行失败；
- [x] WinForms/WPF 参数弹窗增加分类树和底部参数说明区，支持 `WorkflowPropertyVisibleWhen` 及 Source/Binding 命名约定条件显示；
- [x] 节点默认端口方向改为上进下出，多输出端口及名称常驻显示；
- [x] 节点标题与运行序号/耗时按最新要求恢复同一标题层，节点尺寸根据标题、端口名称、端口边和端口数量自动扩缩并保持中心吸附；
- [x] 新增 Roslyn `CSharpScript` 节点，支持编译缓存、受控 Globals、取消、Trace、结果变量和可选 Error 动态端口；
- [x] Dictionary、List 和复杂对象进入通用结构化 JSON 编辑器；常用标量 List、对象 List 和字符串键 Dictionary 增加 WinForms/WPF 专用表格编辑；
- [x] Trace 监视增加暂停滚动，并新增按累计/平均耗时排序的节点 Timing 趋势页；
- [x] CSharpScript 参数改用专用智能编辑器：WinForms 语法着色、双 UI Ctrl+Space 补全、API 候选和 350ms Roslyn 实时诊断；
- [x] WinForms/WPF 双击节点统一进入自适应节点信息窗口：普通节点直接铺满参数表；特殊节点左侧为参数树表、右侧直接平铺 Block 子画布、脚本或图像，不再要求用户选择页面；
- [x] 参数区改为紧凑的两列自定义分组属性表格，支持分类折叠、Boolean 复选框、搜索、底部属性说明及复杂值专用编辑；
- [x] 节点参数使用独立编辑副本，只有“应用/确定”才作为一个可撤销文档操作提交；“取消”或直接关闭不会修改正式节点；
- [x] 消除参数和脚本编辑频闪：脚本输入不再逐键触发 Session/参数区重建，WinForms 属性区启用双缓冲，分类折叠改为原位显隐，搜索使用 180ms 防抖，WinForms 全文语法着色仅在打开或显式编译时执行；
- [x] 参数行及其所有子编辑控件统一响应鼠标和键盘焦点，底部说明实时切换；新增 `WorkflowPropertyAttribute` 中文参数元数据，并为未标注参数提供覆盖全部节点的中文约定回退；
- [x] 新增 UI 无关 `IWorkflowScriptNode`、`IWorkflowImageDisplayNode`、图像帧源/解析器及 `IWorkflowNodeEditorPageProvider`，宿主可注册自定义共享页面模型和双原生渲染器；
- [x] Block 子画布可直接嵌入节点工作台编辑，并向父 Session 传播文档变更；视觉帧采用复制后的不可变快照并在窗口关闭时停止和释放帧源；
- [x] 移除旧“通用图像编辑页”残留双轨（2026-09-20）：删除 `IWorkflowImageDisplayNode`、`WorkflowImageFrame`、`WorkflowImagePixelFormat`、`IWorkflowImageFrameSource`、`IWorkflowImageFrameSourceResolver`、`WorkflowPluginModuleGroups.Vision`、内置 `WorkflowImageEditorPageProvider` / `WorkflowImageEditorPageModel`、两平台 `ImageFrameSourceResolver` 与 `PageKind.Image` 渲染分支、`WorkflowImageViewport`；节点详情页扩展统一由 `IWorkflowNodeEditorPageProvider` + `RendererKey` 承担（全解决方案 0 警告 0 错误，`UI.Shared.Tests` 62 通过 / `UI.Windows.Tests` 351 通过）；
- [x] 增加 Axis 超时分支/恢复/取消、大型障碍场路由、反馈回环 Waypoint 和两层嵌套 Block 导航测试；
- [x] 节点高度改为按左右边端口数自动计算：默认上进下出节点由 84 压缩至 64，多侧边端口仍按数量自动增高；
- [x] 单节点耗时改用 `Stopwatch` 仅统计节点调度/Handler 执行，不再包含快照观察者和运行监视 UI 耗时；Studio 快照投递改为异步合并，亚毫秒耗时保留三位小数。
- [x] 视觉显示改为厂商原生 Renderer 优先、通用像素画布兜底；新增原生图像引用计数租约、原生帧单槽背压和 WinForms/WPF HALCON HWindow Renderer，ROI/Overlay 继续使用通用模型持久化。
- [x] 视觉采图节点支持 Camera/File/Folder 三种来源；HALCON 本地图像保持 HImage 原生链路，文件夹按自然顺序逐次读取并支持循环、停留末张和结束失败，输出当前路径、索引及总数。
- [x] 新增 UI 无关 `WorkflowPropertyEditorAttribute` 和稳定 EditorKey；WinForms/WPF 参数区支持原生文件/文件夹浏览器、过滤器与路径存在性校验，视觉采图节点已接入。
- [x] 修复原生图像未经过像素帧发布时，运行后再打开节点详情无法显示：`VisionDisplayHub` 保留最新原生租约并为迟到的通用帧订阅者按需转换、回放最新帧。
- [x] 修复重复运行视觉资源持续增长：`VisionResourceTracker.ReleaseAllAsync` 可复用清理，根 `WorkflowRuntimeHost` 每次新运行前通过 `IWorkflowRunPreparationService` 自动释放上一轮图像/工具结果；显示 Hub 仅保留每个 SourceKey 的最新帧，并提供 `ClearAsync/ClearAllAsync` 显式清理。
- [x] 完成 WorkflowInput 参数行的强类型树形绑定选择：前道必然完成节点输出按节点/成员分层，全局 Context 数据按分类/变量/成员分层，支持搜索、折叠、当前项定位和类型过滤；全局绑定以兼容字符串格式持久化并由运行时解析。
- [x] 完成 WinForms 人工界面优化准备：`DP.WorkFlow.UI.WinForms` 中全部 Form/UserControl/自绘画布，以及 `ScriptEngine.WinForms` 的 Roslyn 编辑器和 `DP.WorkFlow.Vision.UI.WinForms` 的 ROI 编辑器，均已拆分为标准 `.cs + .Designer.cs` partial 结构；补充无参设计器构造、显式 SubType/DependentUpon 和关键布局/扩展点中文注释。运行时动态参数、节点绘制和模型数据仍保留在业务 `.cs`。
- [x] 修复 CPU 密集节点阻塞桌面 UI：`WorkflowRuntimeHost.RunAsync` 现在是明确的后台执行边界，统一将运行准备和引擎执行调度到线程池，避免 PaddleOCR/ONNX 等返回同步完成 `ValueTask` 的处理器在首次未完成 await 前占用 WinForms/WPF 调用线程；增加同步 CPU Handler 非阻塞回归测试。
- [x] 删除现阶段全部自定义主题切换代码：移除 `WorkflowVisualTheme`、应用主题服务、主题感知接口、Studio 主题属性、Renderer 主题参数及相关测试；仅保留当前固定配色，后续由整体软件重新建立统一风格系统。
- [x] WinForms 画布新增自动概览窗口：任一节点超出当前可视区域时在右上角显示节点位置、连接/Waypoint 关系和当前视口框；点击或拖动概览可将主画布定位到对应区域，全部节点回到视口后自动隐藏。
- [x] 新增受限 `plugin.json` 加载器及 Runtime/Vision/WinForms/WPF Module 分组；重复 PluginId、ExtensionId、RendererKey 和越界程序集路径在启动阶段失败。
- [x] 视觉工具节点使用 `VisionBackendSelection`，支持部署 Profile、明确 Provider 和确定性自动选择；同一 OCR 文档可以通过配置切换 HALCON/PaddleOCR。
- [x] OCR Provider 输出统一 `VisionOcrFacts`，`Vision.Ocr` 标准节点输出不再暴露动态字典。
- [x] 节点详情页聚合改为 PageProvider Catalog；Property、SubWorkflow、Script、Image 均作为内置提供器注册，插件通过 PageId/Priority 替换槽位并由 RendererKey 精确匹配平台 Renderer。
- [x] 新增 `VisionWinFormsStudioExtension` / `VisionWpfStudioExtension`，页面提供器和 Renderer 成对注册；Studio 支持从插件目录自动装载平台 Module。
- [x] PaddleOCR 工作流节点迁入 `DP.WorkFlow.Nodes.Vision.PaddleOcr` 集成 Module，消除 `MachineVision.PaddleOcr` 对 Workflow 的反向依赖。
- [x] `VisionRuntime` 在运行准备阶段验证并冻结 Provider/Profile/Importer；根计划及子计划需求一次解析为运行专用不可变 `VisionOperationBindingSet`，节点执行不再解析 Provider。
- [x] Blob、Fixture、找线、找圆、Geometry Measure、Color 补齐 Provider 无关强类型 Options/Facts；语义专用节点拒绝动态参数 JSON。
- [x] 插件 Manifest 增加版本、依赖完整性和无环校验，Module 按依赖拓扑顺序装载。
- [x] 原生视口契约下移到 `MachineVision.UI.WinForms/Wpf`，HALCON 桌面 Adapter 不再引用 Workflow Studio。
- [x] 删除弱类型 `WorkflowCustomEditorPageModel`；自定义页必须提供强类型 Model、RendererKey，桌面 Renderer 必须声明 ModelType。
- [x] 增加关键集成验证：真实 Vision Runtime/Studio 插件程序集通过 Manifest 装载、视觉后端配置持久化往返、运行准备冻结、Provider 依赖方向和桌面视口所有权。验证发现并修复 `IWorkflowVisionFactsNode.FactsType` 被 JSON 当作配置序列化的问题，现改为显式能力接口实现。
- [x] 增加生产 `HalconImageImporter` 和 `OpenCvImageImporter/OpenCvVisionImage`；均复制 Gray8/BGR/BGRA 像素、处理 Stride 且不转移源图像所有权，HALCON 环境自动注册 Importer。
- [x] 普通视觉工具统一复用图像/ROI 页面，并支持输入、结果或命名 `VisionProcessFrame` 过程图选择；Provider 绑定保持运行状态，不进入编辑事务。
- [x] `DP.WorkFlow.UI.Shared` 移除 Standard/Composite 项目引用；Block 映射依赖结构能力接口，脚本 Studio 使用 `IWorkflowScriptHostContext` seam。
- [x] RuntimeHost、Navigator 和 JSON Store 删除 Canvas 兼容入口；Compiler、图校验和绑定分析只接受 `WorkflowDocument`/Graph，Canvas 编译重载已删除。
- [x] 新增独立 `IWorkflowPublicDataStore`、成功后暂存发布、公共数据绑定和设计期 `WorkflowPublicDataCatalog`；流程局部变量、节点输出和公共数据不再混用。
- [x] 全量验证捕获并修复 HALCON WinForms 视口 Resize 竞态：单次 BeginInvoke 可能早于 HSmart 内部维护完成，现使用 50ms UI Timer 合并 Resize/Wheel/Pan 重绘；原失败用例连续 10 次通过。
- [x] 修复 Document/Canvas 所有权反转：`WorkflowDocument` 独占内部状态并成为唯一创建根；Canvas 无公开构造、无 Document 反向引用，Compiler、GraphValidator、BindingAnalyzer 和 DesignerSession 均删除 Canvas 入口。
- [x] Standard、Composite、Motion、Process 与 Vision 节点包统一提供 Runtime Plugin Module；Standard 新增聚合 Handler 注册，示例宿主不再逐个列出处理器。
- [x] NodeCatalog、HandlerCatalog 和 RuntimePluginCatalog 支持验证后冻结；冻结时验证节点工厂的 ModelType/NodeType，并确保每种节点唯一匹配 Handler。
- [x] Handler 注册支持固定和配置相关的强类型运行能力要求；RuntimeBinder 将要求冻结到绑定计划，RuntimeHost 在准备 Module 和节点执行前递归预检根计划及子计划。
- [x] 节点执行上下文新增 `GetRequiredCapability<T>()`；Standard/Motion/Process/Vision 的必需能力访问已收敛，可选视觉显示、资源跟踪和受信任脚本仍保留兼容服务入口。
- [x] WinForms/WPF 自定义详情页缺少 Renderer 或 ModelType 不匹配时在创建控件前失败；Block 映射 UI 改为只依赖 `IWorkflowBlockMappingNode`，平台 Studio 移除 Composite 节点程序集引用。

### 下一步

1. 将 `WorkflowDesignerSession`、JSON Store 和文档快照器的内部写入从 Canvas 适配投影迁移到 Document Graph/Layout 编辑接口，最终删除 Canvas 类型；
2. 为 Graph/Layout 建立稳定只读集合并消除访问时数组分配；
3. 为控制连接增加稳定身份并替换 `ConnectionIndex` 布局关联；
4. 为 OpenCV 增加 Blob/Measure/Color 算法 Provider，使用本轮强类型契约验证 HALCON/OpenCV 互换；
5. 定义局部变量、公共数据和节点标准输出的统一提交协调器及持久化公共数据 Adapter；
6. 继续验证 DPI、键盘导航、无障碍、复杂回环和大图路由。

### 当前限制

- 已支持顺序、循环、结构化并行、Pause/Resume、Block、Token/Scope 输出和设计期绑定分析；
- 异常恢复节点、专用告警子画布、动态节点端口及 WarningResolution 恢复调度已实现；宿主需注册 WorkflowWarningHandlerCoordinator 才会自动切入告警子流程；
- Signal、IO、Axis、气动、CodeReader、产品流和 WaferRobot 已完成首轮保真重写，仍需补齐逐节点旧 JSON 版本迁移与设备模拟故障测试；
- Dictionary/List/复杂对象属性编辑仍待实现；
- 普通单出口端口仍按声明的连接基数约束，结构化并行应使用 `ParallelAll.Branch`；
- DPI、键盘导航、无障碍和复杂大图路由仍需继续验证。
