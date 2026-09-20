# AR-01 修复方案：运行准备与资源作用域

状态：**修复方案（未实施）**。本文只给方案，不改代码——原因见 §7。

---

## 1. 修复原则

一句话：

> **"清空相册"只能由"真正开新一轮的人"触发；本轮内部起的子流程只能校验，不能清。**

对应到代码：`PrepareAsync` 目前把两件不同的事混在一起了——

| 做的事 | 谁需要 | 举例 |
|---|---|---|
| **校验 / 准备绑定** | **每个作用域都要** | 检查跨子文档节点 ID 重复、能力预检、链式调用下一个准备服务 |
| **释放上一轮资源** | **只有根运行需要** | `Clear()` 清空帧仓 |

这就是根因：**一个接口承担了两种不同作用域的职责，而它没有任何信息可以区分调用者是谁。**

---

## 2. 改动清单

| # | 文件 | 改动 |
|---|---|---|
| 1 | `Abstractions/Execution/IWorkflowRunPreparationService.cs` | `WorkflowRunPreparationContext` 增加作用域字段；修正接口注释 |
| 2 | `Nodes.Vision/Acquisition/WorkflowVisionFrameScope.cs` | `PrepareAsync` 只在根作用域 `Clear()`；修正类注释 |
| 3 | `Runtime/Hosting/WorkflowRuntimeHost.cs` | 传 `Root` |
| 4 | `Runtime/Execution/WorkflowEngine.cs` | 传 `Nested` + 父节点 ID |
| 5 | `Nodes.Process/Recovery/WorkflowWarningHandlerCoordinator.cs` | 传 `Nested` |
| 6 | `Runtime/Execution/WorkflowJointRecoveryGroup.cs` | **暂按 `Nested`**（见 §5） |
| 7 | `tests/Workflow/DP.WorkFlow.Nodes.Vision.Tests/` | 新增 3 个回归测试 |

---

## 3. 逐处改动

### 改动 1：让准备请求携带作用域

`src/Workflow/Kernel/DP.WorkFlow.Abstractions/Execution/IWorkflowRunPreparationService.cs`

```csharp
/// <summary>准备请求所属的运行作用域。</summary>
public enum WorkflowRunScopeKind
{
    /// <summary>根运行：真正开始新一轮，允许释放上一轮运行留下的运行级资源。</summary>
    Root,

    /// <summary>
    /// 根运行内部的嵌套运行（子流程、故障处置、联合参与者）：
    /// 只做校验与绑定准备，不得释放任何既有资源——这些资源仍被根运行引用。
    /// </summary>
    Nested
}

/// <summary>提供运行准备阶段可检查的全部根计划和子计划节点配置快照。</summary>
/// <param name="Nodes">按根计划优先的稳定顺序递归展开的节点配置。</param>
/// <param name="ScopeKind">本次准备请求的作用域；调用者必须明确声明。</param>
/// <param name="ParentNodeId">嵌套运行时承载它的父节点 ID；根运行为空。</param>
public sealed record WorkflowRunPreparationContext(
    IReadOnlyList<IWorkflowNodeModel> Nodes,
    WorkflowRunScopeKind ScopeKind,
    string? ParentNodeId = null);

/// <summary>
/// 由运行宿主在根运行开始时、以及任何嵌套运行开始时调用。
/// 实现者必须依据 <see cref="WorkflowRunPreparationContext.ScopeKind"/> 判断：
/// 只有 <see cref="WorkflowRunScopeKind.Root"/> 才允许释放上一轮资源；
/// <see cref="WorkflowRunScopeKind.Nested"/> 必须保持既有资源不变。
/// </summary>
public interface IWorkflowRunPreparationService
{
    /// <summary>在新建引擎开始执行首节点前准备运行级资源和全部计划节点绑定。</summary>
    ValueTask PrepareAsync(WorkflowRunPreparationContext context, CancellationToken cancellationToken);
}
```

