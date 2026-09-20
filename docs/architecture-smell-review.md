# DP.WorkFlow 架构异味评审与长期演进设计

状态：代码取证结论。本评审只读源码、文档、构建脚本与测试，**未修改任何运行行为**。
方法：静态取证（项目引用图、文件规模、方法清单、契约与实现对照）+ 与 `CONTEXT.md`、`docs/project-structure.md`、`docs/node-platform-review-backlog.md` 声明的目标比对。
口径：**异味 = 代码现状与项目自己声明的架构目标之间的偏离**，不是主观风格偏好。

---

## 0. 结论摘要

项目在**领域建模**上的投入远高于同类工业软件：`CONTEXT.md` 的术语体系（文档 / 执行计划 / 运行时绑定计划 / 运行组合目录 / 执行令牌 / 并行作用域）严谨且自洽，16 篇 ADR 覆盖了关键取舍。真正的问题不在概念层，而在**概念落地的收口处**：边界被声明了但没被执行，职责被命名了但没被拆分，风险被识别了但被测试固化。

按严重度分四档：

| 档位 | 含义 | 数量 |
|---|---|---|
| S1 | 与项目自己声明的验收目标直接冲突，或已构成正确性/资源风险 | 4 |
| S2 | 边界声明与代码依赖不一致，架构意图被侵蚀 | 6 |
| S3 | 职责过载与重复实现，随规模线性恶化 | 7 |
| S4 | 工程卫生与治理，影响可复现构建与知识传递 | 4 |

最高优先级的四件事：

1. `WorkflowRuntimePluginCatalog.Freeze()` 的冻结语义与 `NP-01` 验收目标相反，且测试把风险行为写成了期望。
2. 节点包（Standard / Composite / Process）在编译期依赖 `Runtime` 与 `Platform`，插件模型实际上不成立。
3. 运行快照在热路径上无条件全量构造，且与调度状态共用一把全局锁。
4. UI 层是当前最大的复杂度堆积地：双平台设计器约 3500 行几乎逐方法重复。

---

## 1. 依赖与边界异味

### S1-1 节点包编译期依赖宿主（插件模型不成立）

证据（`*.csproj` 的 `ProjectReference`）：

```text
DP.WorkFlow.Nodes.Composite  →  Abstractions + Core + Runtime
DP.WorkFlow.Nodes.Process    →  Abstractions + Runtime
DP.WorkFlow.Nodes.Standard   →  Abstractions + Platform/Scripting/ScriptEngine
DP.WorkFlow.Nodes.Vision     →  Abstractions + 仓外 ../DP.Vision.Algorithms
```

`docs/project-structure.md` §5 声明的目标方向是 `Workflow.Nodes ──→ Workflow.Abstractions`。实际是节点包同时依赖 `Core`、`Runtime`，甚至依赖 `Platform`。

这不是"多引用一个程序集"的问题，而是**插件方向被反转**：`Runtime` 是宿主、节点是插件，插件反向依赖宿主的具体程序集后，`WorkflowRuntimePluginCatalog` 所描述的"启动期成组收集、完成后校验并冻结"就失去意义——节点包无法脱离内核版本独立编译与发布，任何内核内部重构都会击穿所有节点包。

`docs/project-structure.md` §6 已把 Composite/Process 的 Runtime 依赖记为"已知待拆"，但**没有给出判定标准**（哪些是"必要运行宿主能力"，哪些"应下移为执行 port"）。这是这条债长期不还的直接原因。

### S1-2 `Platform` 成为 Workflow 的隐式内核依赖

`DP.WorkFlow.Nodes.Standard/Scripting/CSharpScriptNode.cs`：

```csharp
using ScriptEngine;                                    // :5
public CSharpScriptNodeHandler() : this(RoslynScriptService.Shared)   // :179
```

`DP.WorkFlow.UI.Shared/Editors/WorkflowCSharpScriptEditorModel.cs:11`：

```csharp
private static readonly RoslynScriptService Service = new();
```

后果链：`ScriptEngine` 是一个多目标（`net48;net8.0`）、自带 Roslyn、自带 `Worker` 进程隔离与 `Workspaces` 的**平台级库**。它一旦被内核节点包和 UI.Shared 直接引用：

- 内核的编译面被 Roslyn 与 `Microsoft.CodeAnalysis.*` 绑架；
- `net48` 的兼容分支（polyfill、条件编译）沿着引用链渗进工作流构建；
- 脚本能力无法被替换（例如未来要做沙箱/远程脚本服务），因为调用点写死在两个程序集里。

`docs/project-structure.md` §5 只写了"Platform 不得反向依赖 Workflow"，**反方向无人约束**，于是泄漏从此进入。值得注意的是 `Abstractions/Scripting/IWorkflowScriptHostContext.cs` 已经存在——设计意图本来是对的，只是没有落地。

### S2-1 `UI.Shared` 名为模型层，实为组合根

```text
DP.WorkFlow.UI.Shared  →  Abstractions + Core + Runtime + Persistence.Json + ScriptEngine
```

