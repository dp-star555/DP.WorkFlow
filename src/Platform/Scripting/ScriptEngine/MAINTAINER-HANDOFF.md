# ScriptEngine 完整技术说明与 Astra 接手指南

> 本文是当前 ScriptEngine 实现的维护者基线，面向后续思考模型、开发者和代码审查者。
> 它记录模块范围、接口、实现路径、不变量、验证命令、已知限制和继续演进顺序。
> 面向使用者的简明说明仍见同目录的 [README.md](README.md)。

## 1. 当前结论

ScriptEngine 已经是一个支持工业桌面宿主的 C# 动态扩展模块，而不是简单的 `CSharpScript.EvaluateAsync` 包装：

- 脚本输入是一个或多个**普通 C# 源文件**，不是 Roslyn Script 语法。
- 可编译、诊断、执行、按稳定脚本标识热替换，并管理显式 DLL 和私有依赖。
- 核心支持 `net8.0` 和 `.NET Framework 4.8`。
- WinForms/WPF 编辑器支持语义高亮、诊断、折叠、智能缩进、代码片段、完整 Roslyn 补全和项目级编辑。
- 不可信、可能崩溃或死循环的脚本可以放入独立 Worker；常驻 Worker 池使用命名管道复用进程。
- Roslyn Workspaces/Features 保持可选，不进入核心运行包。
- WPF 通过 `WindowsFormsHost` 复用 WinForms Scintilla 实现，两套桌面界面保持同一编辑行为。

当前没有阻塞项。完整 Release 构建和现有 ScriptEngine 测试矩阵均已通过，详见“验证基线”。

---

## 2. 设计边界与不可破坏的不变量

### 2.1 模块范围

ScriptEngine 负责：

1. 设计期 C# 语言分析。
2. 普通 C# 程序编译和入口契约验证。
3. 受信任进程内执行和热版本替换。
4. 多文件项目联合编译。
5. 显式 DLL、同目录私有依赖、引用锁定和架构检查。
6. 独立 Worker 执行、硬超时、资源限制和进程树回收。
7. WinForms/WPF 编辑适配器。
8. 可选 Workspaces 语义编辑和完整补全。

ScriptEngine **不负责**：

- 工作流调度、节点生命周期或流程持久化。
- 用户、角色、ACL、低权限账号创建或业务权限管理。
- 把静态安全策略包装成安全沙箱。
- 在 net48 默认 AppDomain 中强行卸载已经加载的动态程序集。
- 直接提供 NuGet 包解析器、许可证/漏洞平台或调试器。

### 2.2 核心不变量

后续修改必须保持以下行为：

1. **编译不加载程序集**：`CompileProgramAsync` / `CompileProjectAsync` 只生成 PE/PDB Artifact。
2. **入口唯一**：源码中必须恰好有一个实现 `ICSharpProgram` 的非抽象类型，并具有公共无参数构造函数。
3. **每次执行新建实例**：热版本复用程序集，但不复用 `ICSharpProgram` 对象。
4. **替换必须原子**：新修订成功编译和加载后才替换旧热版本；无效新源码不能回退执行旧源码。
5. **活动旧版本可完成**：正在执行的旧修订不被中途卸载，活动计数归零后再清理。
6. **所有长期缓存有界**：分析、Emit、运行槽、Workspace 和元数据缓存不得无上限增长。
7. **硬超时必须依赖 Worker**：进程内 `CancellationToken` 仅是协作式取消。
8. **Restricted 不是沙箱**：它只产生静态语义诊断，不能代替进程/账号/容器隔离。
9. **Workspaces 保持可选**：`ScriptEngine` 核心不得引用 `Microsoft.CodeAnalysis.*.Workspaces` 或 Features。
10. **补全文档不得使用 Scintilla CallTip**：补全窗口和说明必须独立；CallTip 只用于签名和悬浮信息。
11. **WPF 复用同一 WinForms 编辑器**：除非明确重新作出架构决策，不维护第二套编辑实现。
12. **项目级异步编辑必须校验源码修订**：不得覆盖重命名或 Code Fix 计算期间产生的新用户修改。
13. **输入行为必须原子撤销**：补全附带 using、配对字符、片段和项目编辑应保持预期的单步撤销语义。
14. **保留原文换行风格**：输入行为和片段不能无故把 CRLF 改成 LF，反之亦然。

---

## 3. 模块和依赖结构

```text
宿主 / 工作流 Studio / Demo
            │
            ├── ScriptEngine.WinForms ── Scintilla.NET
            │          │
            │          └── ScriptEngine（核心）
            │
            ├── ScriptEngine.Wpf ── WindowsFormsHost ── ScriptEngine.WinForms
            │
            ├── ScriptEngine.Workspaces（可选）
            │          ├── Microsoft.CodeAnalysis.CSharp.Workspaces
            │          ├── Microsoft.CodeAnalysis.CSharp.Features
            │          └── ScriptEngine（只通过核心契约接入编辑器）
            │
            └── ScriptEngine.Worker ── ScriptEngine（独立进程）
```

### 3.1 外部 seam

