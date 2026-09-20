# 对《大型节点流程系统：架构审阅与演进路线》的评审意见

被评审对象：`docs/architecture-review-and-roadmap.md`
评审方式：逐条核对文档中的代码引用（文件、行号、符号）与探针产物，并复核其结论强度是否与证据匹配。
本文件是**评审意见**，不改变被评审文档，也不修改任何生产代码。

---

## 0. 总体判断

这是一份**高于常见水平**的架构评审，可以提交给团队。主要理由：

- 证据分级明确（【已复现】/【已确认结构】/【代码与竞态分析】/【范围限制】），读者能据此决定信任度；
- 主动限制自己的结论强度（AR-04「本轮未做并发复现，不能称为已发生的现场故障」、AR-09「没有跑性能基准，不能给出实际吞吐退化数值」、AR-12「不能推断其他地方完全没有压测」）——**没有把"我没找到"说成"不存在"**，这是评审里最稀缺的克制；
- 每条建议配了可判定的退出门槛，"不建议现在做的事情"提前挡掉了常见误读；
- 第 29 行对 Process/Composite 引用 Runtime 的判断（"并不自动等于依赖环…需要继续分辨节点能力与应用级编排，而不是机械地禁止全部引用"）比"一律禁止"成熟；
- 第 151 行"逻辑分层，不先增加程序集"+「工位监管高于 Engine 生命周期，但不替 Engine 执行节点」+「不是微服务拆分建议」三句，把最容易被误读的方向提前钉住了。

我核对的结果：**代码引用基本准确**。抽查 12 处行号，`WorkflowRuntimePluginCatalog.cs:89`、`WorkflowNodeConfigurationSnapshotter.cs:220`、`WorkflowNodeExecutionContext.cs:45`、`LoadVisionFileNode.cs:38`、`WorkflowVisionFrameScope.cs:119`、`WorkflowWarningHandlerCoordinator.cs:36`、`WorkflowDocumentJsonStore.cs:285` 等**精确命中**；其余偏移 1–3 行（详见 §5.1）。AR-02 的一个洞察还**优于**我上一轮的结论（详见 §6.1）。

下面按"阻塞级 → 证据链 → 覆盖面 → 路线图 → 表述"五层给出问题。

---

## 1. 阻塞级问题（建议修订后再提交）

### 1.1 项目不在版本控制下，而文档的多项建议以 VCS 为前提

取证：

```text
DP.WorkFlow/            无 .git（同级 Base/、Halcon_DP.../ 有）
git status            → fatal: not a git repository
```

但文档建议：

- AR-11「运行制品记录文档修订/摘要、插件与节点版本」；
- AR-11「先支持锁定版本部署和**回退**」；
- AR-11「节点级版本迁移注册、旧文档语料测试」；
- 阶段 D「运行制品清单、故障审计、发布和回退」。

**没有 VCS，就没有"修订"、没有"回退"、没有"制品身份"**。AR-11 和阶段 D 的全部内容都建立在"存在可追溯的版本"这一前提上，而该前提当前不成立。更直接的影响是：AR-01/AR-02/AR-03/AR-04 都涉及跨程序集、跨平台（net48/net8.0）的改动，**没有版本控制意味着没有安全网**，任何一次重构都无法回退。

建议：在阶段 A 之前插入「阶段 0：纳入版本控制」，并把它写成阶段 A 的**前置条件**而非并列项。`.gitignore` 文件已存在（含 `bin/`、`obj/`、`.tmp/` 等规则），说明曾经打算使用 VCS，需要确认是漏初始化还是被整体拷贝时丢失。

### 1.2 探针产物不在项目内，路径描述不准确，且不会被保留

文档第 9 行：

> 额外执行了最小探针 `.pi-tmp/architecture-review-probe/`（位于仓库根目录），日志为 Git Bash `/tmp/architecture-probe.log`。

实际位置：

```text
C:\Data\PiProgects\WorkFlow\.pi-tmp\architecture-review-probe\   ← 上一级工作区，不是 DP.WorkFlow 仓根
C:\Users\...\AppData\Local\Temp\architecture-probe.log            ← Git Bash /tmp，易失
```

三个问题：