`docs/project-structure.md` §3 描述它是"框架无关的 Authoring、Editors、Diagnostics 和 Monitoring 模型"。一个"框架无关模型层"同时引用 `Runtime`（执行）与 `Persistence.Json`（存储），意味着：

- 任何 UI 编辑体验的改动都可能牵动 `Runtime` 的引用面；
- 无法为"纯设计期宿主"（无 Runtime）复用 Authoring 模型；
- 违反它自己声明的"WinForms/WPF 不得拥有工作流语义"的对称约束——语义上移到了 UI.Shared。

### S2-2 WPF 经 WinForms 传递依赖

```text
DP.WorkFlow.UI.Wpf  →  ScriptEngine.Wpf  →  ScriptEngine.WinForms
```

WPF 宿主最终拖入 `System.Windows.Forms`。对工业现场的单机应用当前无痛，但它封死了三条长期路线：跨平台宿主、无 WinForms 的 WPF 精简部署、以及把编辑器组件做成可独立引用的包。

### S2-3 跨仓源码引用而非包引用

`DP.WorkFlow.Nodes.Vision.csproj` 与 `DP.WorkFlow.Vision.UI*.csproj` 使用 `..\..\..\..\..\DP.Vision\src\...` 形式的 `ProjectReference`。

- 构建要求同级目录存在 `DP.Vision` 源码（README 已声明）；
- 版本无法锁定，两侧改动互相击穿，且没有版本号可回溯；
- `README.md` 声称"独立 DP.Vision""不反向依赖 Workflow"，但正向是源码级耦合，"独立"只成立了一半。

单向依赖规则是好的，缺的是**单向的版本化契约**。

### S2-4 平台库与工作流共用构建属性

`Platform/Desktop/ModernUI.WinForms` 有 123 个源文件、20337 行，是多目标（`net48;net8.0-windows`）通用控件库；`Platform/Scripting/*` 共 6 个项目。它们与 `Workflow` 共用同一个 sln 和同一个 `Directory.Build.props`。

于是工作流的需求变成了平台的约束：`Directory.Build.props` 里的 HALCON 注册表探测、`PlatformTarget=x64`、`TreatWarningsAsErrors` 全部对 ModernUI 生效。**两个发布节奏完全不同的产品被绑成一次构建。**

---

## 2. 内核设计异味

### S1-3 冻结语义与验收目标相反（含被测试固化的风险行为）

`Abstractions/Plugins/WorkflowRuntimePluginCatalog.cs`：

```csharp
public WorkflowRuntimePluginCatalog Freeze()
{
    lock (_syncRoot)
    {
        if (_frozen) return this;        // :93-94  已冻结 → 直接返回，不再校验
        var descriptors = Nodes.Freeze();
        Handlers.Freeze();
        _frozen = true;                  // :98    ← 先置位
        foreach (var descriptor in descriptors.Values)
        {
            var sample = descriptor.Factory();
            try { _ = Handlers.ResolveWithRequirements(sample); }   // :104 后置校验
            catch (...) { throw new InvalidOperationException(...); }
        }
        return this;
    }
}
```

`_frozen = true` 在验证循环**之前**执行。若某个节点缺少处理器或多重匹配：

- 异常抛出，但 `_frozen` 已经为 `true`；
- `IsFrozen` 此后恒为 `true`；
- 再次调用 `Freeze()` 会在 `:93` 直接 `return this`，**跳过全部校验**。

即"校验失败的目录"会被后续调用当作"已冻结的有效目录"复用——这正是 `NP-01` 验收目标明文要求禁止的（"冻结失败状态明确，失败目录不能当成功目录复用"）。

更值得警惕的是测试把该行为写成了期望（`tests/Workflow/DP.WorkFlow.Core.Tests/WorkflowPluginLoaderTests.cs:53-58`）：

```csharp
var error = Assert.Throws<InvalidOperationException>(() => catalog.Freeze());
Assert.Contains("没有已注册的处理器", error.Message);
Assert.True(catalog.IsFrozen);     // ← 断言"失败后仍然是已冻结"
```

失败态需要被建模为**三态**（`Mutable` / `Frozen` / `Faulted`），而不是布尔量。测试期望需同步修正。

### S2-5 注册的非原子性

```csharp
if (!_extensionIds.Add(extensionId)) throw ...;   // :52  先记账
extension.Register(this);                          // :54  再执行插件回调（在锁内）
```

插件回调在中途抛异常时，`_extensionIds` 已写入而节点/处理器只注册了一半，目录进入"部分注册且不可重试"状态（`NP-03` 已识别）。同时在 `lock` 内执行第三方回调，会把插件初始化耗时直接转成全局锁占用。

### S1-4 运行快照在热路径上无条件全量构造

```csharp
private void PublishSnapshot(string? message = null) =>
    SafeInvoke(SnapshotChanged, GetRuntimeSnapshot(message));   // :785-786
```

