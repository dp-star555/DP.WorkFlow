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

| # | 验收项 | 判定方式 | 状态 |
|---|---|---|---|
| 1 | 嵌套准备不释放父资源 | `嵌套运行准备不得释放根运行已保留的帧` 通过 | ✅ |
| 2 | 根准备仍然释放上一轮资源 | `根运行准备仍然释放上一轮保留的帧` 通过 | ✅ |
| 3 | 嵌套准备仍然执行校验 | `嵌套准备仍然执行跨子文档节点ID重复校验` 通过 | ✅ |
| 4 | 生产路径端到端 | 采图 → 主操作故障 → 执行处置 → **继续消费原图成功** | ✅ 见 §10 |
| 5 | 结束后租约按所有权恰好释放 | 仓释放后 UI 已 Retain 的快照仍可显示；最后一个租约释放时底层存储归池 | ✅ 见 §10 |
| 6 | 全量回归 | `tools/Test-DPWorkFlow.ps1` 全绿；新增测试在修复前**必须红**（否则测试没测到点上） | ✅ 见 §9 |
| 7 | **调用点声明的作用域**（补测时发现） | 覆盖 AR-01 的破坏点（`WorkflowEngine` 嵌套路径）；另 2 处仍无声明级覆盖 | ⚠️ 部分覆盖，见 §10 |

第 6 条是硬要求：**先在未修复的代码上跑，确认新增测试失败**，再实施修复。否则无法证明测试真的覆盖了这个缺陷。

第 7 条是本轮补测时发现的覆盖盲区：阶段 1 原有 4 个测试直接构造 `WorkflowRunPreparationContext`，
**根本不经过调用点**，因此把 `WorkflowEngine` 的 `Nested` 改回 `Root` 时它们仍然全绿。
已补 1 个测试覆盖该破坏点；其余调用点的覆盖情况见 §10。

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

> **2026-09-20 更正**：下列"必然失败"的结论已被推翻。真实根因是**系统环境变量缺失**
> （NuGet 在 CoreCLR 下直接读 `ProgramFiles(x86)` / `ProgramFiles` / `APPDATA`，变量不存在则返回 null），
> **与 SDK 版本、与仓库配置都无关**。补齐 `APPDATA` + `ProgramFiles(x86)` 后，
> 在仓库目录内用 `global.json` 选中的 SDK 9.0.316 带还原完整构建 → **0 警告 0 错误**。
> 详见 `architecture-final-summary.md` 的 AR-24 一节与 §10.4 第 6 问。

本机 `dotnet restore` 曾因 `Environment.GetFolderPath(CommonApplicationData)` 返回 null 而失败
（**该归因不正确**，实际是 NuGet 自己读环境变量）。当时验证使用
`ProgramData` 环境变量 + SDK 10 + `--no-restore` / `--no-build` 完成，
未修改 `global.json` 与 `tools/Test-DPWorkFlow.ps1`。
**修复后 `global.json` 无需改动**，`9.0.308` 在仓库内可正常工作。

---

## 10. 验收补齐记录（2026-09-20 续）

阶段 1 交付时只有 4 个"给定 ScopeKind 时帧仓行为"的测试。本轮补齐验收第 4、5 条，
并封堵补测时发现的第 7 条覆盖盲区。

### 新增测试

| 测试 | 文件 | 覆盖 |
|---|---|---|
| `处置子流程运行后父运行仍能消费原图` | `Nodes.Vision.Tests/WorkflowVisionFrameScopeRecoveryEndToEndTests.cs` | 验收 4 |
| `运行结束后仓仍持有租约下一轮根运行开始时恰好归零` | `Nodes.Vision.Tests/WorkflowVisionFrameScopeLeaseOwnershipTests.cs` | 验收 5（仓侧） |
| `仓释放后UI快照仍可显示且最后一个租约释放时才归池` | 同上 | 验收 5（UI 侧） |
| `恢复子流程的准备请求声明嵌套作用域并携带父节点ID` | `Runtime.Tests/WorkflowRunPreparationScopeDeclarationTests.cs` | 验收 7（仅 `WorkflowEngine` 调用点） |

### 验收 4：端到端

走真实引擎与真实 `LoadVisionFileNode`（含帧仓 `Retain` / `Publish`）：
`LoadVisionFileNode("file") → FaultNode("fault") → ConsumeFrameNode("consume", Frame ← file)`。
处置协调器走 `IWorkflowRecoverySubflowContext.RunRecoverySubflowAsync`，即生产唯一入口。