1. **位置描述错误**：探针在 `DP.WorkFlow` 的**上级目录**，不在被评审项目的根目录；
2. **证据不可保留**：探针与日志都在项目之外，不受任何忽略规则约束，也不会随项目分发，下一次评审无法复跑；
3. **忽略规则缺口**：`.gitignore` 有 `.tmp/`、`**/.tmp/`，**没有 `.pi-tmp/`**——若把探针移入项目，会被误提交。

文档阶段 A 要求"将三个已复现问题转成永久回归测试"，但**探针本身是临时的**，两者矛盾。

建议：
- 探针移入 `tests/Workflow/DP.WorkFlow.ArchitectureProbes/`（或 `tools/probes/`）并入库；
- 探针输出**直接写文件到仓库内**（如 `artifacts/probe/`），不要写 `/tmp`；
- `.gitignore` 增补 `.pi-tmp/`；
- 文档中把探针路径改为项目内相对路径，并把日志关键输出**内嵌进文档**（当前只贴了 5 行，实际可贴完整输出）。

### 1.3 与既有评审文档的关系未声明

`docs/` 下现在同时存在：

```text
architecture-smell-review.md              （上一轮，依赖/内核/UI/构建四类异味）
architecture-review-and-roadmap.md        （本文件）
node-platform-review-backlog.md           （NP-01..NP-14）
node-fault-recovery-review.md
```

本文件末行只说"补充并更新 `node-platform-review-backlog.md` 的证据"，**没有说明与 `architecture-smell-review.md` 的关系**：是取代、合并、还是有意裁剪？读者无法判断哪份是权威。

这个项目的文档密度已经很高（16 篇 ADR + 10 篇专项 + 45KB `progress.md`），再叠加并存评审会继续推高导航成本。建议在文档头部加一行 `supersedes: docs/architecture-smell-review.md`（或 `merged-into`），并删除被取代的那份。

---

## 2. 证据链问题

### 2.1 AR-01 的结论方向正确，但**漏报了影响范围**，且归因层级未收敛

**先说文档做对的部分**：我独立复核后确认 AR-01 是**真实的契约缺陷**，而且比文档描述的更严重。

`IWorkflowRunPreparationService` 在**三个不同生命周期层级**被调用：

```text
Hosting/WorkflowRuntimeHost.cs:282        ← 根运行启动
Execution/WorkflowEngine.cs:538           ← 每次子运行（RunTrackedChildAsync）
Execution/WorkflowJointRecoveryGroup.cs:149 ← 每个联合恢复参与者
```

文档只引用了第二处（子运行），**遗漏了根运行与联合恢复两处**。而这三处共用同一个扁平注册的服务实例——这才是"准备与资源作用域混淆"的完整证据。建议补上，这条会更难被反驳。

**再看需要收敛的部分**：探针的构造方式是

```csharp
.Add<IWorkflowRunPreparationService>(frames)   // 同一个 frames 实例
.Add<IWorkflowFaultRecoveryCoordinator>(...)
```

配合样例的实际装配（`samples/DP.WorkFlow.WinForms.Sample/Form1.cs:96,112` 与 `samples/Legacy/WpfApptest/MainWindow.xaml.cs:66,68` 均把**同一个 `_frameScope`** 注册为 `IWorkflowVisionFrameScope` 和 `IWorkflowRunPreparationService`）——文档第 44 行也如实写了这一点。

于是"缺陷在哪一层"存在三个候选，文档把它们混在一个 P0 里：

| 候选 | 内容 | 修复位置 |
|---|---|---|
| (a) 内核 | 子运行/根运行/联合恢复都无差别调用扁平准备服务 | `WorkflowEngine` / `WorkflowRuntimeHost` |
| (b) 接口 | `IWorkflowRunPreparationService` 没有根/子作用域维度 | Abstractions 契约 |
| (c) 装配 | 把有状态的帧仓注册为准备服务 | 样例与宿主 |

文档的建议实际指向 (b)（RunScope 所有权契约），方向是对的。但标【已复现】并列为 P0，读起来像是 (a) 已坐实。**探针证明的是机制，不是生产路径**：它用自定义 `ProbeNode` + 自定义 `ProbeHandler` 模拟了"取图→故障→恢复→读原图"，没有走 `LoadVisionFileNode`。