`GetRuntimeSnapshot()`（`:193-230`）在 `lock (_stateSync)` 内通过 LINQ `ToDictionary` 构造 4 个只读字典（节点、并行作用域、活动令牌、子流程），并复制 `RunState.Faults` 与 `RunState.NodeOutputs`。

三个叠加问题：

1. **无订阅者也构造**。`SafeInvoke` 对 `null` 处理器做了保护，但实参 `GetRuntimeSnapshot(message)` 是**提前求值**的——没有订阅者时依然付出全量分配。
2. **调用频率是每节点两次**。`MarkNodeStarted`（`:733`）与 `MarkNodeFinished`（`:766`）各发布一次；并行分支内每个节点都触发，`ExecuteParallelScopeAsync` 还会额外发布。
3. **与调度状态共用一把锁**。`PublishSnapshot` 虽然写在锁外，但 `GetRuntimeSnapshot` 立刻重新进入同一把 `_stateSync`；并行分支在此锁上排队，快照构造成为并发扩展的直接瓶颈。

配合 `MarkNodeFinished` 中每次清空并重建 `_activeNodeIds`（`:757-763`，O(活动令牌数)），整体呈现 **O(节点数 × 令牌数) 的分配与锁竞争**，而这两者都与流程规模正相关。

`NP-12` 提到"监控订阅失败可诊断而不影响执行"——方向正确，但当前实现把"监控的成本"也放进了执行路径。

### S2-6 配置快照依赖反射

`Core/Compilation/WorkflowNodeConfigurationSnapshotter.cs`：

```csharp
private static readonly MethodInfo MemberwiseCloneMethod = ...;   // :11
var clone = MemberwiseCloneMethod.Invoke(source, null);           // :74
foreach (var field in EnumerateInstanceFields(type))              // :77  私有字段反射
    field.SetValue(clone, clonedFieldValue);
```

问题点：

- `MemberwiseClone` **绕过构造函数**，任何"构造期建立的不变量"在快照中不被重建；
- `IsSharedImmutable`（`:220-224`）把 `Delegate` 与 `JsonDocument` 判定为"可共享不可变"并**直接返回原引用**——闭包捕获的宿主对象、以及可释放的 `JsonDocument` 都会跨"快照边界"共享；
- 无深度预算（`visited` 只防环，不防深图），深层对象图存在栈溢出路径；
- `Restore`（`:30-51`）用反射逐字段回填现有实例，同样绕过不变量；
- 集合重建走 `Activator.CreateInstance(type, nonPublic: true)`（`:200`），失败信息是"必须提供无参数构造函数"——把类型约束的失败推迟到运行期。

`NP-02` 已要求"明确允许/拒绝的配置类型、深度预算、独立快照与恢复契约"。当前实现的取向是**用反射兜住一切**，代价是编译期无法验证配置可快照性。长期应改为显式契约（`IConfigurationCloneable` 或源生成快照），反射仅作过渡期的兜底并给出显式诊断。

### S3-1 `WorkflowEngine` 职责过载

954 行（另有 `partial` 文件），单一类同时承担：

| 职责 | 证据 |
|---|---|
| 调度与路径推进 | `ExecutePathAsync` :317 |
| 节点执行与结果提交 | `ExecuteNodeAsync` :380 |
| 并行作用域编排 | `ExecuteParallelScopeAsync` :558 |
| 子引擎创建与监管 | `RunTrackedChildAsync` :503 |
| 暂停 / 外部 Hold 门 | `Pause/Resume/AddExternalHold` :116-188 |
| 快照投影 | `GetRuntimeSnapshot` :193 |
| Trace 环形缓冲 | `WriteTrace` :788 |
| 故障记录 | `RunState.RecordFault` :479 |
| 恢复循环 | `RunAsync` :261-276 |
| 安全计数上限 | `GetNextNodeExecutionCount` :635 |

状态面：13 个可变集合/字典 + 6 个序列计数器 + 9 个公开事件，全部由单一 `_stateSync` 保护。

这些职责的**变更原因完全不同**（调度语义、并发模型、监控投影、恢复协议、资源安全），却耦合在一个类型和一把锁里。任何一项演进都要重新推演另外几项的并发正确性。

### S3-2 静态可变状态绕过运行实例隔离

| 位置 | 内容 | 风险 |
|---|---|---|
| `Runtime/Data/WorkflowBindingResolver.cs:13` | `static ConcurrentDictionary<BindingPlanKey, BindingPathPlan> PathPlans` | 无上界、无失效；长驻进程内存持续增长；跨运行实例共享，与"运行实例独立状态"的心智模型冲突 |
| `Nodes.Standard/Scripting/CSharpScriptNode.cs:179` | `RoslynScriptService.Shared` | 脚本服务成为进程级单例，宿主无法替换或隔离 |
| `UI.Shared/Editors/WorkflowCSharpScriptEditorModel.cs:11` | `static readonly RoslynScriptService Service = new()` | 设计期与运行期各持一套脚本服务，配置漂移 |
| `UI.Shared/Authoring/Designer/WorkflowDesignerSession.cs:64` | `static readonly object ClipboardSync` | 同进程多文档共享剪贴板锁 |
| `Platform/Scripting/ScriptEngine/Compilation/ScriptEnvironmentSnapshot.cs:23-25` | 3 个静态 `ConcurrentQueue` | 以静态队列充当排序缓存 |