| Seam | 接口/入口 | 主要 Adapter |
|---|---|---|
| 编译、运行和轻量语言能力 | `RoslynScriptService` | 核心 Roslyn 实现 |
| 脚本执行契约 | `ICSharpProgram` | 用户脚本类型 |
| 完整补全 | `IRoslynScriptCompletionProvider` | `RoslynScriptWorkspaceService` |
| 项目级编辑 | `IRoslynScriptProjectEditingService` | `RoslynScriptWorkspaceService` |
| 单文件桌面编辑 | `RoslynScriptEditorControl` | WinForms Scintilla；WPF 包装 |
| 多文件桌面编辑 | `RoslynScriptProjectEditorControl` | WinForms 标签编辑器；WPF 包装 |
| 隔离执行 | `RoslynScriptWorkerClient` / `RoslynScriptWorkerPool` | `ScriptEngine.Worker` 进程 |

`RoslynScriptService` 是核心深模块的主要接口。调用方不应该绕过它直接拼装 Roslyn Compilation、加载上下文或热版本注册表。

### 3.2 目标框架与固定依赖

| 项目 | TargetFrameworks | 关键依赖 |
|---|---|---|
| `ScriptEngine` | `net48;net8.0` | `Microsoft.CodeAnalysis.CSharp 4.11.0` |
| `ScriptEngine.Workspaces` | `net48;net8.0` | CSharp.Workspaces/Features 4.11.0 |
| `ScriptEngine.WinForms` | `net48;net8.0-windows` | Scintilla.NET 5.3.2.9 |
| `ScriptEngine.Wpf` | `net48;net8.0-windows` | WinForms 编辑器 + WindowsFormsHost |
| `ScriptEngine.Worker` | `net48;net8.0` | ScriptEngine 核心 |
| Demo | `net8.0-windows` | WinForms + Workspaces |

版本统一配置在：

- `DP.WorkFlow/Directory.Packages.props`

第三方声明位于：

- `ScriptEngine/THIRD-PARTY-NOTICES.md`

---

## 4. 代码目录地图

### 4.1 核心

| 文件 | 职责 |
|---|---|
| `RoslynScriptContracts.cs` | 环境、项目、诊断、补全、Artifact、执行结果和入口契约 |
| `RoslynScriptService.cs` | 核心门面；编译、执行、诊断、补全、导航、高亮、格式化和缓存统计 |
| `Compilation/RoslynScriptCompiler.cs` | Emit、唯一入口验证、编译结果缓存 |
| `Compilation/ScriptEnvironmentSnapshot.cs` | 平台/外部引用快照、内容哈希、Revision、元数据复用、单文件嵌入元数据 |
| `Compilation/NetFrameworkCompatibilitySource.cs` | net48 的 `IsExternalInit`、required-member 等兼容标记源码 |
| `Language/RoslynScriptAnalysisProvider.cs` | 有界分析快照、Compilation/SemanticModel 共享和增量语法树 |
| `Language/RoslynScriptLanguageService.cs` | 诊断、轻量补全、签名、快速信息、定义和高亮 |
| `Language/RoslynScriptEditingService.cs` | 折叠、缩进和轻量文本 Code Action |
| `Language/RoslynScriptCompletionMatcher.cs` | 前缀/包含/子序列模糊匹配 |
| `Language/RoslynScriptSecurityPolicyEvaluator.cs` | Restricted 静态语义检查 |
| `Runtime/ScriptRuntimeRegistry.cs` | ScriptId 槽、热版本 lease、并发活动计数和卸载 |
| `References/RoslynScriptReferenceInspector.cs` | 不加载 DLL 的 PE 元数据检查 |
| `References/RoslynScriptReferenceLock.cs` | 确定性 XML 引用锁定与验证 |
| `Isolation/RoslynScriptWorkerClient.cs` | 一次性 Worker 客户端 |
| `Isolation/RoslynScriptWorkerPool.cs` | 命名管道常驻 Worker 池 |
| `Isolation/WindowsProcessJob.cs` | Windows Job Object 资源和进程树限制 |
| `Workspace/RoslynScriptWorkspaceContracts.cs` | 可选 Workspaces seam 的核心契约 |

### 4.2 编辑器和可选模块

| 文件/目录 | 职责 |
|---|---|
| `ScriptEngine.WinForms/RoslynScriptEditorControl.cs` | 单文件编辑器编排、异步分析和命令 |
| `ScriptEngine.WinForms/ScriptEditorInputBehavior.cs` | 片段、换行、配对、删除、缩进触发和补全提交决策 |
| `ScriptEngine.WinForms/ScriptCompletionPopup.cs` | 虚拟化多列补全窗口和文档侧栏 |
| `ScriptEngine.WinForms/DiagnosticOverviewBar.cs` | 右侧诊断概览和点击导航 |
| `ScriptEngine.WinForms/RoslynScriptProjectEditorControl.cs` | 标签式多文件项目编辑和项目级历史 |
| `ScriptEngine.WinForms/ScriptWorkspaceDialogs.cs` | 重命名、Code Fix 和差异预览窗口 |
| `ScriptEngine.Workspaces/RoslynScriptWorkspaceService.cs` | CompletionService、重命名、引用、Code Fix、常驻 Workspace 缓存 |
| `ScriptEngine.Wpf/*` | WinForms 编辑器的 WPF 包装和引用管理窗口 |
| `ScriptEngine.Worker/Program.cs` | 一次性请求和命名管道池协议入口 |

### 4.3 宿主集成和验证