建议：把 AR-01 拆成两栏——「机制已复现（探针）」与「生产路径未复现（待补）」——并明确写出**最小修复面在哪一层**。同时补一条受影响面：除子运行外，根运行与联合恢复同样受影响。

### 2.2 AR-03 缺"受影响节点清单"，严重度可能被高估

探针的复现是**构造性**的：自定义 `readonly record struct Payload(List<int> Items)`，证明的是"含引用型成员的 struct 会被 `IsSharedImmutable` 判定为可共享"。这成立，我复核了 `WorkflowNodeConfigurationSnapshotter.cs:220` 的实现（`type.IsValueType` 直接返回 true）。

但**文档没有回答一个决定严重度的问题：现有节点配置里有没有含引用型成员的 struct？** 如果没有，这是"契约不完备的隐患"（P1），而不是"当前会出错"（P0）。如果有，应当**逐个列出节点类型**。

建议：补一节"受影响的现有节点配置清单"。若为空，把 AR-03 降为 P1 并说明理由；若非空，把清单附在证据后——这比探针本身更有说服力。

### 2.3 "已复现"标签下，三条证据的强度并不齐

| 条目 | 复现类型 | 强度 |
|---|---|---|
| AR-02 | 直接代码推导 + 探针 | 最强。**无需探针即可从代码推出**，且我上一轮独立确认 |
| AR-03 | 构造性复现 | 中。证明机制，未证明现有配置受影响 |
| AR-01 | 构造性复现 | 中。机制已证，生产路径未证 |

三条同标【已复现】，读者会认为强度一致。建议在标签后补类型后缀，例如【已复现·构造性】/【已复现·生产路径】/【已确认·代码推导】。

### 2.4 缺"已排除/已证伪"清单

文档在"应保留的架构资产"里写了对的部分（很好），但没有交代**查过什么、确认没问题**。读者因此无法判断覆盖面：AR-01..12 是"发现的问题"还是"所有问题"？

建议补一节"本轮已核对且未发现问题的范围"，哪怕只有三五行（例如：Kernel 未反向依赖领域节点或桌面 SDK；Token/Scope 可见性语义自洽；未知节点保真策略正确）。

---

## 3. 覆盖面缺口

以下问题在上一轮评审中已取证成立，本文件未收录。请确认是**有意裁剪**（那应写明"本轮范围不含 X"）还是**遗漏**：

| # | 问题 | 取证位置 | 与本文件的关联 |
|---|---|---|---|
| 1 | `UI.Shared` 引用 `Runtime + Persistence.Json + ScriptEngine`，名为模型层实为组合根 | `Studio/DP.WorkFlow.UI.Shared/*.csproj` | AR-10 只谈设计器重复，未谈依赖方向 |
| 2 | `UI.Wpf → ScriptEngine.Wpf → ScriptEngine.WinForms`，WPF 传递依赖 WinForms | `Studio/DP.WorkFlow.UI.Wpf/*.csproj` | 与 AR-10 同属双平台问题，且封死跨平台路线 |
| 3 | `Nodes.Standard → ScriptEngine` 直接引用 + `RoslynScriptService.Shared` 静态单例 | `Nodes.Standard/Scripting/CSharpScriptNode.cs:5,179` | AR-12 提到"进程内受信任执行"，但未提依赖泄漏与单例 |
| 4 | `WorkflowBindingResolver.PathPlans` 静态无界缓存，无失效策略 | `Runtime/Data/WorkflowBindingResolver.cs:13` | AR-09 谈快照历史成本，未覆盖这个独立泄漏源 |
| 5 | `Directory.Build.props:4-8` 用注册表探测 HALCON，全局生效 → **构建不可复现** | `Directory.Build.props` | AR-12 谈验收与运维，但构建可复现性缺失 |
| 6 | sln 混入 Legacy 样例（`WinFormsApp_test`/`WindowsFormsApp1`/`WpfApptest`），与 `docs/project-structure.md` §2 声明不符 | `DP.WorkFlow.sln` | 属工程卫生，影响"制品"可信度 |
| 7 | `SafeInvoke` 静默吞掉观察者异常，无诊断出口 | `WorkflowEngine.cs:814-829` | AR-12 谈监控，但未提这个具体缺陷 |
| 8 | `WorkflowProperty*` 三特性 + `IWorkflowNodeEditorCapabilities` 放在 Abstractions（内核携带 UI 关注点） | `Abstractions/Nodes/` | AR-07/AR-10 都绕开了这个更根本的问题 |
| 9 | Motion 18 个"设备动作 = 节点类型"的类型爆炸 | `Nodes.Motion`（24 文件 / 18 类型） | "大型节点流程系统"长期规模化的核心风险，未独立提出 |
| 10 | 测试把缺陷写成规范：`Assert.True(catalog.IsFrozen)` 在 Freeze 失败后断言仍为已冻结 | `WorkflowPluginLoaderTests.cs:56` | **AR-02 的直接后果**：修复必须同步改测试期望，文档未提 |
| 11 | 双平台设计器各自保留私有几何副本（`BuildOrthogonalPath`/`AvoidNodeObstacles`/`Compact`） | `UI.WinForms/.../WorkflowDesignerControl.cs:1726-1798` | AR-10 谈交互状态机，未提几何算法也已重复 |
| 12 | `Nodes.Vision` 用仓外相对路径源码引用 `../DP.Vision/src/...`，无版本锁定 | `Nodes.Vision/*.csproj` | AR-11 谈版本协议，未覆盖跨仓源码耦合 |

