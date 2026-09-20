# Node平台架构问题与优化工作清单

状态：问题记录与优先级建议，**不是已实现清单**。本轮仅审阅代码和更新文档，不改运行行为。范围覆盖整个Node平台，不限于Vision。

最新整体取证和演进优先级见[架构审阅与长期路线](architecture-review-and-roadmap.md)，其中三项问题已用最小探针复现。下方NP编号继续用于跟踪；原执行排序是早期恢复专项的安排，当前排序以新报告阶段A–D为准。

## 保留的主干

Document → Compiler → ExecutionPlan → RuntimeBinder → BoundPlan → 能力预检/准备 → Engine → RunState。

NodeModel/Handler/Catalog与PageProvider/Renderer保持分离；Standard、Composite、Motion、Process、Vision作为领域分支。避免超级Node基类、全局可变运行上下文以及为拆分而拆分程序集。

## 优先级与工作包

| ID | 优先级 | 问题/方向 | 当前依据与注意点 | 验收目标 |
|---|---|---|---|---|
| NP-01 | P0 | 冻结与不可变计划 | RuntimePluginCatalog在完整Handler验证前冻结；BoundPlan能力列表底层为数组 | 冻结失败状态明确，失败目录不能当成功目录复用；外部不能修改已冻结计划 |
| NP-02 | P0 | 配置类型与快照 | ConfigurationSnapshotter反射复制对象图，并共享ValueType/Delegate；包含引用的struct和闭包等需专项约束 | 明确允许/拒绝的配置类型、深度预算、独立快照与恢复契约 |
| NP-03 | P0 | 插件注册失败与能力使用 | Module.Register可能部分写入；能力预检主要检查服务非空，执行时再次取服务 | 失败注册可回退或明确废弃；声明与使用一致；本次运行实例稳定 |
| NP-04 | P0 | 异常、处理、恢复 | 已有显式恢复请求、Retry/Jump/Stop、告警子流程和安全声明；不能等同现场安全恢复 | 优先专项审计，见[异常恢复评审](node-fault-recovery-review.md) |
| NP-05 | P0 | 统一运行控制 | IWorkflowRuntimeHost未包含具体宿主已有的StopAsync；准备/运行/恢复/关闭需统一语义 | 等待停止完成后释放资源；Pause/Hold/Cancel/Stop职责一致；不把软件停止当安全急停 |
| NP-06 | P1 | 设备资源协调 | DI能力存在不代表设备在线、可并发或未被占用 | 设备标识、占用规则、完成确认、超时未知状态和资源释放可验收 |
| NP-07 | P1 | 字段级编辑扩展 | 专属页面可扩展，一般设备/动作/嵌套Options仍需更好的共享编辑机制 | Field Provider/Model与两平台Renderer；隔离提交、Undo、类型校验一致 |
| NP-08 | P1 | Document修改入口收敛 | Document为状态所有者，但编辑/快照仍有CanvasProjection集合路径 | 文档命令、Revision、原子变更通知、Graph/Layout分类；再逐步删除投影写适配 |
| NP-09 | P1 | 数据契约、身份与生命周期 | 已有Token/Scope可见性和显式公共数据；资源型输出与跨层身份需要系统化 | 完整执行来源、资源所有权、取消后释放、恢复后的数据有效性；不退回全局latest读取 |
| NP-10 | P1 | Composite模块化 | 已有子流程隔离和输入输出映射 | 对外契约、参数化复用、调用方诊断、子流程调试与故障归属明确 |
| NP-11 | P1 | 节点制作SDK与验收套件 | 能力声明和接口签名不能保证实现真的遵约 | 模板、注册、持久化、输出提交、取消/恢复、资源及双平台扩展有统一测试套件 |
| NP-12 | P1 | 结果/监控扩展 | Vision预览仍有具体结果类型和具体scope判断；可作为平台扩展问题样本 | 事实身份、预览发布、图元转换可扩展；监控订阅失败可诊断而不影响执行 |
| NP-13 | P2 | 执行诊断与回放 | 已有快照/Trace，不等于完整事故记录与回放 | 耗时、等待原因、输入来源、故障/恢复因果链；回放不重新驱动真实设备 |
| NP-14 | P2 | 无界面宿主与性能治理 | 执行核心独立于UI，可继续完善部署和基准 | 桌面/后台/测试一致；以阶段耗时、内存和并发基准驱动优化 |

## 各领域分支的约束

- **Standard**：控制、数据转换、等待、信号、队列、脚本；明确背压、超时、取消、脚本权限，不收纳无归属业务。
- **Composite**：隐藏子流程实现、暴露稳定输入输出，不以复制节点集合冒充可复用流程模块。
- **Motion**：资源占用和动作确认优先于动作节点数量；重复执行必须有设备语义保证。
- **Process**：按产品流转、工位协调、设备协同、异常处置划分职责，避免业务大杂烩；不吞并Runtime调度。
- **Vision**：复用能力声明、编辑隔离、资源所有权、事实来源等模式，不把ROI/FrameId强塞进所有Node。

## 执行顺序

1. 当前聚焦NP-04：异常分类、恢复权限、外部副作用、恢复执行身份与数据有效性。
2. 同步关注与恢复直接相关的NP-05/NP-09；其他P0单独安排，不在恢复专项中顺手大改。
3. 再推进节点制作SDK、字段编辑、Composite与设备资源协调。
4. 最后扩展诊断回放、无界面宿主和有基准的性能优化。

## 代码取证入口

- `Kernel/DP.WorkFlow.Abstractions/Plugins/WorkflowRuntimePluginCatalog.cs`
- `Kernel/DP.WorkFlow.Core/Compilation/WorkflowNodeConfigurationSnapshotter.cs`
- `Kernel/DP.WorkFlow.Runtime/Preparation/WorkflowRuntimeBinder.cs`
- `Kernel/DP.WorkFlow.Runtime/Preparation/WorkflowRuntimeCapabilityValidator.cs`
- `Kernel/DP.WorkFlow.Runtime/Hosting/IWorkflowRuntimeHost.cs`
- `Kernel/DP.WorkFlow.Runtime/Execution/WorkflowNodeExecutionContext.cs`

以上路径相对`src/Workflow/`。具体修复需新增确定性测试，不将源码审阅或历史全量绿灯等同于这些问题已解决。