`CONTEXT.md` 明确"运行实例：执行计划的一次独立执行，拥有自己的身份、状态、输出历史和故障记录"。进程级静态缓存在语义上属于"无人拥有的第三层状态"，它既不是文档也不是运行实例，却会影响两者的行为。

### S3-3 服务定位器取代能力声明

节点包中 `GetService` / `GetRequiredCapability` 共 **93 处**，覆盖 Motion（18 个文件）、Process（21 个文件）、Standard（12 个文件）、Vision（5 个文件）。

项目已有两套正确的机制：`WorkflowRuntimeCapabilityRequirement`（声明）与 `WorkflowRuntimeCapabilityValidator`（运行前递归预检）。但实际节点的依赖是**执行期从 `IServiceProvider` 取**，于是：

- 节点对宿主的依赖无法静态枚举；
- 预检只验证"服务非空"，不验证接口契约（`NP-03` 已识别）；
- 声明与使用可能不一致，且不一致不会被任何检查发现。

`Motion/IO/IoWriteNode.cs` 的 `IoReadNodeHandler.GetService(context)` 是典型形态：能力需求藏在另一个 Handler 的静态辅助方法里，而不是节点声明里。

### S3-4 观察者异常被静默吞掉

```csharp
private static void SafeInvoke<T>(Action<T>? handlers, T argument)
{
    foreach (Action<T> handler in handlers.GetInvocationList())
    {
        try { handler(argument); }
        catch { /* 观察者异常不得改变流程执行结果 */ }   // :824-827
    }
}
```

"不影响执行"的取舍是对的，但**没有任何诊断出口**：宿主无法得知自己的监控订阅正在持续抛异常，只能看到监控面板空白。应至少计数并通过 `RunState`/Trace 暴露。

---

## 3. 节点层异味

### S3-5 设备动作 1:1 映射为节点类型

`Nodes.Motion` 共 24 个源文件，其中 18 个是"设备动作 = 一个节点类型"：

```text
Axis(4)        AxisAction / AxisServo / AxisStop / AxisWait
CodeReader(4)  CodeReaderOpen / Close / Trigger / WaitScan
IO(6)          IoRead / IoWrite / IoWait / IoMultiCheck / IoMultiWait
Pneumatic(4)   CylinderControl / CylinderWait / VacuumControl / VacuumWait
```

`Nodes.Process` 38 个文件，`ProductFlow` 10 + `Recovery` 7 + `Robot` 3 同构。

每个类型都是 `XxxNodeModel` + `XxxNodeHandler` 成对，并需要：一个稳定 `NodeType` 键、一份持久化 schema、一个工具箱条目、一个属性面板描述。新增一台设备通常意味着新增若干节点类型。

对照 `README.md`：Vision 侧已经用"统一范围能力与公开解析入口"取代了"节点类型白名单"，从而支持外部扩展而不增加节点类型。**Motion / Process 没有采用同一模式**——这说明模式已经存在且被验证过，只是没有推广。

`CONTEXT.md` 也早已给出方向：**视觉操作**是"跨视觉实现保持稳定的一项原子能力"，节点只是它的宿主。设备动作同理：应存在"设备能力目录"，节点退化为薄壳。

### S3-6 UI 关注点进入内核契约

`Abstractions/Nodes/` 中与 UI 直接相关：

```text
WorkflowPropertyAttribute.cs           属性面板字段描述
WorkflowPropertyEditorAttribute.cs     编辑器类型
WorkflowPropertyVisibleWhenAttribute.cs 条件可见性
IWorkflowNodeEditorCapabilities.cs     节点编辑能力
```

`docs/project-structure.md` §3 声明 `Abstractions` 是"节点、数据和执行扩展契约；不得引用其他业务项目"，`README.md` 声称"UI 无关节点、端口和 Handler 契约"。

但"字段是否可见、用哪种编辑器渲染"是纯粹的呈现决策。把它放进内核契约，等于要求内核理解 UI 语义：新增一种编辑器控件就要动内核程序集，且内核的 schema 版本被迫跟随 UI 演进。

`NP-07` 已提出 Field Provider / Model + 两平台 Renderer 的方向，与 Vision 的 `PageProvider` / `RendererKey` 机制同构——同样是一个已存在的模式没有推广。

---

## 4. UI 层异味

### S3-7 双平台设计器逐方法重复（当前最大复杂度堆积）

| 文件 | 行数 |
|---|---|
| `UI.WinForms/Authoring/Designer/WorkflowDesignerControl.cs` | 1925 |
| `UI.Wpf/Authoring/Designer/WorkflowDesignerControl.cs` | 1621 |
| `UI.Shared/Authoring/Designer/WorkflowDesignerSession.cs` | 1456 |