其中 **#5（构建不可复现）、#10（测试固化缺陷）、#1/#2（依赖方向）** 建议优先补入——它们分别影响"阶段 A 能否验收"、"AR-02 能否真的关闭"、"阶段 C 的边界目标"。

---

## 4. 路线图的可执行性问题

### 4.1 AR 清单与阶段路线没有交叉映射

阶段 A 写「对应 NP-01/02/03/05/09」，映射到的是 **NP 编号**，而风险清单用的是 **AR 编号**。于是读者无法回答：

- AR-06 在哪个阶段关闭？
- 阶段 B 的退出门槛覆盖哪几条 AR？
- AR-05 到 AR-11 分别归属哪个阶段？

阶段 A 正文用"三个已复现问题"隐式指代 AR-01/02/03，其余靠猜。

建议补一张映射表，列：`AR 编号 | 阶段 | 是否本阶段关闭 | 关闭判据 | 关联 NP`。

### 4.2 AR 之间的前置关系缺失

文档诚实地说"本轮没有足够数据给出可信的季度工期或吞吐承诺"——这个克制是对的。但**依赖关系不是工期**，是可以直接判断的：

- AR-04（统一生命周期串行化）是 AR-05（工位监管生命周期）的**前置**——监管需要一个受监管的生命周期才能挂载；
- AR-02（目录三态）与 AR-03（快照契约）相互独立，可并行；
- AR-07（Document 唯一写入口）与 AR-10（共享交互状态机）有**顺序耦合**——AR-10 抽出的手势→命令层需要 AR-07 的命令入口作为落点；
- AR-01（RunScope 所有权）与 AR-06（能力实例/操作权）共享"所有权契约"这一概念，应**合并设计**再分别实现。

当前路线图是四个并列的桶，没有说明"A 做不完能否开 B"。建议给 AR 之间加前置箭头，并在阶段划分上体现。

### 4.3 阶段 B 的退出门槛缺"可判定"的量化边界

阶段 B 门槛写的是完整场景链（"A/B 交接失败→统一处置→气缸受阻→人工核实→继续处置→按各自入口恢复"），作为场景验收很好。但"同时覆盖取消、设备不退出、新危险、准备失败和晚响应"没有说明**每种情形的预期终态**是什么（是"保留阻断"还是"终止"？）。建议为每种情形写出期望终态，否则测试无法判定通过。

---

## 5. 表述与细节

### 5.1 行号引用普遍偏移 1–3 行，且多处指向文档注释而非代码

抽查结果：