**关键设计：`ScopeKind` 必填，不给默认值。**

全项目只有 4 个调用点，让编译器强制每一处表明意图。这样以后新增调用点时**不可能"忘了想"**——必须显式回答"我这是根运行还是嵌套运行"。

如果出于兼容考虑一定要给默认值，**只能默认 `Nested`**（安全方向）：

- 默认 `Nested` → 忘记传参的调用者不会破坏别人的数据，最坏情况是资源没被及时释放（可控的内存增长）；
- 默认 `Root` → 忘记传参的嵌套调用者会删掉父运行正在用的数据（**数据损坏**）。

### 改动 2：帧仓只在根作用域清空

`src/Workflow/Nodes/DP.WorkFlow.Nodes.Vision/Acquisition/WorkflowVisionFrameScope.cs`

```csharp
/// <inheritdoc/>
public async ValueTask PrepareAsync(WorkflowRunPreparationContext context, CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(context);
    cancellationToken.ThrowIfCancellationRequested();

    // 校验：所有作用域都要做
    var duplicate = context.Nodes.Where(n => n is AnalyzeVisionFrameNodeModel or LoadVisionFileNodeModel
            or LoadVisionFolderNodeModel or CaptureVisionFrameNodeModel)
        .GroupBy(n => n.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
    if (duplicate is not null)
        throw new InvalidOperationException($"新版视觉预览节点ID跨子文档重复：{duplicate.Key}；不能把不同节点的图像合并到同一预览槽。");

    if (_next is not null) await _next.PrepareAsync(context, cancellationToken).ConfigureAwait(false);

    // 释放上一轮：只有根运行才做。
    // 嵌套运行属于本轮内部，仓内帧仍被根运行的节点输出引用，此时清空会让父输出指向已释放的图像。
    if (context.ScopeKind != WorkflowRunScopeKind.Root)
        return;

    lock (_gate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Clear();
    }
}
```

同时修正类注释（原文只描述了根运行场景，是这次误解的来源之一）：

```csharp
/// <summary>
/// 有界运行帧仓和最新预览源。根运行开始时释放上一轮仓内租约，使结果查看窗口结束后自然回收；
/// 根运行内部的嵌套运行只校验、不清空。UI 已 Retain 的快照不受影响。
/// </summary>
```

### 改动 3：根宿主传 `Root`

`WorkflowRuntimeHost.RunCoreAsync`：

```csharp
if (context.Services.GetService(typeof(IWorkflowRunPreparationService)) is IWorkflowRunPreparationService preparation)
    await preparation.PrepareAsync(
        new WorkflowRunPreparationContext(
            EnumerateNodes(plan.Plan).ToArray(),
            WorkflowRunScopeKind.Root),
        cancellationToken).ConfigureAwait(false);
```

这是契约里写的、也是唯一的真正"开新一轮"的位置。

### 改动 4：恢复子流程传 `Nested`

`WorkflowEngine.RunTrackedChildAsync`（当前在 `:538-539`）：

```csharp
if (prepare)
{
    WorkflowRuntimeCapabilityValidator.Validate(boundPlan, childContext.Services);
    if (childContext.Services.GetService(typeof(IWorkflowRunPreparationService)) is IWorkflowRunPreparationService preparation)
        await preparation.PrepareAsync(
            new WorkflowRunPreparationContext(
                EnumerateRecoveryNodes(boundPlan.Plan).ToArray(),
                WorkflowRunScopeKind.Nested,
                parentNode.Id),
            cancellationToken).ConfigureAwait(false);
}
```

这里 `parentNode` 本来就在作用域内，直接可用。

### 改动 5：处置协调器兜底路径传 `Nested`

`WorkflowWarningHandlerCoordinator.RecoverAsync`（当前在 `:53-55`）：