两平台设计器各 73 个方法，方法名高度一一对应：

```text
交互     OnMouseDown / OnMouseMove / OnMouseUp / OnMouseWheel / OnMouseDoubleClick
         OnDragEnter / OnDragOver / OnDragDrop / ProcessCmdKey
命中     HitNode / HitPort / HitConnection / HitWaypoint / HitConnectionLabel
绘制     DrawGrid / DrawConnections / DrawNode / DrawPorts / DrawPortLabel / DrawMarquee
         DrawOverviewMap / DrawPendingConnection / DrawWaypointHandles / DrawConnectionLabel
几何     BuildOrthogonalPath / AvoidNodeObstacles / Compact / AddDistinct / Offset
         DistanceToSegment / NormalizeRectangle / NearestSide / StateColor / RoundedRectangle
```

共享层 `UI.Shared/Authoring/Designer/WorkflowOrthogonalRouter.cs`（368 行）**确实被两平台调用**（WinForms 2 处、WPF 1 处）。但两平台各自仍保留私有几何副本——例如 WinForms `:1726-1798` 整段 `BuildOrthogonalPath` / `Offset` / `AddDistinct`，与共享路由器职责重叠。

也就是说：**纯算法共享了，占 90% 的交互状态机、命中测试与绘制组织全部重复。** 一份鼠标交互语义的修改需要同步两处并各自验证；`_dragNodeOrigins`、`_connectionStart`、`_segmentWorkingWaypoints` 这些交互状态在两平台各有一份。

项目已经有正确的做法可循：`PageProvider` / `PageId` / `Priority` / `RendererKey`（`README.md` 提到 Block/Script/Image 已不再硬编码在聚合模型）。设计器应采用同一模式——**共享层产出与平台无关的场景图（可绘制原语 + 命中区域 + 交互状态机），平台层只做"原语 → 原生绘制调用"和"原生输入事件 → 语义事件"。**

### S3-8 `WorkflowDesignerSession` 上帝对象

1456 行，承载 `README.md` 列出的 UI.Shared 能力中的绝大部分：文档事务、Undo/Redo、选择集、剪贴板、视口、工具箱、运行覆盖层、诊断、脏状态、最近文件、面包屑导航。

这些是**相互独立、变更频率不同**的关注点。应拆为 `SelectionModel` / `ClipboardService` / `ViewportState` / `CommandStack` / `RunOverlayModel` / `DiagnosticsModel` 等协作对象，由会话仅作协调。

### S3-9 属性面板重复且混入业务逻辑

```text
UI.WinForms/Editors/WorkflowPropertyPanel.cs   1145
UI.Wpf/Editors/WorkflowPropertyPanel.cs         747
UI.Shared/Editors/WorkflowPropertyInspectorModel.cs  502
```

共享模型（502 行）已经存在，但两个平台面板合计 1892 行——说明呈现层里混入了本应属于共享模型的类型推导、候选枚举、可见性判定等逻辑。这是"模型共享了但没共享够"的典型形态。

### S3-10 自绘脚本编辑器

`ScriptEngine.WinForms/RoslynScriptEditorControl.cs` 1711 行 + `RoslynScriptProjectEditorControl.cs` 1189 行，配合 `ScriptCompletionPopup` 376 行，构成一套自绘代码编辑器（`Scintilla.NET` 仅在 1 处被引用）。这是平台侧独立的复杂度来源，且与工作流的构建配置绑定（见 S2-4）。

---

## 5. 构建与工程卫生异味

### S4-1 主解决方案混入历史样例

`DP.WorkFlow.sln` 含 61 个 `Project(` 条目（40+ 可构建项目），其中：

```text
WinFormsApp_test        ← 与 DP.WorkFlow.WinForms.Sample 语义重复
WindowsFormsApp1        ← ModernUI.WinForms.Sample，保留默认工程名
WpfApptest              ← Legacy，仅作对照
```

样例工程文件名也仍是 `WinFormsApp_test.csproj` / `WindowsFormsApp1.csproj`。`docs/project-structure.md` §2 声明"可运行示例统一位于 `samples/`"并区分 `Legacy/`，但主 sln 未做区分——`docs/project-structure.md` 自己也写了"WpfApptest 已接入新版视觉，**不在主 Solution 中**"，与实际 sln 内容不符。

### S4-2 六种目标框架组合与条件编译扩散

```text
net8.0                     × 11
net8.0-windows             ×  6
net48;net8.0               ×  2
net48;net8.0-windows       ×  4
net8.0;net48               ×  1
netstandard2.0;net8.0      ×  1
```

条件编译分支：`ModernUI.WinForms` 8 处，`ScriptEngine` 11 处以上（`#if NET48` / `#if NETFRAMEWORK` / `#if !NETFRAMEWORK`）。

