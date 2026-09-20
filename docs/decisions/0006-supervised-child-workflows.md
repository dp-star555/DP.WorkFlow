# ADR-0006：编译期子流程定义与父引擎监管

- 状态：Accepted
- 日期：2026-01-01

## 决策

Block 等复合节点通过 `IWorkflowSubDocumentNode` 持有 UI 无关子画布。父流程编译时递归编译全部子画布并检测循环引用，运行时不得临时重新解释画布。

子流程使用复制父变量、隔离 NodeOutputs 的 `WorkflowContext`。父引擎负责创建子引擎，并向活动子引擎传播取消、Pause、Resume 和 ExternalHold。父快照通过 `ChildWorkflows` 保留当前或最近的完整子快照。

## 原因

节点自行创建和静态缓存子引擎会绕过宿主生命周期，导致暂停、取消和 UI 快照不一致；共享父 Context 又会清空或覆盖父运行输出。编译期定义和受监管执行可以消除这些问题。

## 结果

新增独立的 `DP.WorkFlow.Nodes.Composite` 程序集。Block 输出为子运行摘要；子变量默认不回写父作用域，需要显式输出映射功能时再扩展契约。
