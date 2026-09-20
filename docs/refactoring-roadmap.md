# DP.WorkFlow 渐进重构路线

## 已落地结构

```text
WorkflowDocument
├── WorkflowGraph          节点、入口和控制连接
└── WorkflowLayout         坐标、尺寸、端口展示和折线路径
          ↓ WorkflowCompiler
WorkflowExecutionPlan      冻结配置、控制拓扑和结构化作用域
          ↓ WorkflowRuntimeBinder
WorkflowBoundExecutionPlan 唯一 Handler 绑定
          ↓ WorkflowEngine
WorkflowRunState           Token、Scope、输出、故障和快照
```

控制流只负责调度，数据绑定只负责取值；失败节点的标准输出和暂存变量均不提交。

## 已完成基线

- 新建独立 `DP.WorkFlow` 目录，不修改旧 `WorkFlow.Rebuild`。
- `WorkFlow.*` 项目、程序集和路径统一重命名为 `DP.WorkFlow.*`。
- 公共命名空间由 `Workflow*` 统一迁移为 `DP.WorkFlow*`。
- 引入内部深模块 `WorkflowGraphIndex`，编译和绑定分析共享同一份控制图索引。
- 并行作用域由统一分析模块产生；分支在 Join 前共享节点时以 `WF022` 阻止编译。
- `WorkflowExecutionPlan` 深复制节点配置，并为每次执行返回独立配置，画布和 Handler 均不能污染编译快照。
- 节点失败时不再向标准输出历史提交空值。
- Loop 迭代状态由执行令牌的 LoopFrame 管理，完成后再次进入会创建新帧。
- Parallel/Join 通过结构能力声明，Core 和 Runtime 不再比较 NodeType 字符串。
- 并行分支恢复在能够保留原 Token/Scope 身份前明确拒绝 Retry 和 Jump。
- WorkflowRuntimeBinder 递归预解析 Handler，缺失或冲突不会拖到节点执行时才暴露。
- Schema 4 正式持久化根和子文档 `EntryNodeId`，节点版本要求与当前注册版本严格相等。
- `WorkflowDocument` 已成为持久化、编译和 RuntimeHost 的正式入口；Canvas 仅保留为桌面设计器过渡投影。
- 属性检查器、节点编辑器和布局修复均通过可撤销事务提交；构造、打开和导航不再自动修复文档。
- 动态端口失效连接保留为 `Detached` 并阻止编译，不再静默删除。
- `WorkflowRunState` 拥有节点输出、并行可见性、节点故障和最新监视快照。
- 节点变量写入先暂存，只有节点成功完成后才与标准输出一起提交。
- Block 子文档默认不继承父变量，只通过显式输入输出映射交换数据。
- Retry 和 Jump 分别要求显式重试安全声明和恢复目标能力。

## 下一批改造

### 1. 宿主依赖预检

- 在 Handler 已绑定的基础上，继续预检函数、设备、视觉 Provider 和模型资源。
- 将缺失依赖汇总为配置诊断，不等到节点运行时失败。

### 2. 数据契约深化

- 将视觉工具动态 `Outputs` 逐类替换为 OCR、Blob、Fixture、Measure 等强类型事实。
- 设计节点输出公开策略和公共数据仓生命周期。
- 收敛仍依赖全局变量键的视觉图像传递。

### 3. Canvas 投影退役

- WinForms/WPF 画布改为直接消费 Document 编辑接口。
- 删除 `WorkflowCanvasModel` 编译、持久化和 RuntimeHost 兼容重载。
- 完成后移除过渡画布投影。