`net48` 支持把平台库的复杂度显著放大（polyfill、`WindowsProcessJob`、Worker 进程的框架分支），而**工作流内核本身全部是 `net8.0`**。需要明确回答一个架构问题：`net48` 是当前业务硬需求，还是历史兼容？如果前者，应把它隔离在 `Platform` 的独立发布单元里，而不是让整个解决方案共用一套多目标配置。

### S4-3 构建依赖开发机注册表（不可复现构建）

`Directory.Build.props`：

```xml
<_HalconRootFromUserProfile>$(registry:HKEY_CURRENT_USER\Environment@HALCONROOT)</_HalconRootFromUserProfile>
<HALCONROOT Condition="!Exists('$(HALCONROOT)\bin\dotnet35\halcondotnet.dll') and Exists('$(_HalconRootFromUserProfile)\bin\dotnet35\halcondotnet.dll')">...</HALCONROOT>
```

注释自述："避免 HALCON 项目被**静默编译成无 SDK 桩**"——即构建结果取决于当前用户的注册表状态。后果：

- CI 或新开发机上会得到"能编译通过但行为不同"的产物；
- 该属性组对**所有**项目生效，包括与视觉无关的 ModernUI 与 ScriptEngine；
- `TreatWarningsAsErrors=true` 与 `GenerateDocumentationFile=true` 全局启用，配合条件编译会放大跨框架告警面。

应改为显式属性 + CI 注入（或 `Directory.Build.user.props`，不进版本控制），并把 HALCON 边界收敛到 `DP.Vision` 仓（它本来就在外部仓）。

### S4-4 文档治理与知识传递

现状：16 篇 ADR、10 篇 `docs/nodes/` 专项、`progress.md` 45KB。

问题不在数量，而在**形态**：

- `progress.md` 按"当前 / 前轮 / 前前轮"倒序堆叠，无索引、无状态列，45KB 单文件。读者无法快速回答"NP-01 现在到底是什么状态"。
- 文档同时混排"已实现能力""明确边界""未实现项"，且三者在同一段落中并列（例如"最终全量 725/725"与"尚未实现…"紧邻）。**通过数量是过程指标，不是架构完成度指标。**
- 已识别的 P0（`NP-01`/`NP-02`/`NP-03`/`NP-05`）在代码中**全部仍然成立**（本评审逐条取证确认），但 `progress.md` 的多轮"全量绿灯"读起来像是这些问题已收敛。

这是本项目最需要修的一种"元异味"：**文档产出速度长期高于代码收敛速度，导致文档的导航价值下降**。

---

## 6. 目标架构设计

### 6.1 分层与依赖规则（唯一权威方向）

```text
        ┌──────────────────────────────────────────────┐
  L4    │ 宿主与组合根                                  │
        │ Host.Cli / Host.Desktop / Studio.WinForms/Wpf │
        └───────────────┬──────────────────────────────┘
                        │ 只向下依赖
        ┌───────────────▼──────────────────────────────┐
  L3    │ 组合与扩展装配                                │
        │ Plugin.Composition（目录/冻结/预检/装配）      │
        │ Studio.Model（UI 无关的 Authoring 模型）       │
        └───────────────┬──────────────────────────────┘
                        │
        ┌───────────────▼──────────────────────────────┐
  L2    │ 执行内核                                      │
        │ Runtime（调度/状态/绑定/宿主契约）             │
        └───────────────┬──────────────────────────────┘
                        │
        ┌───────────────▼──────────────────────────────┐
  L1    │ 文档与编译                                    │
        │ Core（Documents / Compilation / Analysis）     │
        └───────────────┬──────────────────────────────┘
                        │
        ┌───────────────▼──────────────────────────────┐
  L0    │ 稳定契约（零依赖，不含 UI 关注点）             │
        │ Contracts（Node/Port/Handler/Capability/       │
        │            Identity/Result/Diagnostics）       │
        └──────────────────────────────────────────────┘
```

**节点包（Nodes.*）只允许依赖 L0 `Contracts`。** 若某节点确实需要宿主能力，走两条路之一：

1. 能力在 `Contracts` 中定义为 port（如 `IAxisController`、`IImageSource`），由宿主实现并注册；
2. 能力属于"运行宿主服务"，则定义 `Runtime.Hosting.Abstractions`（**只含接口**）作为独立程序集，节点依赖它而非 `Runtime` 实现体。

**外部能力（脚本、视觉 SDK）通过 port 注入。** `Contracts/Scripting/IWorkflowScriptHostContext` 已有雏形，`CSharpScriptNode` 应只依赖它；`RoslynScriptService` 的实现与装配留在宿主侧。

**UI 关注点出内核。** `WorkflowProperty*` 三个特性与 `IWorkflowNodeEditorCapabilities` 迁出 `Contracts`，改为独立 `Workflow.Editing.Contracts` + Provider 扩展包，与 Vision 的 `PageProvider`/`RendererKey` 统一为同一套"扩展描述 + 平台 Renderer"机制。

### 6.2 内核职责拆分

`WorkflowEngine` 拆为五个协作对象，各自拥有独立不变量与锁域：