| 路径 | 用途 |
|---|---|
| `samples/ScriptEngine.WinForms.Demo/` | 可运行完整编辑/编译/执行 Demo |
| `tests/Platform/ScriptEngine.Tests/` | 核心、引用、运行时和 Worker 测试 |
| `tests/Platform/ScriptEngine.Workspaces.Tests/` | CompletionService、缓存和项目语义编辑测试 |
| `tests/Platform/ScriptEngine.Windows.Tests/` | WinForms/WPF、输入和真实补全窗口测试 |
| `tools/run-script-engine-editor-matrix.ps1` | net8/net48/x86/压力/单文件验证矩阵 |
| `src/Workflow/Studio/DP.WorkFlow.UI.Shared/Editors/WorkflowCSharpScriptEditorModel.cs` | 工作流环境和脚本模板 |
| `src/Workflow/Studio/DP.WorkFlow.UI.WinForms/Editors/WorkflowCSharpScriptEditorControl.cs` | 工作流 WinForms Adapter |
| `src/Workflow/Studio/DP.WorkFlow.UI.Wpf/Editors/WorkflowCSharpScriptEditorControl.cs` | 工作流 WPF Adapter |

---

## 5. 脚本源码契约

ScriptEngine 编译的是 `SourceCodeKind.Regular` 的普通 C# DLL。每个可执行项目必须满足：

1. 至少一个非空 `.cs` 文件。
2. 恰好一个非抽象类实现 `ICSharpProgram`。
3. 入口类具有公共无参数构造函数。
4. 入口实现：

```csharp
using ScriptEngine;

public sealed class Program : ICSharpProgram
{
    public async ValueTask<object?> ExecuteAsync(
        CSharpProgramContext context,
        CancellationToken cancellationToken)
    {
        var host = context.GetHostContext<MyHostContext>();
        context.Output.WriteLine("running");
        await Task.Delay(10, cancellationToken);
        return host.Value;
    }
}
```

编译选项：

- `LanguageVersion.Latest`
- `OutputKind.DynamicallyLinkedLibrary`
- `OptimizationLevel.Release`
- nullable enabled
- deterministic emit
- unsafe disabled
- portable PDB
- `CS1998` 被抑制，允许模板保留 `async` 而暂时没有 `await`

### 5.1 多文件名称规则

`RoslynScriptProject.SourceFiles`：

- 至少一个文件。
- 文件名忽略大小写后必须唯一。
- 必须以 `.cs` 结尾。
- 不允许绝对路径和 `..`。
- `.g.cs` 保留给引擎生成源码。
- 内部统一把 `\` 规范为 `/`，并按文件名确定性排序。

### 5.2 生成源码和诊断来源

引擎会生成：

- `ScriptEngine.Imports.g.cs`：环境 Imports 转换成 global using。
- net48 必要时生成兼容标记类型源码。

`RoslynScriptDiagnostic.Origin`：

- `UserSource`：可映射回用户文件。
- `GeneratedSource`：导入或兼容源码。
- `Infrastructure`：引用、环境或入口契约错误。

编辑器只应把 `UserSource` 诊断映射为用户文件波浪线。

---

## 6. 核心调用接口

### 6.1 RoslynScriptEnvironment

| 属性 | 语义 |
|---|---|
| `Imports` | 自动 global using，默认 System 常用命名空间 |
| `References` | 已加载程序集兼容入口；新代码优先使用 `ReferencePaths` |
| `ReferencePaths` | 编译和运行需要的显式 DLL 路径 |
| `ResolveReferenceDependencies` | 默认递归发现显式 DLL 同目录私有托管依赖 |
| `ValidateReferenceArchitecture` | 默认拒绝和当前进程不兼容的 x86/x64/ARM DLL |
| `SecurityPolicy` | `Trusted` 或静态 `Restricted` |
| `Services` | 只在受信任进程内模式传给脚本 |
| `Output` | 进程内标准输出目标 |
| `ContextItems` | 编辑器额外宿主补全项 |

建议复用稳定的环境对象。含 `ReferencePaths` 时，每次创建快照都会重新检查文件内容，以识别原位替换 DLL；无外部路径的环境会通过弱引用缓存复用，同时逐项检查集合内容。

### 6.2 RoslynScriptService 主要方法

| 能力 | 方法 |
|---|---|
| 单文件诊断 | `GetDiagnostics` / `GetDiagnosticsAsync` |
| 多文件诊断 | `GetProjectDiagnosticsAsync` |
| 单文件编译 | `CompileProgramAsync` |
| 多文件编译 | `CompileProjectAsync` |
| 稳定脚本执行 | `ExecuteAsync(scriptId, source, ...)` |
| 匿名执行 | `ExecuteAsync(source, ...)`，只适合临时工具 |
| 多文件执行 | `ExecuteProjectAsync(scriptId, project, ...)` |
| 移除热版本 | `RemoveProgram(scriptId)` |
| 轻量补全 | `GetCompletionsAsync` / `GetProjectCompletionsAsync` |
| 签名/快速信息 | `GetSignatureHelpAsync` / `GetQuickInfoAsync` |
| 定义导航 | `GetDefinitionAsync` / `GetProjectDefinitionAsync` |
| 高亮 | `GetSyntacticHighlightSpans` / `GetHighlightSpansAsync` |
| 折叠/缩进 | `GetFoldingSpans` / `GetIndentation` |
| 轻量修复 | `GetCodeActions` / `ApplyCodeAction` |
| 文本工具 | `FormatSource` / `GetUsings` / `SetUsings` |
| 缓存观测 | `GetCacheStatistics()` |

典型调用：

```csharp
using var service = new RoslynScriptService();
var environment = new RoslynScriptEnvironment
{
    ReferencePaths = new[] { @"lib\Customer.Algorithm.dll" },
    Output = Console.Out
};

