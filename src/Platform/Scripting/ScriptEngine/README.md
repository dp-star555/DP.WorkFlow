# ScriptEngine

> 后续维护者或思考模型接手前，请先阅读完整的 [ScriptEngine 技术说明与 Astra 接手指南](MAINTAINER-HANDOFF.md)。该文档记录模块边界、代码地图、不变量、验证基线、历史故障和继续演进顺序。

ScriptEngine 是流程系统中的受信任 C# 动态扩展中间件。它负责编辑期语言能力、Roslyn 编译、按脚本修订热加载以及显式 DLL 引用，不负责流程调度。

## 编辑能力

- WinForms/WPF 编辑器输入标识符满 2 个字符且停止输入 300ms 后请求补全（`AutoCompletionDelay` 可配置）；输入 `.` 和 `Ctrl+Space` 立即请求。继续输入、程序替换文本或切换项目标签会取消旧请求，过期结果不展示。300ms 是分析启动延迟，不包含 Roslyn 计算耗时。
- 轻量核心补全来源包含 Roslyn 可见符号、C# 关键字和宿主 `ContextItems`；设置可选 `IRoslynScriptCompletionProvider` 后改用完整 Roslyn `CompletionService`，支持扩展方法、命名参数、跨文件符号、未导入类型以及提交时自动添加 using。
- 补全使用独立的虚拟化窗口，不再受 Scintilla 原生单列列表限制；名称、语义类别和内联说明分列显示，右侧预览签名/XML 文档，并支持前缀、包含和子序列模糊匹配、高 DPI 缩放及多显示器边缘避让。
- 输入 `(` 或 `,` 自动显示当前方法签名和活动参数；鼠标悬停可查看符号文档或波浪线诊断；按 `F12` 可跳转到源码定义或查看元数据符号来源。
- 自动配对括号、花括号和引号；选区可由括号/引号包围，成对空字符可一次退格删除。代码片段状态、成对字符和换行保持由独立输入行为模块统一决策，片段会保留原文档的 CRLF/LF 风格。
- 换行以及单独输入左右花括号时，根据容错 Roslyn 语法结构自动计算缩进；Allman 风格左花括号会自动与 `if`、方法或类型声明对齐，块内光标再增加一级缩进。
- 输入 `if`、`for`、`foreach`、`switch`、`try`、`using`、`class` 或 `program` 后连续按两次 Tab，可直接展开代码片段。编辑区同时显示缩进引导线、活动 `{ }` 作用域连线和当前行背景。
- 折叠边栏支持 namespace、类型、方法/访问器、`if`、`switch`、循环、异常处理块和 `#region`，收缩后在标题行显示 `…`；也可通过控件 API 全部展开或收缩。
- 内置 `if`、`switch`、`for`、`foreach`、`try`、`using`、`class` 和 `program` 代码片段。
- 提供格式化、行注释、查找替换、跳转行、行尾空白清理、缩放、空白字符显示以及修改状态接口；快捷键包括 `Ctrl+F`、`Ctrl+H`、`Ctrl+G` 和 `Ctrl+/`。
- 波浪线右键菜单可应用缺少分号、括号、花括号和常用 using 等轻量代码修复。
- 可选 `ScriptEngine.Workspaces` 模块提供基于完整 Solution 语义的跨文件重命名、查找全部引用、自动查找 using、删除无用 using 和完整 Roslyn 补全；模块使用有界常驻 `AdhocWorkspace` 缓存，并通过 `Document.WithText` 增量更新变化文件，核心运行包不因此引入 Workspaces/Features 依赖。
- XML 文档注释的标签与正文分别着色。
- 输入停止后自动刷新 Roslyn 错误/警告波浪线，并在右侧文档概览条标记位置；点击标记可跳转到对应行。

## 生命周期

- `ScriptId` 标识一个稳定的逻辑脚本。
- `Revision` 由源码、Imports、编译选项和全部引用文件内容共同计算。
- 同一 `ScriptId + Revision` 始终复用已加载版本，并为每次运行创建新的 `ICSharpProgram` 实例。
- 同一 `ScriptId` 出现新 `Revision` 时，先完成编译和加载，再原子替换当前版本。
- 旧版本存在活动执行时继续运行；活动计数归零后再卸载。
- `CompileProgramAsync` 只产生 Artifact，不加载程序集。
- `RemoveProgram` 清理指定脚本的运行槽和编译缓存。