```csharp
WorkflowRuntimeCapabilityValidator.Validate(_boundPlan, context.Services);
if (context.Services.GetService(typeof(IWorkflowRunPreparationService)) is IWorkflowRunPreparationService preparation)
    await preparation.PrepareAsync(
        new WorkflowRunPreparationContext(
            EnumerateNodes(_plan).ToArray(),
            WorkflowRunScopeKind.Nested),
        cancellationToken).ConfigureAwait(false);
```

这是故障处理内部起的子流程，明确属于本轮内部。

---

## 4. 回归测试

新增 `tests/Workflow/DP.WorkFlow.Nodes.Vision.Tests/WorkflowVisionFrameScopeRunScopeTests.cs`。

测试要点：`ImageBuffer.Dispose()` 会把**当前句柄**的 `_storage` 置空，所以被清掉的句柄再 `Retain()` 必然抛 `ObjectDisposedException`——断言可以写得很直接，不需要关心引用计数。

```csharp
using DP.Vision;

namespace DP.WorkFlow.Tests;

/// <summary>AR-01 回归：嵌套运行不得释放根运行的帧。</summary>
public sealed class WorkflowVisionFrameScopeRunScopeTests
{
    private static ImageFrame MakeRetainedSource(out ImageFrame nodeOutput, WorkflowVisionFrameScope scope)
    {
        var source = VisionImage.CopyFrom(new ImageInfo(1, 1, EPixelLayout.Gray8), new byte[] { 7 });
        var local = new ImageFrame("f1", source);
        nodeOutput = scope.Retain(local);   // 模拟 LoadVisionFileNode：交给仓，仓返回句柄
        local.Dispose();
        source.Dispose();
        return nodeOutput;
    }

    private static WorkflowRunPreparationContext Context(WorkflowRunScopeKind kind) =>
        new(Array.Empty<IWorkflowNodeModel>(), kind);

    [Fact]
    public async Task 嵌套运行准备不得释放根运行已保留的帧()
    {
        using var scope = new WorkflowVisionFrameScope();
        await scope.PrepareAsync(Context(WorkflowRunScopeKind.Root), CancellationToken.None);

        var nodeOutput = MakeRetainedSource(out _, scope);

        // 本轮内部起嵌套运行（处置子流程）
        await scope.PrepareAsync(Context(WorkflowRunScopeKind.Nested), CancellationToken.None);

        // 修复前：这里抛 ObjectDisposedException（父运行的节点输出已失效）
        using var lease = nodeOutput.Retain();
        Assert.Equal("f1", lease.FrameId);
    }

    [Fact]
    public async Task 根运行准备仍然释放上一轮保留的帧()
    {
        using var scope = new WorkflowVisionFrameScope();
        var previous = MakeRetainedSource(out _, scope);

        await scope.PrepareAsync(Context(WorkflowRunScopeKind.Root), CancellationToken.None);

        // 清理语义不能被削弱
        Assert.Throws<ObjectDisposedException>(() => previous.Retain());
    }

    [Fact]
    public async Task 嵌套准备仍然执行跨子文档节点ID重复校验()
    {
        using var scope = new WorkflowVisionFrameScope();
        var nodes = new IWorkflowNodeModel[]
        {
            new LoadVisionFileNodeModel { Id = "same" },
            new LoadVisionFileNodeModel { Id = "same" }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.PrepareAsync(
                new WorkflowRunPreparationContext(nodes, WorkflowRunScopeKind.Nested),
                CancellationToken.None).AsTask());
    }
}
```

三个测试分别锁住三件事：**嵌套不清**（修的就是它）、**根仍然清**（别把清理改没了）、**嵌套仍然校验**（别用"跳过准备"来绕过问题）。

---

## 5. 联合恢复那一处为什么暂不定

`WorkflowJointRecoveryGroup.RunCoreAsync`（`:149`）为**每个参与者**调用 `PrepareAsync`。

它和其他三处不同：其他三处一眼能看出是"本轮内部"，而联合恢复的"一轮"需要先定义清楚——**它是开启一轮新的生产协作，还是在协调已经在跑的多个运行实例？**

建议：