| 文档引用 | 实际内容 | 偏差 |
|---|---|---|
| `WorkflowRuntimeHost.cs:130`（等待停止） | `StopAsync` 的 `<summary>` 注释 | 方法在 **133** |
| `WorkflowRuntimeHost.cs:229`（Reset） | `ResetAsync` 的 `<param>` 注释 | 方法在 **231** |
| `WorkflowDocument.cs:33`（CanvasProjection） | `WorkflowLayout Layout { get; }` | CanvasProjection 在 **~39** |
| `WorkflowRunState.cs:53`（NodeOutputs ToArray） | 文档注释行 | 代码在 **54** |
| `WorkflowPluginLoaderTests.cs:56` | `Assert.True(catalog.IsFrozen)` | **精确** |
| `WorkflowDocumentJsonStore.cs:285` | `if (sourceSchemaVersion >= CurrentSchemaVersion && ...)` | **精确** |

行号在活跃重构期必然漂移。建议统一改为**符号引用为主、行号为辅**，例如 `WorkflowRuntimeHost.StopAsync`、`WorkflowDocument.CanvasProjection`——这样文档不会因为一次编辑就失效。

### 5.2 "45/45" 口径不可复核

第 7 行：「没有修改生产实现，**没有重新执行全部测试**。工程核对为 45/45。」

`DP.WorkFlow.sln` 有 61 个 `Project(` 条目（含解决方案文件夹），可构建项目 40+。"45/45" 指什么？读者无法复核，且与"未重跑测试"并置，容易让人误以为做过完整验证。

建议：明确写成"项目引用/工程数量核对 45/45（非测试结果）"，或直接删除该数字。

### 5.3 评审过程污染了工作区，且文档未记录

