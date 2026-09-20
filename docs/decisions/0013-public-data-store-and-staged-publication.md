# ADR-0013：公共数据仓与流程局部变量分离

- 状态：Accepted
- 日期：2026-01-01

## 决策

`WorkflowContext` 同时组合但不混同三类数据：流程局部变量、Token/Scope 节点输出以及 `IWorkflowPublicDataStore`。`SetVariable` 只写当前工作流作用域的局部变量；节点只有显式调用 `PublishData` 才会向公共数据仓暂存写入。局部变量和公共数据变更都只在节点成功后提交，失败节点不会公开暂存值。

子流程创建独立局部变量和节点输出状态，但共享父流程的公共数据仓。已有文档中的 `$global:` 绑定前缀继续读取，以保持持久化键稳定；其正式语义改名为“公共数据”，新代码使用 `FromPublicData`、`IsPublicData` 和 `PublicDataKey`。

## 原因

原模型将 `WorkflowContext.Variables` 同时描述为局部交换区和全局公开数据，导致 Block 隔离、数据公开策略和生命周期无法独立演化。直接把所有节点写入放进公共仓还会绕过节点输出的 Token/Scope 可见性规则，并使失败节点产生不可见副作用。

独立仓接口允许宿主选择运行级、设备级或持久化 Adapter，同时不改变节点输出模型和现有变量键。

## 结果

公共数据发布成为显式动作；普通 `SetVariable` 不再具有跨子流程公开语义。公共数据可以通过 `WorkflowBindingKey.FromPublicData` 绑定，父子流程共享同一仓。跨局部变量、公共数据和节点输出的单锁事务不在本决策中承诺；当前保证是每类暂存变更仅在节点成功后提交。