1. **先按 `Nested` 处理**（安全方向：不清空任何东西，不会损坏数据）；
2. 补一个联合恢复的帧生命周期测试，观察参与者之间的帧是否互相影响；
3. 确认后再决定。如果确实需要干净起点，**应该由联合组在"新一轮协作开始"这一个点上显式触发一次，而不是在每个参与者的准备里各触发一次**——否则参与者 A 的准备可能清掉参与者 B 刚准备的东西。

---

## 6. 分步实施建议

### 阶段 1（本文方案，改动小、可立即做）

改动 1–6 + 测试。**4 个生产文件 + 1 个测试文件**，行为立即正确，不重构任何结构。

风险点：`WorkflowRunPreparationContext` 是公开类型，`ScopeKind` 必填会**编译期击穿所有外部实现者**。本项目尚未发布，建议直接改；若要平滑，用默认值 `Nested`。

### 阶段 2（语义修正，更彻底）

把两种职责拆成**两个接口**，让误用从"运行期 bug"变成"编译期错误"：

```csharp
/// <summary>只做校验与绑定准备；任何作用域都可调用，不得释放既有资源。</summary>
public interface IWorkflowRunPreparationService
{
    ValueTask PrepareAsync(WorkflowRunPreparationContext context, CancellationToken cancellationToken);
}

/// <summary>释放运行级资源；只由运行所有者在确认上一轮已彻底结束时调用。</summary>
public interface IWorkflowRunResourceOwner
{
    ValueTask ReleasePreviousRunAsync(CancellationToken cancellationToken);
}
```

`WorkflowVisionFrameScope` 同时实现两个：`PrepareAsync` 只校验，`ReleasePreviousRunAsync` 才 `Clear()`。

根宿主调两个；嵌套调用点**拿不到** `IWorkflowRunResourceOwner`（类型不同，编译器拦住）。

这比阶段 1 更根本：阶段 1 靠"调用者自觉传对枚举"，阶段 2 靠"调用者根本调不到"。

### 阶段 3（AR-01 的完整方向）

RunScope 层级 + 所有权令牌：子作用域拥有自己的资源容器，父资源只由父释放。这是 AR-01 建议的终态，改动面大，应放在阶段 A 之后。

---

## 7. 为什么当初不动代码（已完成，保留作历史记录）

> 本节记录的是实施**前置条件**的推理。该条件已由 AR-24 满足，阶段 1 随后实施完毕。

`DP.WorkFlow` 当初**不在版本控制下**（无 `.git`，见 AR-24）。本次改动跨 4 个程序集（Abstractions / Runtime / Nodes.Vision / Nodes.Process），且 `ScopeKind` 必填会击穿所有调用点——**在没有版本控制、无法回退、无法 diff 的情况下实施结构性改动，风险不可接受**。

建议顺序（已按此执行）：

```text
AR-24 纳入版本控制  →  实施本文阶段 1  →  跑全量测试  →  再评估阶段 2
```

---

## 8. 验收标准

沿用 AR-01 的验收，并补上可判定条件：

| # | 验收项 | 判定方式 |
|---|---|---|
| 1 | 嵌套准备不释放父资源 | `嵌套运行准备不得释放根运行已保留的帧` 通过 |
| 2 | 根准备仍然释放上一轮资源 | `根运行准备仍然释放上一轮保留的帧` 通过 |
| 3 | 嵌套准备仍然执行校验 | `嵌套准备仍然执行跨子文档节点ID重复校验` 通过 |
| 4 | 生产路径端到端 | 采图 → 主操作故障 → 执行处置 → **继续消费原图成功**（当前会抛 `ObjectDisposedException`） |
| 5 | 结束后租约按所有权恰好释放 | 运行结束后帧仓内租约数为 0；UI 已 Retain 的快照仍可显示 |
| 6 | 全量回归 | `tools/Test-DPWorkFlow.ps1` 全绿；新增测试在修复前**必须红**（否则测试没测到点上） |