`C:\Data\PiProgects\WorkFlow\`（上一级）中留有 9 个**文件名被破坏的日志**和一个 `NUL` 文件：

```text
C:DataPiProgectsWorkFlow.pi-tmpcritical-full.log            Sep 3
C:DataPiProgectsWorkFlow.pi-tmpfinal-test.log               Sep 3
C:DataPiProgectsWorkFlow.pi-tmpfive-round-final.log         Sep 3
C:DataPiProgectsWorkFlow.pi-tmpfive-round-test.log          Sep 3
C:DataPiProgectsWorkFlow.pi-tmpoptimization-final-633.log   Sep 3
C:DataPiProgectsWorkFlow.pi-tmpoptimization-final-green.log Sep 3
C:DataPiProgectsWorkFlow.pi-tmpoptimization-final-tests.log Sep 3
C:DataPiProgectsWorkFlow.pi-tmpoptimization-full.log        Sep 3
C:DataPiProgectsWorkFlow.pi-tmpp0-document-full.log         Sep 3
NUL                                                         Sep 4
```

成因明确：在 Git Bash 中把 Windows 路径当文件名使用（冒号被剥离），以及 `> NUL` 未改成 `> /dev/null`。这些文件**跨越 Sep 3 – Sep 20 多次出现**，说明是稳定复现的工具使用问题，而非偶发。

建议：评审文档应交代自身产生的工件去向；这些文件建议清理（我未删除任何文件）。另外阶段 D 谈"发布与制品可信度"时，工作区卫生本身也是可信度的一部分。

---

## 6. 值得保留的，以及一处文档优于既有结论的地方

### 6.1 AR-02 的洞察优于上一轮评审

文档第 58 行：

> 只把 `_frozen=true` 移到后面不足以恢复已冻结的子目录。

这是**上一轮评审遗漏的**，而且是对的。复核实现：

```csharp
var descriptors = Nodes.Freeze();   // 子目录已不可逆冻结
Handlers.Freeze();                  // 同上
_frozen = true;                     // ← 移到这里也无法回滚上面两行
```

配合 `WorkflowPluginLoaderTests.cs:59-60` 在失败后仍断言 `handlers.Register(...)` 抛异常，可确认子目录的冻结是**永久**的。因此修复方案必须是"**在候选目录上完整校验，通过后再发布**"，而不是调整赋值顺序。文档给出的方向（"重试必须得到明确错误或在全新候选目录重建"）正是唯一可行的方案。这条应当作为 AR-02 的核心结论突出。

### 6.2 应原样保留的部分

- 证据分级标签体系；
- 每条 AR 的"建议 + 验收"双段结构；
- "不建议现在做的事情"清单；
- 第 151 行"逻辑分层，不先增加程序集"——比按程序集切分更务实，且明确声明"不是微服务拆分建议"；
- 第 161 行"工位监管高于 Engine 生命周期，但不替 Engine 执行节点"——一句话钉住了最容易失控的边界；
- 第 29 行对"引用 Runtime ≠ 依赖环"的分辨；
- 阶段 D 门槛"由目标工位的图规模、执行频率、历史保留、停机预算和安全要求确定"——拒绝给无依据的工期承诺。

---

## 7. 建议的修订清单

按优先级排列，可直接勾选执行。

**提交前必须完成**

- [ ] 补「阶段 0：纳入版本控制」，并声明为阶段 A 的前置条件（§1.1）
- [ ] 探针移入项目内并入库；日志写入 `artifacts/`；`.gitignore` 增补 `.pi-tmp/`（§1.2）
- [ ] 修正探针路径描述（当前写的"仓库根目录"实为上级工作区）（§1.2）
- [ ] 声明与 `architecture-smell-review.md` 的关系（取代/合并），并删除被取代文档（§1.3）

**证据链补强**

- [ ] AR-01 补上根运行（`WorkflowRuntimeHost.cs:282`）与联合恢复（`WorkflowJointRecoveryGroup.cs:149`）两处调用点（§2.1）
- [ ] AR-01 拆分为「机制已复现」与「生产路径未复现」，写明最小修复面所在层级（§2.1）
- [ ] AR-03 补"受影响的现有节点配置清单"；为空则降级为 P1（§2.2）
- [ ] 三条"已复现"补充复现类型后缀（§2.3）
- [ ] 补一节"本轮已核对且未发现问题的范围"（§2.4）
- [ ] 明确说明本轮范围是否包含 §3 表中的 12 项；不含则写明"范围外"（§3）

**路线图可执行性**

- [ ] 增加 `AR × 阶段` 映射表，含关闭判据与关联 NP（§4.1）
- [ ] 标注 AR 之间的前置关系，至少含 AR-04→AR-05、AR-07→AR-10、AR-01↔AR-06（§4.2）
- [ ] 阶段 B 为每种异常情形写出期望终态（§4.3）

**表述**

- [ ] 行号引用改为"符号名为主、行号为辅"（§5.1）
- [ ] 修正或删除"45/45"口径（§5.2）
- [ ] 交代评审工件去向；清理上级目录的 9 个畸形日志与 `NUL` 文件（§5.3，我未代为删除）

**建议突出**

- [ ] 将 §6.1 的"子目录冻结不可逆，修复必须是候选目录+发布"提升为 AR-02 的核心结论

---

## 附：本次核对的取证记录

| 核对项 | 结果 |
|---|---|
| `WorkflowRuntimePluginCatalog.cs:89` Freeze 起始 | 精确 |
| `WorkflowNodeConfigurationSnapshotter.cs:220` `IsSharedImmutable` | 精确 |
| `WorkflowNodeExecutionContext.cs:45` `GetRequiredCapability` | 精确 |
| `LoadVisionFileNode.cs:38` 返回 Retain 句柄 | 精确 |
| `WorkflowVisionFrameScope.cs:119` `PrepareAsync` 起始 | 精确 |
| `WorkflowWarningHandlerCoordinator.cs:36` 复用 `context.Services` | 精确 |
| `WorkflowDocumentJsonStore.cs:285` NodeVersion 全等校验 | 精确 |
| `WorkflowRuntimeBinder.cs:60` `ToArray()` 后转 `IReadOnlyList` | 成立（可强转回数组修改） |
| `WorkflowRuntimeHost` 有 `StopAsync`/`ResetAsync`，`IWorkflowRuntimeHost` 无 | 成立 |
| `WorkflowEngine.Recovery.cs`、`WorkflowJointRecoveryGroup.cs` 存在 | 成立 |
| 样例双注册（`Form1.cs:96,112` / `MainWindow.xaml.cs:66,68`） | 成立 |
| `PrepareAsync` 会 `Clear()` 并 `Dispose` 全部帧租约 | 成立 |
| 探针日志 5 行输出 | 与文档引用一致 |
| 探针源码逻辑 | 与文档描述一致（含主动捕获 `ObjectDisposedException` 的说明属实） |
