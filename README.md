# DP.WorkFlow

DP.WorkFlow 是从旧 `WorkFlow.Rebuild` 独立出来的 .NET 8 重构主线。程序集、项目路径和公共命名空间统一使用 `DP.WorkFlow`；旧目录只作为对照基线，不再承接新设计。

- [渐进重构路线](docs/refactoring-roadmap.md)
- [整体使用指南](docs/USAGE_GUIDE.md)
- [UI 使用说明](docs/ui/overview.md)
- [当前进度](docs/progress.md)
- [Node平台架构问题与优化清单](docs/node-platform-review-backlog.md)
- [节点异常、处理与恢复专项评审](docs/node-fault-recovery-review.md)
- [节点数据传递与模型关系](docs/nodes/node-data-flow-model.md)
- [大型节点系统：整体架构审阅与长期演进路线](docs/architecture-review-and-roadmap.md)
- [联合恢复与处置步骤继续：当前SDK、契约和限制](docs/nodes/joint-recovery.md)
- [真实人工交互与双平台恢复演示](docs/nodes/operator-interaction.md)（`--recovery-demo`）
- [异常恢复V1：工程师预设处理、原操作继续与命名入口](docs/nodes/fault-recovery-v1.md)
- [异常恢复场景与结构设计草案](docs/node-recovery-scenarios-design.md)
- [节点实现盘点与优先级](docs/nodes/implementation-priority.md)
- [源码目录与依赖规则](docs/project-structure.md)
- [节点工程分类与依赖边界](docs/nodes/project-classification.md)
- [插件架构](docs/plugin-architecture.md)
- [节点重写保真审计](docs/nodes/fidelity-audit.md)
- [独立 DP.Vision 实施记录](docs/dp-vision-integration-plan.md)
- [多厂商图像采集 Provider 实施与验收基线](docs/vision-acquisition-providers.md)（阶段A–E 已实施：HALCON 与 Basler 两个真实 Provider；阶段F 受外部依赖阻塞）
- [新版视觉节点、ROI 与双宿主示例](docs/nodes/new-vision-file-pipeline.md)
- [模板定位坐标系、ROI随动与双坐标结果](docs/nodes/vision-coordinate-systems.md)

WinForms/WPF 示例使用独立 DP.Vision 的18种节点及原生图像页，启动即有可运行的文件→预处理→Region→形态学→Blob→筛选→掩码颜色流程。构建需保留同级 `../DP.Vision/` 源码。旧视觉节点、MachineVision 工程、兼容 Adapter、旧页面及 Paddle 工作流/工具已删除；不会静默转换旧文档。相机采集由独立 `DP.Vision.Halcon` 以插件形式提供：示例只扫描 `plugins/` 目录里的 `plugin.json`，编译期不引用任何 HALCON 类型，工作流文档只保存逻辑SourceId。详见[清理决定](docs/decisions/0014-vision-clean-break.md)和[已落地算子及用法](docs/nodes/vision-operators.md)。

## 构建

```powershell
dotnet restore DP.WorkFlow.sln
dotnet build DP.WorkFlow.sln --no-restore
powershell -ExecutionPolicy Bypass -File tools/Test-DPWorkFlow.ps1 -Configuration Debug
```