第 6 条是硬要求：**先在未修复的代码上跑，确认新增测试失败**，再实施修复。否则无法证明测试真的覆盖了这个缺陷。

---

## 9. 实施记录（2026-09-20）

commit `16f1c45`。改动与 §3 逐条一致，另加 1 个测试。

| 文件 | 改动 |
|---|---|
| `Abstractions/Execution/IWorkflowRunPreparationService.cs` | 新增 `WorkflowRunScopeKind`；`WorkflowRunPreparationContext` 增加必填 `ScopeKind` + `ParentNodeId = null`；修正接口注释 |
| `Nodes.Vision/Acquisition/WorkflowVisionFrameScope.cs` | 校验与 `_next` 链式调用对所有作用域照常；**仅 `Root` 才 `Clear()`**；修正类注释 |
| `Runtime/Hosting/WorkflowRuntimeHost.cs` | `Root` |
| `Runtime/Execution/WorkflowEngine.cs` | `Nested` + `parentNode.Id` |
| `Nodes.Process/Recovery/WorkflowWarningHandlerCoordinator.cs` | `Nested` |
| `Runtime/Execution/WorkflowJointRecoveryGroup.cs` | 暂 `Nested`（附注释说明为何待定） |
| `tests/.../DP.WorkFlow.Nodes.Vision.Tests/WorkflowVisionFrameScopeRunScopeTests.cs` | **新增** 4 个测试 |
| `tests/.../DP.WorkFlow.Runtime.Tests/WorkflowRuntimeHostTests.cs` | `TestRunPreparation` 补断言 `ScopeKind == Root` |
| `tests/.../DP.WorkFlow.UI.Windows.Tests/NewVisionCompletionTests.cs` | 3 处构造点显式声明 `Root` |

测试比 §4 多 1 个：**嵌套准备同样链式调用后续准备服务**——锁住「嵌套只是不清资源，不是跳过准备」。

### 红→绿证据（验收第 6 条）

修复前运行新测试：

```text
[xUnit.net] DP.WorkFlow.Tests.WorkflowVisionFrameScopeRunScopeTests.嵌套运行准备不得释放根运行已保留的帧 [FAIL]
  System.ObjectDisposedException : Cannot access a disposed object.
  Object name: 'ImageBuffer'.
     at DP.Vision.ImageBuffer.Alive() ... ImageBuffer.cs:line 62
     at DP.Vision.ImageBuffer.Retain() ... ImageBuffer.cs:line 54
     at DP.Vision.ImageFrame.Retain() ... ImageFrame.cs:line 27
失败: 1，通过: 3，总计: 4
```

修复后：

```text
已通过! - 失败: 0，通过: 4，总计: 4
```

### 全量回归

12 个测试目标 **729 个测试全部通过，0 失败**：
Vision 8 / ScriptEngine net8.0 51 + net48 50 / Core 45 / Composite 7 / Motion 12 /
Process 56 / Standard 48 / Persistence 10 / Runtime 26 / UI.Shared 63 / UI.Windows 353。

Debug 与 Release 构建均 **0 警告 0 错误**。

### 本次未处理的同源问题

`WorkflowVisionAcquisitionSession.PrepareAsync` 与本次修复**同源**：它无条件用新冻结清单替换
`_sequences`（游标归零）。嵌套运行时会让文件夹序列中途重头读。本方案 §2 未列该文件，故未动；
建议在阶段 2 一并处理，或单独立一条 AR。

### 环境前置（重要）

本机 `dotnet restore` 因 `Environment.GetFolderPath(CommonApplicationData)` 返回 null 而必然失败。
本次验证使用 `ProgramData` 环境变量 + SDK 10 + `--no-restore` / `--no-build` 完成，
未修改 `global.json` 与 `tools/Test-DPWorkFlow.ps1`。`global.json` 当前固定 `9.0.308`，
在本机无法完成任何还原，需用户决策是否提升到 `10.0.302`。