| 拆分出的对象 | 职责 | 状态 |
|---|---|---|
| `ExecutionScheduler` | 路径推进、端口选择、安全计数上限 | 调度游标、执行计数 |
| `ParallelScopeCoordinator` | 作用域创建/汇聚/兄弟取消 | 作用域表 |
| `ChildEngineSupervisor` | 子引擎创建、Hold/Pause 传播、子快照转发 | 活动子引擎表 |
| `RunStateProjector` | 快照与 Trace 投影，**含节流与增量** | Trace 环形缓冲、快照序号 |
| `AdmissionGate` | Pause / ExternalHold 合并与等待 | 门状态 |

配套两条硬约束：

- **快照惰性化**：`RunStateProjector` 内部判断订阅者数量，无订阅者时不构造；有订阅者时按时间/事件阈值合并发布。
- **锁域分离**：调度状态与投影状态分锁；投影读取采用不可变快照（copy-on-write）而非在调度锁内 `ToDictionary`。

### 6.3 节点层演进方向

```text
现状：一台设备动作 → 一个新节点类型
目标：设备能力目录 → 参数化节点 + 能力声明
```

参考 Vision 已验证的模式：

- 节点声明所需**能力**（`IAxisController`、`IIoPort`、`IPneumaticActuator`）；
- 具体设备/通道作为**配置 + 绑定**，而非新类型；
- 能力由宿主装配并在运行前递归预检；
- 新增设备 = 新增能力实现 + 配置，**不新增节点类型**。

这直接抑制节点类型爆炸，并使工具箱、持久化 schema、属性面板的规模不再随设备数量线性增长。

---

## 7. 长期发展规划

### 阶段一 · 止血（不改运行语义）

目标：消除 S1 级正确性与资源风险，使后续重构有可信基线。

| 项 | 动作 | 验收 |
|---|---|---|
| 冻结原子性 | `Freeze()` 改为"先校验、后置位"；冻结态建模为 `Mutable/Frozen/Faulted` 三态 | 失败后 `IsFrozen == false`，重试可再次校验；同步修正 `WorkflowPluginLoaderTests` 的期望 |
| 注册原子性 | 插件回调在锁外执行；失败时回滚 `_extensionIds` | 半途失败后目录仍可重试注册 |
| 快照热路径 | 无订阅者不构造；订阅者存在时按阈值合并 | 节点事件数不变的前提下，快照分配次数显著下降（需基准数据） |
| 静态缓存 | `WorkflowBindingResolver.PathPlans` 加上界与失效策略，或改为按运行实例作用域 | 长驻进程反复运行不单调增长 |
| 构建可复现 | 移除 `Directory.Build.props` 的注册表探测，改为显式属性 + CI 注入 | 干净机器与 CI 上产出一致；无"静默桩" |
| 解决方案收敛 | Legacy 样例移出主 sln；样例工程改名 | sln 项目数与 `docs/project-structure.md` §2 一致 |

### 阶段二 · 边界收口

目标：让声明的依赖规则成为可执行的规则。

1. `Contracts` 瘦身：迁出全部 UI 关注点特性；新增 Field Provider 契约。
2. 节点包去 `Runtime` 依赖：Composite / Process 逐项判定"下移为 port"或"依赖 `Runtime.Hosting.Abstractions`"，并补齐判定标准写入 `docs/project-structure.md` §6。
3. 脚本能力 port 化：`CSharpScriptNode` 与 `UI.Shared` 去掉对 `ScriptEngine` 的直接引用。
4. `UI.Shared` 拆分为 `Studio.Model`（纯模型，只依赖 Contracts/Core）与 `Studio.Host`（组合根）。
5. `DP.Vision` 改为包引用或子模块 + 显式版本锁定。
6. 引入架构测试（如基于程序集引用的单元断言），把依赖规则写进 CI，**防止回潮**。

### 阶段三 · 内核重构

目标：让内核的并发与规模特性可预测。

1. 按 §6.2 拆分 `WorkflowEngine`，先抽 `RunStateProjector`（收益最直接），再抽 `ParallelScopeCoordinator` 与 `ChildEngineSupervisor`。
2. 并发模型明确化：单写者 + 不可变状态快照，或按职责分片锁；产出并发设计说明（ADR）。
3. 配置快照去反射：引入显式克隆契约或源生成，反射退为带诊断的兜底。
4. 能力声明与使用一致性校验：预检阶段比对"声明能力"与"实际 `GetService` 调用"，不一致即失败。
5. `SafeInvoke` 增加观察者异常计数与诊断出口。

### 阶段四 · 可扩展与长期演进

1. 设备动作参数化（§6.3），抑制节点类型增长。
2. UI 交互层收敛：共享层产出场景图 + 命中区域 + 交互状态机，平台只做渲染与输入适配；设计器目标是把两个 ~1800 行的平台控件降到"渲染适配 + 事件翻译"。
3. 无界面宿主（Headless）与性能基准治理：以阶段耗时、内存、并发基准驱动优化，替代以测试通过数作为主要进度指标。
4. 诊断与回放：把"执行事实"扩展为完整事故记录（耗时、等待原因、输入来源、故障/恢复因果链）。
5. 版本与兼容策略成文：节点 schema 版本、插件 API 版本、文档迁移策略、DP.Vision 契约版本。

