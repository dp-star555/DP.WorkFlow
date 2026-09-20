# ADR-0011：文档、执行计划与运行状态分层

- 状态：Accepted
- 日期：2026-09-02

`WorkflowDocument` 是唯一创建根、状态所有者以及可编辑和持久化的唯一事实来源，并显式分离语义图与设计器布局；`WorkflowCompiler` 只把文档语义编译为隔离的 `WorkflowExecutionPlan`，随后由 `WorkflowRuntimeBinder` 预解析全部节点处理器；每次执行的数据输出、并行可见性、故障和监视快照归属于独立 `WorkflowRunState`。选择该结构是为了保证查看和布局操作不改变执行语义、运行不受后续编辑影响，并让失败执行无法发布正常输出或暂存变量。

## Consequences

根入口成为 Schema 4 文档的必填语义；子流程使用独立子文档。设计器属性变化必须通过事务提交，失效连接保留为 `Detached` 诊断而不是删除。`WorkflowCanvasModel` 仅作为现有桌面画布控件的过渡投影，不再是编译、校验、绑定分析、持久化和运行宿主的正式入口。Canvas 没有公开构造函数，也不持有或反向创建 Document；它只访问 Document 创建并拥有的内部状态。
