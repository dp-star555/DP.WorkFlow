# 旧版到新版类型迁移表

> 状态值：Pending / InProgress / Compatible / Completed。

| 旧类型 | 新项目/类型 | 状态 | 注释迁移 | 行为测试 | 备注 |
|---|---|---|---|---|---|
| `WorkflowNodeAttribute` | `DP.WorkFlow.Abstractions/WorkflowNodeAttribute` | InProgress | 完成 | 待补充 | 保留名称，NodeType Key 稳定化 |
| `IWorkflowNodeModel` | `DP.WorkFlow.Abstractions/IWorkflowNodeModel` | InProgress | 完成 | 完成 | 执行职责移至 Handler |
| `WorkflowNodeModel` | `DP.WorkFlow.Abstractions/WorkflowNodeModel` | InProgress | 完成 | 完成 | 不再直接实现 ExecuteAsync |
| `WorkflowCanvasModel` | `DP.WorkFlow.Core/WorkflowDocument` | Completed | 完成 | 完成 | Graph/Layout/Entry 分离，Canvas 仅为桌面投影 |
| `WorkflowConnectionModel` | `DP.WorkFlow.Core/WorkflowConnectionModel` | InProgress | 完成 | 完成 | Label 改为显式 FromPort/ToPort |
| `NodeExecutionResult` | `DP.WorkFlow.Abstractions/NodeExecutionResult` | Completed | 完成 | 完成 | Continue/CompletePath/Fault 互斥，不返回目标节点 ID |
| `WorkflowContext` | `DP.WorkFlow.Runtime/WorkflowContext` | InProgress | 完成 | 完成 | null 不再表示删除 |
| `WorkflowExecutionPlan` | `DP.WorkFlow.Core/WorkflowExecutionPlan` | Completed | 完成 | 完成 | 配置、拓扑和子计划与文档隔离 |
| `WorkflowEngine` | `DP.WorkFlow.Runtime/WorkflowEngine` | Completed | 完成 | 完成 | Token、LoopFrame、并行、子文档和恢复闭环 |
| `StartNodeModel` | `DP.WorkFlow.Nodes.Standard/StartNodeModel` | InProgress | 完成 | 完成 | Handler 分离 |
| `ActionNodeModel` | `DP.WorkFlow.Nodes.Standard/ActionNodeModel` | InProgress | 完成 | 完成 | 使用动作注册表 |
| `EndNodeModel` | `DP.WorkFlow.Nodes.Standard/EndNodeModel` | InProgress | 完成 | 完成 | Handler 分离 |
| `WorkflowBindingKey` | `DP.WorkFlow.Abstractions/WorkflowBindingKey` | InProgress | 完成 | 完成 | 保留 `nodeId|memberPath` 文本兼容 |
| Source/Value/BindingKey 三件套 | `DP.WorkFlow.Abstractions/WorkflowInput<T>` | InProgress | 完成 | 完成 | 统一固定值与绑定值配置 |
| `WorkflowDocumentJsonStore` | `DP.WorkFlow.Persistence.Json/WorkflowDocumentJsonStore` | Completed | 完成 | 完成 | Schema 4，兼容迁移 Schema 1/2/3 |
| 未安装的插件节点 | `UnknownWorkflowNodeModel` | InProgress | 完成 | 完成 | 当前格式下无损保留原始 Config |
| `WorkflowBindingResolver` | `DP.WorkFlow.Runtime/WorkflowBindingResolver` | InProgress | 完成 | 完成 | 结构化输出、路径缓存和统一转换 |
| `DecisionNodeModel` | `DP.WorkFlow.Nodes.Standard/DecisionNodeModel` | InProgress | 完成 | 完成 | 当前支持 Function 与强类型 Binding |
| `WaitFunctionNodeModel` | `DP.WorkFlow.Nodes.Standard/WaitFunctionNodeModel` | InProgress | 完成 | 完成 | 显式 Success/Timeout 端口 |
| 3650 天无限等待补丁 | `Timeout.InfiniteTimeSpan` | Completed | 完成 | 完成 | 删除伪无限超时 |
| 连接 Label/隐式分支 | `WorkflowPortDescriptor` | InProgress | 完成 | 完成 | 稳定端口和连接基数 |
| `DelayNodeModel` | `DP.WorkFlow.Nodes.Standard/DelayNodeModel` | InProgress | 完成 | 完成 | 可取消 Task.Delay |
| `LoopNodeModel` | `DP.WorkFlow.Nodes.Standard/LoopNodeModel` | InProgress | 完成 | 完成 | Loop/Completed 端口及执行上限 |
| `JumpNodeModel.TargetNodeId` | `JumpNodeModel.Success` 连接 | InProgress | 完成 | 完成 | 目标从节点配置迁移为图连接 |
| `WorkflowRuntimeSnapshot` | `DP.WorkFlow.Runtime/WorkflowRuntimeSnapshot` | InProgress | 完成 | 完成 | RunId、Sequence、节点耗时及只读数据 |
| `Pause/Resume` | `AsyncManualResetEvent` 调度门 | InProgress | 完成 | 完成 | 不再用 20ms 轮询等待 |
| `ExternalHoldReasons` | 可叠加原因集合 | InProgress | 完成 | 完成 | 全部原因解除后才恢复 |
| `WorkflowNodeTraceEntry` | `WorkflowTraceEntry/WorkflowTraceBatch` | InProgress | 完成 | 完成 | 有界缓存和不可变批次 |
| `ParallelAllNodeModel` | `DP.WorkFlow.Nodes.Standard/ParallelAllNodeModel` | InProgress | 完成 | 完成 | Branch 多连接与结构化作用域 |
| `WaitAllInputsCompletedNodeModel` | 同名新版节点 | InProgress | 完成 | 完成 | 汇聚后只执行一次 |
| `WorkflowExecutionPlan.NodeParallelMemberships` | `WorkflowParallelScopePlan` | Completed | 完成 | 完成 | 共同可达、后支配和分支互斥校验 |
| 单 CurrentNodeId | `ActiveNodeIds/ActiveTokens` | InProgress | 完成 | 完成 | 快照可表达并行活动 Token |
| `{NodeId}.Result` 单值 | `WorkflowNodeOutput` 历史 | InProgress | 完成 | 完成 | Run/Token/Scope/Execution 身份 |
| 并行共享结果覆盖 | Token/Scope 可见性规则 | InProgress | 完成 | 完成 | 兄弟隔离、汇聚后开放 |
| `IWorkflowRuntimeHost` | 同名新版接口与宿主 | InProgress | 完成 | 完成 | Configure/Run/Cancel/Reset/快照转发 |
| `BlockNodeModel` | `DP.WorkFlow.Nodes.Composite/BlockNodeModel` | InProgress | 完成 | 完成 | 模型与受监管 Handler 分离 |
| `ISubCanvasNode` | `IWorkflowSubDocumentNode` | Completed | 完成 | 完成 | 具有独立 Entry/Graph/Layout 的子文档契约 |
| 节点静态子引擎缓存 | 父 `WorkflowEngine.ChildWorkflows` | InProgress | 完成 | 完成 | 生命周期和快照由父引擎监管 |
| Block 共享父 Context | `CreateChildScope` | Completed | 完成 | 完成 | 默认不继承父变量，仅共享宿主能力和信号域 |
| Block 内嵌 JSON | Schema 4 `SubDocument` | Completed | 完成 | 完成 | 独立入口、递归往返和循环引用保护 |
| Block 隐式共享 Context | `InputMappings/OutputMappings` | InProgress | 完成 | 完成 | 父子作用域显式数据契约 |
| `ValueCompareNodeModel` | 同名新版模型与 Handler | InProgress | 完成 | 完成 | InvariantCulture 和一致容差边界 |
| `StringCompareNodeModel` | 同名新版模型与 Handler | InProgress | 完成 | 完成 | Ordinal 比较及安全 Like |
| `CompareNodeResult` | 不可变 record | InProgress | 完成 | 完成 | 统一比较输出结构 |
| 运行期绑定候选 | `WorkflowBindingAnalyzer` | InProgress | 完成 | 完成 | 设计期支配、路径和类型分析 |
| `IWorkflowBindingTypeProvider` | `WorkflowNodeDescriptor.OutputType` | InProgress | 完成 | 完成 | 插件目录声明输出 CLR 类型 |
| UI 反射绑定树 | `WorkflowBindingCandidate` | InProgress | 完成 | 完成 | UI 无关候选模型 |
| 无绑定诊断代码 | WFB001-WFB005 | InProgress | 完成 | 完成 | 来源、时序、未知类型、路径、兼容性 |
| `Ctr_WorkflowDesigner` | `WorkflowDesignerSession` + WinForms/WPF 控件 | InProgress | 完成 | 基础完成 | 共享行为、双原生渲染 |
| `Ctr_WorkflowToolbox` | 双 UI `WorkflowToolboxControl` | InProgress | 完成 | 基础完成 | 共享 ToolboxItem |
| UI 内直接改 Canvas | Session 编辑命令 | InProgress | 完成 | 完成 | Undo/Redo 与端口约束统一 |
| 单一 WinForms 画布 | WinForms GDI+ / WPF DrawingContext | InProgress | 完成 | 基础完成 | 各自原生输入和绘制 |
| `Frm_WorkflowNodeEditorDialog` | 双 `WorkflowPropertyPanel` | InProgress | 完成 | 基础完成 | 共享 Inspector、原生编辑器 |
| `WorkflowBindingTreeEditor` | WorkflowInput 候选编辑 | InProgress | 完成 | 基础完成 | 使用 BindingAnalyzer 候选 |
| Block 子窗口/静态引擎查询 | `WorkflowDesignerNavigator` | InProgress | 完成 | 完成 | 面包屑和子快照映射 |
| 设计器散装控件 | 双 `WorkflowStudioControl` | InProgress | 完成 | 基础完成 | 工具箱、画布、属性和命令组合 |