### 贯穿始终 · 治理改进

- `progress.md` 改为**索引 + 状态表**：每条能力一行，列 `状态(未开始/进行中/已实现/已冻结)`、`对应 ADR`、`验收证据`；轮次记录归档到 `docs/history/`。
- `node-platform-review-backlog.md` 的 NP 项增加 `状态` 与 `最后验证日期` 列，并与代码取证入口一一绑定。
- 新增架构决策时同步更新依赖规则文档，**规则变更与代码变更必须同一提交**。

---

## 8. 附录：取证清单

本评审全部结论可按下表复核。

| 编号 | 取证位置 |
|---|---|
| S1-1 | 各 `src/**/*.csproj` 的 `ProjectReference` |
| S1-2 | `Nodes.Standard/Scripting/CSharpScriptNode.cs:5,179`；`UI.Shared/Editors/WorkflowCSharpScriptEditorModel.cs:11` |
| S1-3 | `Abstractions/Plugins/WorkflowRuntimePluginCatalog.cs:89-114`；`tests/Workflow/DP.WorkFlow.Core.Tests/WorkflowPluginLoaderTests.cs:53-58` |
| S1-4 | `Runtime/Execution/WorkflowEngine.cs:193-230,733,766,785-786` |
| S2-1 | `Studio/DP.WorkFlow.UI.Shared/*.csproj` |
| S2-2 | `Studio/DP.WorkFlow.UI.Wpf/*.csproj` → `ScriptEngine.Wpf.csproj` |
| S2-3 | `Nodes.Vision/*.csproj`；`Studio/DP.WorkFlow.Vision.UI*.csproj` |
| S2-4 | `Platform/Desktop/ModernUI.WinForms`（123 文件 / 20337 行）；`Directory.Build.props` |
| S2-5 | `WorkflowRuntimePluginCatalog.cs:43-57` |
| S2-6 | `Core/Compilation/WorkflowNodeConfigurationSnapshotter.cs:11,74-95,220-224` |
| S3-1 | `WorkflowEngine.cs`（954 行）字段与方法清单 |
| S3-2 | `WorkflowBindingResolver.cs:13,122`；`ScriptEnvironmentSnapshot.cs:23-25`；`WorkflowDesignerSession.cs:64` |
| S3-3 | `grep -rn "GetService\|GetRequiredCapability" src/Workflow/Nodes` → 93 处 |
| S3-4 | `WorkflowEngine.cs:814-829` |
| S3-5 | `Nodes.Motion` 24 文件（18 节点类型）；`Nodes.Process` 38 文件 |
| S3-6 | `Abstractions/Nodes/WorkflowProperty*.cs`、`IWorkflowNodeEditorCapabilities.cs` |
| S3-7 | 双平台 `WorkflowDesignerControl.cs`（1925 / 1621 行，各 73 方法）；`UI.Shared/.../WorkflowOrthogonalRouter.cs` |
| S3-8 | `UI.Shared/Authoring/Designer/WorkflowDesignerSession.cs`（1456 行） |
| S3-9 | 双平台 `WorkflowPropertyPanel.cs`（1145 / 747 行）；`WorkflowPropertyInspectorModel.cs`（502 行） |
| S3-10 | `ScriptEngine.WinForms/RoslynScriptEditorControl.cs`（1711 行）、`RoslynScriptProjectEditorControl.cs`（1189 行） |
| S4-1 | `DP.WorkFlow.sln`（61 条目）；`samples/` 与 `samples/Legacy/` |
| S4-2 | 各 `*.csproj` 的 `TargetFramework(s)`；`#if NET48/NETFRAMEWORK` 分支 |
| S4-3 | `Directory.Build.props:2-9` |
| S4-4 | `docs/progress.md`（45KB）；`docs/decisions/`（16 篇）；`docs/nodes/`（10 篇） |

---

## 9. 与既有文档的关系

本评审**不替代** `docs/node-platform-review-backlog.md`，而是为其提供代码侧独立取证：

- `NP-01`（冻结与不可变计划）→ 本报告 S1-3 确认成立，并补充"测试固化风险行为"这一新证据。
- `NP-02`（配置类型与快照）→ S2-6，补充 `Delegate`/`JsonDocument` 共享与无深度预算两个具体缺陷。
- `NP-03`（插件注册失败与能力使用）→ S2-5 + S3-3。
- `NP-05`（统一运行控制）→ 已确认 `IWorkflowRuntimeHost` 无 `StopAsync`/`DisposeAsync`。
- 本报告新增（既有清单未覆盖）：S1-1/S1-2/S2-1/S2-2/S2-3 的依赖边界问题、S1-4 的快照热路径问题、S3-5 的节点类型爆炸、S3-7 的双平台重复度、S4-3 的不可复现构建。
