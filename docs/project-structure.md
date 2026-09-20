# 源码目录与依赖规则

## 1. 目的

目录首先表达**领域所有权**，其次表达该领域内部的职责。项目数量不用于表现每一个内部 Module；只有出现真实依赖 seam、独立部署或独立复用需求时才新增程序集。

程序集名称和公共命名空间不跟随物理目录自动变化。目录整理、公共接口重命名和依赖拆分必须分成独立增量。

## 2. 顶层目录

```text
src/
├─ Workflow/                 工作流文档、编译、运行、节点扩展和 Studio
│  ├─ Kernel/                稳定契约、文档/编译、运行内核
│  ├─ Nodes/                 可独立注册的节点包
│  ├─ Persistence/           文档持久化 Adapter
│  └─ Studio/                框架无关 Studio 模型与 WinForms/WPF Adapter
└─ Platform/                 可脱离工作流复用的平台库
   ├─ Scripting/             脚本引擎及桌面编辑器
   ├─ Desktop/               ModernUI 控件与 Gallery
   └─ Localization/          本地化基础设施
```

测试镜像相同的领域分类：

```text
tests/
├─ Workflow/                包括新版视觉节点和Studio集成测试
└─ Platform/
```

可运行示例统一位于：

```text
samples/
├─ DP.WorkFlow.WinForms.Sample/
├─ ModernUI.WinForms.Sample/
├─ Recovery/                 双平台共用的纯软件恢复演示
└─ Legacy/                   独立构建宿主；WpfApptest已接入新版视觉，不在主Solution中
```

## 3. Workflow 内部分类

### Kernel

- `DP.WorkFlow.Abstractions`：节点、数据和执行扩展契约；不得引用其他业务项目。
- `DP.WorkFlow.Core`：可编辑文档和编译计划；不得引用 Runtime、Studio 或具体节点包。
- `DP.WorkFlow.Runtime`：运行准备、执行和一次运行状态；只依赖 Abstractions 与 Core。

### Nodes

- `Standard`：通用控制流、函数、转换、脚本和信号节点。
- `Composite`：Block 等受监管子文档节点。
- `Process`：产品流、机器人和恢复语义节点。
- `Motion`：轴、IO、气动和扫码设备节点。
- `Vision`：Workflow 与同级 `DP.Vision.Algorithms` 中立契约之间的18种强类型节点集成；不引用 SDK 或桌面。独立视觉源码/算法/相机实现/原生画布位于 `../DP.Vision/src`，算法与 SDK 测试位于 `../DP.Vision/tests`。

### Persistence

持久化项目是文档存储 Adapter。它可以依赖 Abstractions 与 Core，但不得依赖 Runtime 或桌面 UI。

### Studio

`DP.WorkFlow.UI.Shared` 保存框架无关的 Authoring、Editors、Diagnostics 和 Monitoring 模型。WinForms/WPF 项目只负责平台呈现和事件适配。

`DP.WorkFlow.Process.UI.WinForms` 与 `DP.WorkFlow.Process.UI.Wpf` 是独立人工交互Adapter，分别引用对应桌面框架与Process运行契约。共享的任务排队/身份/取消逻辑位于无桌面依赖的Process节点包；两个桌面工程不让Kernel或Nodes反向引用UI。公共命名空间使用 `DP.WorkFlow.OperatorUI.*`，避免与 `System.Diagnostics.Process` 名称冲突。

## 4. 内核项目内部目录

```text
DP.WorkFlow.Abstractions/
├─ Nodes/
├─ Data/
├─ Execution/
├─ Extensibility/
└─ Diagnostics/

DP.WorkFlow.Core/
├─ Documents/
└─ Compilation/
   └─ Analysis/
      ├─ Binding/
      ├─ Graph/
      └─ Validation/

DP.WorkFlow.Runtime/
├─ Preparation/
├─ Execution/
├─ State/
├─ Data/
├─ Hosting/
└─ Infrastructure/

DP.WorkFlow.UI.Shared/
├─ Authoring/
├─ Editors/
├─ Diagnostics/
├─ Monitoring/
└─ Styling/
```

禁止新增含义不明确的顶层目录：`Models`、`Services`、`Helpers`、`Common`、`Misc`。文件应放入拥有其不变量和行为的 Module。

## 5. 目标依赖方向

```text
Workflow.Abstractions
        ↑
Workflow.Core
        ↑
Workflow.Runtime

Workflow.Nodes       ──→ Workflow.Abstractions
Workflow.Persistence ──→ Workflow.Core + Workflow.Abstractions
Workflow.Studio      ──→ Workflow Kernel + Persistence + 组合所需节点包

DP.Vision.OpenCv / DP.Vision.Halcon ──→ DP.Vision.Algorithms ──→ DP.Vision
Workflow.Nodes.Vision ──→ Workflow.Abstractions + DP.Vision.Algorithms
Workflow.Vision.UI ──→ Workflow Studio + DP.Vision.UI
DP.Vision 所有项目不得反向依赖 Workflow
Platform 不得反向依赖 Workflow 或 Vision
```

## 6. 已知待拆依赖

以下依赖已经通过物理分类显式暴露，但本次目录调整不同时重写行为：

Composite、Process 节点仍引用 Runtime；应逐项判断是必要运行宿主能力，还是应下移的执行 port。UI.Shared 已通过结构能力移除 Standard/Composite 引用。

旧视觉工程与兼容集成已删除，主线不再有 `src/Vision` 或 `tests/Vision` 目录。新版相机直接依赖独立厂商 SDK 边界，双平台显示只使用 DP.Vision 原生画布。

只有在上述 seam 被确认后才新增或拆分程序集，避免用空壳项目替代真实设计。

## 7. 修改规则

1. 新项目必须先确定所属领域和依赖方向。
2. 新节点优先进入现有节点包；只有依赖集合或发布生命周期不同才创建新节点包。
3. WinForms/WPF 不得拥有工作流语义，只实现 Studio Interface。
4. Provider 不得依赖工作流；工作流集成代码应位于 Workflow 一侧。
5. 测试通过与生产代码相同的 Module Interface 验证行为，不按内部文件夹机械拆分测试项目。
6. 每次结构变更必须运行 `tools/Test-DPWorkFlow.ps1` 并执行 Release Solution 构建。