## 热运行内存

诊断、补全、签名帮助、快速信息和语义高亮通过同一个有界 Roslyn 分析快照复用 `SyntaxTree`、`Compilation` 和 `SemanticModel`。相邻源码修订通过 `SyntaxTree.WithChangedText` 增量解析。Emit 结果和逻辑脚本热版本槽也设置了上限，默认分别为 12、32 和 64，可通过 `RoslynScriptServiceOptions` 调整并通过 `GetCacheStatistics()` 观察分析缓存命中率。

热点执行会复用平台 `MetadataReference`、稳定环境快照，以及工作流节点的规范化源码。每次调用仍会创建新的脚本实例、执行上下文和结果对象，因此内存监视器出现缓慢的 GC 锯齿是正常现象。判断泄漏应观察完整 GC 后的存活内存基线是否持续抬高，而不是要求曲线完全水平。

## 运行时差异

- 现代 .NET 使用可回收 `AssemblyLoadContext`，旧修订释放全部引用后可以协作卸载。
- .NET Framework 4.8 支持编译、编辑和进程内执行，并自动注入 `record`、`init`、`required` 等现代 C# 语法需要的编译标记类型；旧 CLR 本身不支持的默认接口实现等功能仍会返回 Roslyn 诊断。
- .NET Framework 默认 AppDomain 中的已加载程序集不能真正卸载。对于需要硬超时、强隔离或频繁替换同名 DLL 的场景，应使用 `ScriptEngine.Worker`。
- 当前进程内执行属于受信任模式。`CancellationToken` 只提供协作式取消，不能终止不响应取消的死循环。
- `RoslynScriptSecurityPolicy.Restricted` 可在编译前禁止文件、网络、进程、反射、注册表和非托管调用，但这是静态防误用规则，不是安全沙箱。

## DLL 引用

使用 `RoslynScriptEnvironment.ReferencePaths` 显式提供 DLL。引用文件内容哈希进入环境指纹，同一路径文件变化后会产生新的脚本修订。默认会递归发现显式 DLL 同目录中的私有托管依赖，并在编译前检查 x86/x64/ARM 架构；可通过 `ResolveReferenceDependencies` 和 `ValidateReferenceArchitecture` 配置。`RoslynScriptReferenceInspector` 使用 PE 元数据读取程序集名称、版本、架构、SHA256 和直接依赖，不会把 DLL 加载到 UI 进程。

`RoslynScriptReferenceLock` 可生成确定性的 `script.references.lock.xml`，锁定 DLL 身份、版本、架构、SHA256 和依赖清单；部署或启动前调用 `Validate` 可发现缺失或被替换的引用。

## 推荐调用

```csharp
using var service = new RoslynScriptService();
var environment = new RoslynScriptEnvironment
{
    ReferencePaths = new[] { @"lib\Customer.Algorithm.dll" }
};

var compilation = await service.CompileProgramAsync(source, environment, cancellationToken);
if (!compilation.Success)
{
    // 展示 compilation.Diagnostics
    return;
}

var result = await service.ExecuteAsync(
    scriptId: "稳定脚本标识",
    source: source,
    hostContext: hostContext,
    environment: environment,
    cancellationToken: cancellationToken);
```

匿名 `ExecuteAsync(source, ...)` 只适合临时工具。工作流节点必须传入稳定 `ScriptId`，才能在源码变化时精确替换对应旧版本。

## 多文件项目

使用 `RoslynScriptProject` 可以把入口、模型和辅助函数拆分到多个 `.cs` 文件；诊断保留原始文件名，所有文件共享同一个 Compilation、入口验证和运行修订。`GetProjectCompletionsAsync` 和 `GetProjectDefinitionAsync` 提供跨文件补全及定义跳转。

WinForms/WPF 还分别提供 `RoslynScriptProjectEditorControl` 标签式项目编辑器，内置文件添加、删除、重命名、修改星号、关闭确认、项目级实时诊断、跨文件补全、F12 跳转和联合编译。引用可选 Workspaces 模块后，项目编辑器会自动发现并复用同一个常驻实现，启用 `F2` 跨文件重命名、`Shift+F12` 停靠式引用结果、`Ctrl+.` 项目代码修复、应用前并排差异预览、项目级撤销/重做和 `Alt+Left/Alt+Right` 导航历史；也可显式设置 `ProjectEditingService` 与 `CompletionProvider`。