有效性验证：把帧仓守卫改回无条件清空 → 红灯。**注意第一版红灯被掩盖了**：
失败信息是 `节点 consume 未声明 Idempotent 或 ResumeAware，禁止重试`——
二次恢复被重试安全守卫拦住，真正的 `ObjectDisposedException` 没露出来。
修正办法是让消费节点实现 `IWorkflowRetrySafetyNode`，并在 handler 里捕获
`ObjectDisposedException` 单独记录。修正后红灯信息为：

```text
Assert.Null() Failure: Value is not null
Actual:   System.ObjectDisposedException: Cannot access a disposed object.
Object name: 'ImageBuffer'.
   at DP.Vision.ImageBuffer.Alive() ... ImageBuffer.cs:line 62
   at DP.Vision.ImageBuffer.CopyTo(...) ... ImageBuffer.cs:line 90
   at ...ConsumeFrameHandler.ExecuteAsync(...) EndToEndTests.cs:line 169
```

**教训**：让测试在红灯时报出真正的原因，否则红灯只是"某处失败"，等于没测到点上。

### 验收 5：租约按所有权恰好释放

帧仓不暴露租约计数，用**容量为 1 的 `FrameBufferPool` 当探针**：
采集时借走唯一槽位，只要还有任何一个租约没释放，槽位就回不来，`TryRent` 必然失败。
探针用完立即归还槽位，可重复调用。于是"恰好释放"成为确定性布尔断言，不依赖 GC 与计时。

两个变异验证：

| 变异 | 实测红灯 |
|---|---|
| `Clear()` 漏掉 `_previews` 释放 | `根运行开始后上一轮仓内租约必须全部释放。` / `全部租约释放后底层存储必须归池。` |
| `Capture()` 不 `Retain` | `ObjectDisposedException`，落在读快照像素那一行 |

**措辞修正**：原文写"运行结束后帧仓内租约数为 0"，与设计不符。设计是**本轮结束后仓仍持有租约**
（供结果查看窗口使用），**下一轮根运行开始时才归零**。测试按设计语义断言。

**性质说明**：这两条是**特征锁定**测试，锁定"释放语义不能被过度削弱"
（例如为了修 AR-01 干脆删掉 `Clear()`），不揭示 AR-01 缺陷本身。

### 验收 7：调用点声明（补测时发现的盲区）

原有 4 个测试直接构造 `WorkflowRunPreparationContext`，**不经过调用点**。
把 `WorkflowEngine` 的 `Nested` 改回 `Root`，4 个测试仍然全绿。

新增测试走真实恢复路径：`SubflowRecoveryCoordinator : IWorkflowFaultRecoveryCoordinator`
在 `RecoverAsync` 里调用 `IWorkflowRecoverySubflowContext.RunRecoverySubflowAsync`，
用一个 `RecordingPreparation : IWorkflowRunPreparationService` 记录实际收到的上下文，
断言 `ScopeKind == Nested` 且 `ParentNodeId == 故障节点 Id`。

有效性验证：把 `WorkflowEngine` 改回 `Root` → `Expected: Nested / Actual: Root`。

**覆盖范围要如实说明**：新增测试只锁定了 `WorkflowEngine.cs:539` 这**一个**调用点
（即 AR-01 的破坏点本身）。4 个调用点当前的声明级覆盖如下：

| 调用点 | 声明 | 声明级覆盖 |
|---|---|---|
| `WorkflowRuntimeHost.cs:283` | `Root` | ✅ `WorkflowRuntimeHostTests.TestRunPreparation` 断言 `ScopeKind == Root` |
| `WorkflowEngine.cs:539` | `Nested` + `parentNode.Id` | ✅ 本轮新增（破坏点） |
| `WorkflowWarningHandlerCoordinator.cs:55` | `Nested` | ❌ 无 |
| `WorkflowJointRecoveryGroup.cs:150` | 暂 `Nested`（语义待定） | ❌ 无 |

后两处是**残留缺口**。它们是否也需要声明级覆盖，取决于 §5 里"联合恢复那一处为什么暂不定"
的结论——在"一轮"语义定下来之前，为它们写断言会把待定行为固化成契约。
建议与阶段 2 一并处理。