var compilation = await service.CompileProgramAsync(source, environment, cancellationToken);
if (!compilation.Success)
{
    foreach (var diagnostic in compilation.Diagnostics)
        Console.WriteLine($"{diagnostic.Id}: {diagnostic.Message}");
    return;
}

var execution = await service.ExecuteAsync(
    "workflow:node-42", // 稳定逻辑 ScriptId
    source,
    hostContext,
    environment,
    cancellationToken);
```

### 6.3 资源所有权

- 自己创建的 `RoslynScriptService`、`RoslynScriptWorkspaceService` 和 `RoslynScriptWorkerPool` 必须释放。
- `RoslynScriptService.Shared` 是进程级共享实例，不由普通调用方释放。
- 单文件编辑器默认拥有自己创建的 ScriptService；设置外部 `ScriptService` 后由宿主管理。
- 项目编辑器自动发现并创建的 Workspaces 实现由项目编辑器释放；显式赋值的实现由宿主管理。

---

## 7. Revision、缓存和热运行生命周期

### 7.1 Revision

编译 Revision 包含：

- 规范化文件名和全部源码。
- LanguageVersion、nullable、优化选项。
- Imports。
- SecurityPolicy 规则。
- 平台程序集指纹。
- 显式 DLL 的绝对路径和 SHA256。
- 单文件宿主中嵌入程序集的标识和原始元数据哈希。
- 依赖发现和架构验证选项。

因此同一路径 DLL 内容变化、Imports 变化或策略变化都会得到新 Revision。

`RoslynScriptProject.ComputeSourceRevision()` 只包含项目文件名和源码，用于异步编辑版本保护，不等同于完整编译 Revision。

### 7.2 热替换

```text
Compile -> Artifact（尚未加载）
                │
                ▼
Acquire ScriptId + Revision
    ├── 当前 Revision 相同：复用热程序集
    └── Revision 不同：加载新版本 -> 原子切换 -> 旧版本等待活动 lease 清零
                │
                ▼