`Test-DPWorkFlow.ps1` 默认按 `-Suite Auto` 运行：现代控件库（`ModernUI.WinForms`、`ModernUI.Localization`、
`ScriptEngine.WinForms/Wpf`）源码没改动时跳过控件库用例，跑 481 例业务与集成用例；改过控件库时自动带上
全部 817 例。需要强制时用 `-Suite All`（全跑，817 例）、`-Suite Core`（永不跑控件库）、
`-Suite UiControls`（只跑控件库，改控件时的快速回路，336 例）。分层依据见
[测试套件分层](docs/project-structure.md#测试套件分层)。

## 当前能力

- 模板定位输出稳定定义/模板签名与本帧正反矩阵；全部面积ROI节点（含线圆测量及两类模板定位）、卡尺和鲁棒直线支持显式坐标绑定；统一范围能力与解析入口支持外部节点扩展，父子定位不重复补偿；两平台页面保存局部ROI，拒绝混帧和换模板误用；

- UI 无关节点、端口和 Handler 契约；
- `WorkflowDocument` 是唯一创建根和状态所有者，正式分离语义图、根/子入口和设计器布局；Canvas 无公开构造和 Document 反向引用，仅作为桌面编辑投影；
- Schema 4 持久化显式 `EntryNodeId`、子文档和严格节点版本；
- `WorkflowCompiler → WorkflowExecutionPlan → WorkflowRuntimeBinder` 编译与运行时绑定链路；
- 编译期统一 `WorkflowGraphIndex`，集中提供出入边、可达性、支配与后支配关系；
- 节点配置深快照，画布或 Handler 修改不会污染已编译执行计划；
- 并行分支在显式汇聚前必须互斥，提前共享节点会产生 `WF022` 阻塞诊断；
- Loop 使用执行令牌级 LoopFrame，重复进入和并行分支不会共用节点全局计数；
- Parallel/Join 使用结构能力声明，并行恢复在保留 Scope 身份前明确拒绝；
- Handler 在 Configure/绑定阶段递归预解析，缺失和多重匹配提前失败；
- `WorkflowRunState` 独立保存节点输出、并行可见性、节点故障和监视快照；
- 节点结果使用 Continue/CompletePath/Fault 互斥形态；
- 失败执行不提交节点标准输出或暂存变量；
- Retry 需要 Idempotent/ResumeAware 声明，Jump 需要恢复目标能力；
- `plugin.json` 分组装载 Runtime、Studio、WinForms/WPF 插件 Module；
- 节点插件目录、稳定 Key 和冲突诊断；
- `WorkflowImageRuntimePluginModule` 注册18种强类型视觉节点；宿主固定装配采集、Blob、颜色、测量和定位能力，运行前递归预检；
- 统一 `IImageSource / ImageFrame`、有界帧租约、同帧检测证据和原生双平台 ROI 编辑；HALCON SDK 仅在独立厂商边界复制中立像素；
- 节点详情页通过 PageProvider、PageId/Priority 和 RendererKey 自动匹配，Block/Script/Image 不再硬编码在聚合模型；
- `WorkflowInput<T>`、`WorkflowBindingKey` 及运行时成员路径解析；
- 绑定类型转换、路径缓存和结构化错误；
- 流程局部变量、独立公共数据仓及带 Run/Token/Scope/Execution 身份的节点输出；
- 绑定的祖先可见、兄弟隔离和汇聚后开放规则；
- 设计期支配分析、并行汇聚可见性、成员路径与类型候选；
- Start、Action、Decision、ValueCompare、StringCompare、WaitFunction、Delay、Loop、Jump、ParallelAll、WaitAllInputsCompleted、End 标准节点；
- 独立 Composite 程序集中的 Block 子流程节点；
- Success、True、False、Timeout、Loop、Completed、Branch 显式端口；
- 节点端口声明、连接基数和编译期规则检查；
- 循环执行安全上限；
- 取消、Pause/Resume、可叠加 ExternalHold；
- 不可变运行快照、节点耗时、多活动节点、并行作用域和有界 Trace 批次；
- 结构化并行、嵌套并行、汇聚及兄弟分支故障取消；
- RuntimeHost 配置、预运行 Pause/Hold、运行复用、取消、Reset 和快照转发；
- UI.Shared 设计会话、统一节点配置事务、Undo/Redo、视口、工具箱和运行覆盖层；
- 打开、查看和子文档导航不修改文档；布局规范化是显式可撤销命令；
- 动态端口失效连接保留为 Detached 诊断，不再静默删除；
- 原生 WinForms GDI+ 与 WPF DrawingContext 双画布控件；
- 分类树工具箱、拖放创建、正交连线、端口标签和四边端口布局；
- 双属性面板、WorkflowInput<T> 候选编辑、Studio 工作台和 Block 面包屑导航；
- 双诊断面板、RuntimeHost 工具栏、连线选择删除及 Waypoint 编辑；
- 多选框选、批量拖动、对齐分布、自动布局和并行运行监视；
- 新建/打开/保存工作区、脏状态、最近文件及 Trace 实时监视；
- WinForms/WPF Block 输入输出映射集合编辑器；
- 可搜索的分层强类型绑定树及 Trace 筛选/CSV 导出；
- 编译期子画布、隔离子 Context、父子 Pause/Hold/取消传播及嵌套快照；
- Block 子文档默认变量隔离，仅通过显式输入/输出映射交换数据；
- InvariantCulture 数值比较、Ordinal 字符串比较及安全 Like；
- Schema 4 JSON、旧 Schema 1/2/3 显式迁移和未知节点保留；
- 覆盖 Core、Runtime、节点、持久化、视觉与双 UI 的 xUnit 回归测试。

后续改造顺序和已冻结范围见 [渐进重构路线](docs/refactoring-roadmap.md)。