```csharp
var project = new RoslynScriptProject
{
    SourceFiles = new[]
    {
        new RoslynScriptSourceFile("Main.cs", mainSource),
        new RoslynScriptSourceFile("Features/Helper.cs", helperSource)
    }
};
var result = await service.ExecuteProjectAsync("project-id", project, cancellationToken: cancellationToken);
```

需要高级项目编辑能力时单独引用 `ScriptEngine.Workspaces`：

```csharp
using var workspace = new RoslynScriptWorkspaceService(new RoslynScriptWorkspaceOptions
{
    CacheCapacity = 6,
    MaximumCompletionItems = 256
});
editor.CompletionProvider = workspace; // 单文件编辑器启用完整 Roslyn CompletionService
projectEditor.ProjectEditingService = workspace;
projectEditor.CompletionProvider = workspace;

var fixes = await workspace.GetCodeFixesAsync(project, "Main.cs", diagnostic, environment, cancellationToken);
project = fixes[0].Project; // 一次替换全部受影响文件

var references = await workspace.FindReferencesAsync(
    project, "Helper.cs", symbolPosition, environment, cancellationToken);
var rename = await workspace.RenameSymbolAsync(
    project, "Helper.cs", symbolPosition, "NewName", environment, cancellationToken);
if (rename is not null)
    project = rename.Project;

var cache = workspace.GetCacheStatistics(); // 命中、未命中、增量更新文件数和当前条目数
```

## 编辑器验证矩阵

`tools/run-script-engine-editor-matrix.ps1` 会验证核心、Workspaces 和 Windows 编辑器的 net8.0/net48 组合，并重复运行万行级快速快照压力测试。需要发布边界验证时可追加 `-IncludeX86 -IncludeSingleFilePublish`，分别运行 net48 x86 UI 矩阵和 win-x64 单文件发布；单文件步骤会实际启动 Scintilla 并完成一次 Roslyn 编译冒烟。

Scintilla.NET 根据托管程序集位置查找 `x64/x86` 原生卫星目录，因此单文件宿主必须使用 `IncludeAllContentForSelfExtract=true`，并像 Demo 项目一样把 `Scintilla.NET.dll` 与原生卫星作为发布内容保留；不能只把原生 DLL 打进 bundle。

```powershell
./tools/run-script-engine-editor-matrix.ps1 -Configuration Release -StressIterations 5
```

## 独立 Worker

`ScriptEngine.Worker` 同时生成 net8.0 和 net48 可执行程序，并支持单文件及 `ExecuteProjectAsync` 多文件项目。Worker 不接收宿主对象或 `IServiceProvider`，适合纯计算、文件输入输出受策略控制的脚本。返回值跨进程转换为文本。

```csharp
var isolated = await RoslynScriptWorkerClient.ExecuteAsync(
    source,
    new RoslynScriptWorkerOptions
    {
        WorkerPath = @"ScriptEngine.Worker.exe",
        Timeout = TimeSpan.FromSeconds(5),
        MaximumWorkingSetBytes = 256L * 1024 * 1024
    },
    new RoslynScriptEnvironment
    {
        SecurityPolicy = RoslynScriptSecurityPolicy.Restricted
    },
    cancellationToken);
```

超时、取消或超出工作集时宿主会终止 Worker。本模块不提供账号、ACL 或业务权限配置；如部署环境另有权限隔离要求，应由宿主基础设施负责。

高频隔离执行可使用 `RoslynScriptWorkerPool`。它通过带协议版本、进程身份握手和请求健康检查的命名管道复用常驻 Worker，支持有界并发、单请求硬超时/内存检查、故障自动替换，以及按执行次数主动回收；net48 可通过回收释放长期积累的动态程序集。

Windows 下默认启用 Job Object：Job 句柄关闭时内核会终止整个 Worker 进程树，并以 `MaximumCommittedMemoryBytes` 和 `MaximumProcessCount` 应用内核级硬限制；`MaximumWorkingSetBytes` 仍由宿主轮询检测。若部署环境不允许降级，可设置 `RequireWindowsJobObject = true`；执行结果的 `OperatingSystemIsolationApplied` 会报告是否成功应用。非 Windows 平台继续使用进程监控与进程树终止能力。

```csharp
using var pool = new RoslynScriptWorkerPool(
    workerOptions,
    workerCount: 2,
    maximumExecutionsPerWorker: 50);
var result = await pool.ExecuteAsync(source, environment, cancellationToken);
```