每次 CreateProgram -> 新实例 -> ExecuteAsync
```

现代 .NET 使用可回收 `AssemblyLoadContext`。net48 默认 AppDomain 不能真正卸载动态程序集；高频更新时使用 WorkerPool 并通过 `maximumExecutionsPerWorker` 定期回收进程。

### 7.3 有界缓存

核心默认容量：

- 分析快照：12
- Emit 结果：32
- ScriptId 运行槽：64

通过 `RoslynScriptServiceOptions` 调整。分析快照共享 `SyntaxTree`、`Compilation`、`SemanticModel`，相邻源码通过 `SourceText.WithChanges` 和 `SyntaxTree.WithChangedText` 增量解析。

Workspaces 默认：

- 项目形状缓存容量：6
- 单次补全最大项数：256

`RoslynScriptWorkspaceService` 以项目形状和环境为缓存键，命中后使用 `Document.WithText` 只更新变化文档；条目有 lease，正在使用的条目不会被回收线程中途释放。

可通过：

```csharp
service.GetCacheStatistics();
workspace.GetCacheStatistics();
```

观察命中、未命中、增量解析/更新和当前条目数。

---

## 8. 补全体系

### 8.1 两级补全

1. **核心轻量补全**
   - 由 `RoslynScriptLanguageService` 提供。
   - 来源：可见符号、关键字、宿主 `ContextItems`。
   - 不依赖 Workspaces/Features。
   - 保证未部署可选模块时仍有可用编辑体验。

2. **完整 Roslyn CompletionService**
   - 由 `RoslynScriptWorkspaceService` 实现 `IRoslynScriptCompletionProvider`。
   - 支持扩展方法、命名参数、跨文件符号、推荐优先级、未导入类型和提交时自动 using。
   - 返回精确 replacement span、new caret position 和可原子提交的多项 `RoslynScriptTextChange`。
   - 受 `RoslynScriptSecurityPolicy` 过滤，不推荐被禁止命名空间。

单文件编辑器需要显式设置：

```csharp
using var workspace = new RoslynScriptWorkspaceService();
editor.CompletionProvider = workspace;
```

项目编辑器在常规未裁剪桌面部署中会通过反射自动发现 `ScriptEngine.Workspaces`。单文件发布、程序集裁剪或自定义加载上下文中应显式设置 `ProjectEditingService` 和 `CompletionProvider`，不要依赖反射发现。

### 8.2 补全窗口

`ScriptCompletionPopup`：

- WinForms `ToolStripDropDown` + 虚拟 ListView。
- 名称、类别、InlineDescription 多列。
- 右侧签名/XML 文档侧栏。
- 分类图标覆盖类型、方法、属性、字段、事件、变量、关键字、片段、命名空间和宿主上下文。
- 支持前缀、包含、子序列模糊匹配。
- Roslyn MatchPriority 与本编辑器生命周期内 MRU 排序。
- 键盘支持上下、PageUp/PageDown、Home/End、Enter/Tab、Escape。
- 可跟随 DPI 计算尺寸，并避让当前显示器 WorkingArea。

补全提交由 `ScriptEditorInputBehavior.CreateCompletionEdit` 统一计算，所有 text changes 放在同一个 Scintilla undo action 中。未导入类型的标识符替换和 using 插入必须一次撤销。

### 8.3 重要交互约束

- 输入标识符达到默认 2 个字符后，连续空闲 300ms 才启动分析；`AutoCompletionDelay` 可配置。
- 文本变化（包括宿主赋值 Text）和项目标签切换取消旧请求；立即请求会停止尚未到期的计时器，避免重复触发。
- 最近补全防抖修改后的 Windows Release 回归：net48/net8.0-windows 各 19 项通过；下方完整矩阵是此前基线，本轮未重新执行完整解决方案、x86 或发布验证。
- 输入 `.` 立即触发成员补全。
- `Ctrl+Space` 手动触发。
- Workspaces 调用必须可取消；旧请求返回后不得覆盖新源码对应的列表。
- 成员补全不混入代码片段。
- 补全说明留在自定义窗口，不调用 Scintilla `CallTipShow`。
- `(` 或 `,` 的签名提示仍使用 CallTip。

---

## 9. 编辑器输入与视觉行为

### 9.1 统一输入行为

`ScriptEditorInputBehavior` 集中处理：

- Tab-Tab 片段状态机。
- CRLF/LF 探测和片段换行。
- 括号、花括号和引号自动配对。
- 右括号跳过。
- 成对空字符退格删除。
- 配对花括号之间回车。
- 自动缩进触发判断。
- 补全多文本变化和新光标位置。

控件仍负责执行 Scintilla 文本操作和 Roslyn 缩进查询；纯输入决策不要重新散落到多个键盘事件。

Tab-Tab 状态只有在以下条件都不变时才展开：快捷词、快捷词起点和光标位置。第一次 Tab 只提示，第二次 Tab 展开；其他输入或文本变化取消待展开状态。片段包括：

- `if`
- `switch`
- `for`
- `foreach`
- `try`
- `using`
- `class`
- `program`

### 9.2 缩进和折叠

缩进、折叠都基于 Roslyn，不依赖 Scintilla C# Lexer：

- 换行根据容错语法树计算当前行目标列。
- Allman 风格独立 `{` 可对齐控制语句、类型、方法、访问器、lambda、switch。
- 即使 `if()` 等语法尚未完成也要给出合理缩进。
- 成对 `{}` 中回车生成一个缩进空行，并把 `}` 放回外层缩进。
- 折叠覆盖 namespace、类型、成员、访问器、控制语句、循环、异常块和 `#region`。
- 自定义 RGBA Chevron，不恢复 Scintilla 方框和连接 Marker。
- 空行不错误归入前一折叠区，独立 Block 以自己的 OpenBraceToken 为起点。

### 9.3 其他编辑功能

- 语法/语义高亮，XML 文档标签分色。
- 当前行背景、缩进引导和活动花括号作用域。
- 错误/警告波浪线和右侧概览条。
- 签名帮助、快速信息和源码/元数据定义。
- 格式化、行注释、查找/替换、跳转行、清理尾随空白。
- Undo/Redo、缩放、空白字符、自动换行、修改状态。

快捷键：

| 快捷键 | 行为 |
|---|---|
| `Ctrl+Space` | 补全 |
| `Ctrl+F` / `Ctrl+H` | 查找 / 替换 |
| `Ctrl+G` | 跳转行 |
| `Ctrl+/` | 行注释切换 |
| `F12` | 转到定义 |
| `F2` | 项目符号重命名 |
| `Shift+F12` | 查找全部引用 |
| `Ctrl+.` | Code Fix |
| `Ctrl+Alt+Z/Y` | 项目级撤销/重做，和文件文本 Undo 区分 |
| `Alt+Left/Right` | 定义/引用导航后退和前进 |

### 9.4 WPF 限制

WPF Adapter 使用 `WindowsFormsHost`：

- 优点：WinForms/WPF 共享同一 Scintilla 行为和测试基线。
- 限制：存在 Airspace；WPF 覆盖层、透明动画、复杂变换和裁剪不能跨越原生子窗口。
- 不要通过在 WPF 再实现一套编辑器来局部修复 Airspace；如要更换实现，必须先作完整架构决策并补齐两套兼容测试。

---

## 10. 多文件项目和 Workspaces 编辑

`RoslynScriptProjectEditorControl` 提供：

- 标签添加、删除、重命名和切换。
- 修改星号、`ModifiedFileNames`、`MarkProjectSaved()`。
- 关闭未保存文件确认。
- 项目联合编译和实时诊断。
- 跨文件补全、定义导航。
- 引用结果停靠面板，可通过 `ReferencesFound` 事件由宿主接管。
- 重命名和 Code Fix 并排差异预览。
- 有界项目级 Undo/Redo（默认 32）和导航历史（默认 64）。

Workspaces 编辑返回 `RoslynScriptWorkspaceEdit`：

- 完整替换后的 `Project`。
- `ChangedFiles`。
- `FileChanges`，含修改前后源码和预览范围。
- `HasConflicts`。
- `OriginalProjectRevision`。

`ApplyWorkspaceEdit` 默认拒绝：

1. `OriginalProjectRevision` 和当前源码不一致。
2. `HasConflicts == true`，除非显式 `allowConflicts: true`。

此保护不可删除，否则异步重命名/Code Fix 会覆盖用户在操作等待期间的新输入。

使用示例：

```csharp
using var workspace = new RoslynScriptWorkspaceService(new RoslynScriptWorkspaceOptions
{
    CacheCapacity = 6,
    MaximumCompletionItems = 256
});

projectEditor.ProjectEditingService = workspace;
projectEditor.CompletionProvider = workspace;

var rename = await projectEditor.CreateRenameEditAsync(
    "NewName", cancellationToken);
if (rename is { HasConflicts: false })
    projectEditor.ApplyWorkspaceEdit(rename);
```

---

## 11. DLL 引用和锁定

### 11.1 引用解析

优先使用 `RoslynScriptEnvironment.ReferencePaths`：

1. 路径规范化并检查存在。
2. 默认递归发现同目录私有托管依赖。
3. 检查同名程序集冲突。
4. 检查当前进程和 DLL 的 x86/x64/ARM 兼容性。
5. 内容 SHA256 进入环境指纹。
6. Roslyn MetadataReference 按路径+哈希复用。
7. 运行时按简单程序集名解析私有依赖。

`RoslynScriptReferenceInspector` 直接读取 PE 元数据，不把待检查 DLL 加载到 UI 进程。

### 11.2 引用锁

```csharp
var referenceLock = RoslynScriptReferenceLock.Create(paths, baseDirectory);
referenceLock.Save(@"script.references.lock.xml");

var loaded = RoslynScriptReferenceLock.Load(@"script.references.lock.xml");
var validation = loaded.Validate(baseDirectory);
if (!validation.IsValid)
{
    // 缺失、替换、版本、架构或依赖变化
}
```

锁文件记录：

- 可移植相对路径。
- 程序集名和版本。
- 架构。
- SHA256。
- 直接依赖。

XML 加载禁用 DTD。引用锁是部署 DLL 锁定，不是 NuGet lock file，也不解析包许可证或漏洞。

---

## 12. 安全和独立 Worker

### 12.1 信任模型

进程内模式只用于受信任脚本。`RoslynScriptSecurityPolicy.Restricted` 默认禁止：

- `System.IO`
- `System.Net`
- `System.Diagnostics`
- `System.Reflection`
- `Microsoft.Win32`
- `System.Runtime.InteropServices`
- `System.Environment.Exit`
- `System.Activator.CreateInstance`

策略通过语义符号产生 `SE2001` 编译错误，但它不是完备能力控制，不能防御所有动态间接调用或运行时漏洞。

### 12.2 一次性 Worker

```csharp
var result = await RoslynScriptWorkerClient.ExecuteAsync(
    source,
    new RoslynScriptWorkerOptions
    {
        WorkerPath = @"ScriptEngine.Worker.exe",
        Timeout = TimeSpan.FromSeconds(5),
        MaximumWorkingSetBytes = 256L * 1024 * 1024,
        MaximumCommittedMemoryBytes = 512L * 1024 * 1024,
        MaximumProcessCount = 1,
        RequireWindowsJobObject = true
    },
    new RoslynScriptEnvironment
    {
        SecurityPolicy = RoslynScriptSecurityPolicy.Restricted
    },
    cancellationToken);
```

一次性客户端使用临时请求目录传递 UTF-8 源码和环境，超时、取消、资源超限或协议错误后终止进程并等待退出，再清理请求目录。

### 12.3 WorkerPool

```csharp
using var pool = new RoslynScriptWorkerPool(
    options,
    workerCount: 2,
    maximumExecutionsPerWorker: 50);

var result = await pool.ExecuteProjectAsync(project, environment, cancellationToken);
```

池特性：

- 每个 Worker 串行，池内 Worker 并行。
- 有界并发。
- `READY|1|PID` 身份握手。
- PID 校验。
- PING/PONG 健康检查。
- 请求 nonce。
- net8 命名管道使用 `PipeOptions.CurrentUserOnly`。
- Console 输出与控制协议隔离。
- 超时、崩溃、协议故障或达到次数后淘汰进程，后续请求自动补充。

### 12.4 Job Object

Windows 默认使用 Job Object：

- 关闭 Job 句柄终止整个 Worker 进程树。
- `MaximumProcessCount` 限制活动进程数，默认 1，阻止脚本创建子进程。
- `MaximumCommittedMemoryBytes` 是内核提交内存硬限制。
- `MaximumWorkingSetBytes` 仍由宿主轮询。
- `OperatingSystemIsolationApplied` 报告是否成功应用。

Job Object 是稳定性和资源约束模块，不是业务权限模块。账号、ACL、容器和低完整性级别由部署基础设施负责。

### 12.5 Worker 数据边界

Worker 不传递：

- `HostContext`
- `IServiceProvider`
- 任意进程内对象引用

返回值只跨进程转换为文本和类型名。单文件宿主中的嵌入 Assembly 无法按路径发送给 Worker，应把所需 DLL 外置并通过 `ReferencePaths` 传入。

---

## 13. net48、x86 和单文件发布

### 13.1 net48

- 编译和编辑支持现代 C# 语法所需的 `IsExternalInit`、required-member 等标记类型。
- 不承诺旧 CLR 无法执行的现代运行时能力，例如默认接口实现。
- 默认 AppDomain 无法真正卸载动态程序集；频繁更新使用 WorkerPool 回收。
- 测试 x86 时必须使用独立 `IntermediateOutputPath` 和 `OutputPath`，避免污染 AnyCPU 构建缓存。

### 13.2 Scintilla 原生卫星

Scintilla.NET 按托管程序集位置寻找原生 DLL：

- net48 需要包约定的 `packages/Scintilla.NET.<version>/build/x64|x86` 目录。
- net8 桌面输出需要 `x64` / `x86` 原生卫星。
- `ScriptEngine.WinForms.csproj` 已显式传递 net48 卫星到最终应用。

### 13.3 单文件发布

Demo 的单文件发布实际仍保留/自解压 Scintilla 托管包装器和原生卫星。必须使用：

```powershell
-p:PublishSingleFile=true
-p:IncludeNativeLibrariesForSelfExtract=true
-p:IncludeAllContentForSelfExtract=true
```

并参考 `ScriptEngine.WinForms.Demo.csproj` 中：

- `IncludeScintillaInSingleFileBundle`
- `CopyScintillaNativeSatellitesAfterPublish`

核心在 `Assembly.Location` 为空时会从 `Assembly.TryGetRawMetadata` 创建 Roslyn MetadataReference，并使用有界缓存复用原始元数据。不要恢复“所有引用都必须有文件路径”的假设。

---

## 14. Demo 和常用命令

从仓库目录 `DP.WorkFlow` 执行：

```powershell
# Demo
dotnet run --project samples/ScriptEngine.WinForms.Demo/ScriptEngine.WinForms.Demo.csproj -c Release

# 完整解决方案
dotnet restore DP.WorkFlow.sln --force-evaluate
dotnet build DP.WorkFlow.sln -c Release --no-restore -m:1

# 编辑器验证矩阵
./tools/run-script-engine-editor-matrix.ps1 -Configuration Release -StressIterations 5

# 加入 net48 x86 和 win-x64 单文件发布冒烟
./tools/run-script-engine-editor-matrix.ps1 `
    -Configuration Release `
    -StressIterations 5 `
    -IncludeX86 `
    -IncludeSingleFilePublish
```

Demo 支持：

- 编辑、完整 CompletionService、诊断、折叠和格式化。
- DLL 引用管理。
- 进程内编译执行和输出查看。
- `--single-file-smoke` 非交互启动 Scintilla 并执行 Roslyn 编译冒烟。

---

## 15. 验证基线

最近一次完整 Release 验证：

| 测试项目 | 框架 | 结果 |
|---|---|---|
| `ScriptEngine.Tests` | net8.0 | 51 通过 |
| `ScriptEngine.Tests` | net48 | 50 通过 |
| `ScriptEngine.Workspaces.Tests` | net8.0 | 13 通过 |
| `ScriptEngine.Workspaces.Tests` | net48 | 13 通过 |
| `ScriptEngine.Windows.Tests` | net8.0-windows | 17 通过 |
| `ScriptEngine.Windows.Tests` | net48 | 17 通过 |
| Windows 编辑器 x86 | net48/x86 | 17 通过 |
| 完整解决方案 Release 构建 | 全部 | 0 警告、0 错误 |
| Demo 单文件 smoke | win-x64 | 通过 |

覆盖的关键回归：

- 核心编译/运行与入口契约。
- 多文件联合编译、文件名诊断和跨文件定义。
- 热修订原子替换、旧版本 lease 和每次新实例。
- 有界缓存、万行源码快速修订和最新快照正确性。
- 模糊补全、语义分类、签名、QuickInfo。
- Workspaces 跨文件补全、自动 using、多文本提交和安全过滤。
- Workspace LRU、增量文档更新和预览 FileChanges。
- 引用检查、引用锁、私有依赖和 DLL 内容变化 Revision。
- Restricted 策略。
- Worker 执行、多文件、硬超时、池恢复、Console 噪声和 Job 子进程限制。
- CRLF/LF、Tab-Tab、独立左花括号和成对花括号换行。
- 自定义补全窗口、模糊提交、多显示器边缘定位和单步撤销。
- WinForms/WPF 控件兼容面。

注意：当前工作目录在最近一次实现期间不是 Git 仓库。接手时先实际执行 `git rev-parse --is-inside-work-tree`，不能假定可用 `git status/diff`；必要时用文件检查和构建结果建立变更基线。

---

## 16. 已解决过的高风险问题

这些问题都有回归测试，后续不要倒退：

1. **Scintilla 补全项被空格拆散**：原生 `AutoCSeparator` 默认是空格，展示标签中的空格不能靠反斜杠转义。
2. **补全窗口被 CallTip 关闭**：补全文档曾使用 CallTip，导致输入 `context.` 后列表消失；现在使用独立自定义窗口。
3. **独立 `{` 缩进错误**：必须从 Roslyn 容错语法定位控制语句/声明头，而不是只看上一行文本。
4. **Tab-Tab 被补全窗口抢占**：片段状态机必须先于补全窗口处理 Tab；第一次 Tab 关闭列表并等待第二次。
5. **重复释放 CancellationTokenSource**：异步请求取消和释放必须由单一所有者负责，避免 `ObjectDisposedException`。
6. **net48 缺少 IsExternalInit**：兼容标记由编译层注入，不能要求每份用户脚本自行声明。
7. **文件重命名后定义仍指向旧名**：项目快照和 Roslyn Document 名称必须一起更新。
8. **异步 Workspace 编辑覆盖新输入**：应用前校验 `OriginalProjectRevision`。
9. **命名管道连接前设置 AutoFlush**：必须在连接完成后配置流。
10. **脚本 Console 干扰 Worker 协议**：控制协议走命名管道，Console 输出独立捕获。
11. **超时后过早删除请求目录**：必须等待 Worker 退出，再清理目录。
12. **net48 项目引用未传递 Scintilla 卫星**：WinForms 项目显式复制包内 x86/x64 原生文件。
13. **单文件中 Assembly.Location 为空**：平台引用必须支持原始元数据，Scintilla 仍按其加载约定部署。
14. **net48 测试使用 `string.Contains(string, StringComparison)`**：该运行目标没有此重载，测试和兼容代码使用 `IndexOf`。

---

## 17. 已知限制和下一阶段优先级

### 17.1 已知限制

- Restricted 不是安全沙箱，也不提供低权限账号或 ACL。
- net48 进程内动态程序集不可真正卸载。
- Worker 不支持传递宿主对象/服务容器，返回值只保留文本和类型名。
- WPF 存在 WindowsFormsHost Airspace。
- 高 DPI 已有计算和自动化边界测试，但 125%–250%、多屏混合 DPI 仍需要真实设备持续验证。
- 中文内容、Unicode 位置和连续输入已做自动化覆盖；完整 IME composition/candidate window 行为仍需要中文输入法实机验证。
- 自定义补全窗口尚未实现 Visual Studio 同等级的全部 Roslyn commit-character 规则、复杂重载分组和可停靠文档窗口。
- 引用锁不是 NuGet 依赖解析、离线包仓库、许可证或漏洞报告。
- 没有脚本断点、单步、调用栈和局部变量调试模块。
- 工作流 Studio 的专用包装控件仍以单文件场景为主；其手动 `CompletionRequested` 路径会把结果转为字符串，后续可直接传递 `RoslynScriptCompletionItem` 以保留类别和文档。

### 17.2 建议顺序

1. 在真实 125%–250% 混合 DPI 和中文 IME 环境完成交互验证，修复时补自动化边界测试。
2. 完善 CompletionService commit-character/重载选择规则和长签名布局，但保持现有小接口。
3. 为项目差异预览增加逐文件/逐 hunk 勾选，同时保留原子版本保护和项目级 Undo。
4. 完善标签关闭宿主协议、持久保存回调和引用结果长期停靠模型。
5. 增加 100+ 文件、数十万行源码、Worker 高并发和长时间池回收测试。
6. 若业务需要，再设计独立 NuGet 解析模块；不要把它塞进 `RoslynScriptEnvironment`。
7. 若业务需要调试，做可选独立调试模块，优先基于 Worker/PDB，不污染核心运行接口。

---

## 18. Astra 推荐阅读和改动流程

### 18.1 最短阅读顺序

1. 本文。
2. `RoslynScriptContracts.cs`。
3. `RoslynScriptService.cs`。
4. `RoslynScriptCompiler.cs` + `ScriptEnvironmentSnapshot.cs`。
5. `RoslynScriptAnalysisProvider.cs` + `RoslynScriptLanguageService.cs`。
6. `RoslynScriptEditorControl.cs` + `ScriptEditorInputBehavior.cs` + `ScriptCompletionPopup.cs`。
7. `RoslynScriptWorkspaceService.cs` + Workspace contracts。
8. `RoslynScriptProjectEditorControl.cs`。
9. Worker Client/Pool/Program。
10. 三个 ScriptEngine 测试文件。

### 18.2 修改前检查

- 确认改动属于哪个模块和 seam，不要在 UI 中复制 Roslyn/运行时实现。
- 判断是否会把 Workspaces/Features 依赖泄漏到核心。
- 判断是否破坏 net48 编译。
- 判断是否影响 ScriptId/Revision/热替换语义。
- 判断是否新增无界缓存、后台任务或未释放资源。
- 判断异步结果是否有取消和源码版本保护。
- 判断文本修改能否单步撤销并保留 CRLF/LF。
- 判断 Worker 故障路径是否等待进程退出并清理。

### 18.3 修改后最低验证

```powershell
dotnet restore DP.WorkFlow.sln --force-evaluate
dotnet build DP.WorkFlow.sln -c Release --no-restore -m:1
./tools/run-script-engine-editor-matrix.ps1 -Configuration Release -StressIterations 1
```

涉及以下范围时追加：

- Scintilla、发布或平台引用：`-IncludeX86 -IncludeSingleFilePublish`
- 缓存/并发：提高 `-StressIterations`，并运行对应 filtered test 多轮。
- Worker：至少覆盖一次性客户端、池超时恢复和 Job Object 测试。
- WPF：同时运行 net8.0-windows 和 net48 Windows 测试，不能只验证 WinForms。

### 18.4 完成标准

- 完整解决方案 Release：0 警告、0 错误。
- 受影响的 net8/net48 测试全部通过。
- 新行为有接口级或真实控件回归测试。
- README、本维护文档和 Demo 用法同步更新。
- 不以静态策略冒充沙箱，不新增权限配置界面。
- 不因高级编辑功能增加核心运行包体积。
