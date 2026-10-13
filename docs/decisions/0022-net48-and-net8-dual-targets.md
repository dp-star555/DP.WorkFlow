# ADR-0022：Workflow 同时面向 .NET Framework 4.8 与 .NET 8

- 状态：Accepted
- 日期：2026-10-13

## 决策

`src/Workflow` 下的全部库（内核、节点、Persistence、UI.Shared、WinForms/WPF 工作台、Vision/Process/LabelInspection 界面）同时编译为 `net48` 和 `net8.0` / `net8.0-windows`。设备主程序是 .NET Framework 4.8 时，可以直接引用引擎、节点和编辑界面，不需要另起进程。

- 目标框架只在 `src/Workflow/Directory.Build.props` 定义：`$(WorkflowTargetFrameworks)`（`net48;net8.0`）、`$(WorkflowWindowsTargetFrameworks)`（`net48;net8.0-windows`）。项目文件只引用这两个属性。
- `src/Workflow/Directory.Build.targets` 给每个项目链接 `Shared/Compatibility/*.cs`，并在 4.8 目标上引用 PolySharp（record、init、Index/Range、CallerArgumentExpression 等编译器类型）、System.Text.Json、Microsoft.Bcl.AsyncInterfaces、System.Threading.Tasks.Extensions。
- 可空性只在 .NET 8 目标上检查：4.8 的 BCL 没有可空注解，4.8 目标设为 `<Nullable>annotations</Nullable>`。
- 测试工程只跑 .NET 8；4.8 由编译保证，示例宿主 `samples/DP.WorkFlow.WinForms.Sample` 同时编为两个框架，用于在 Windows 上实际运行验证。

## 写代码时的约定

| 不要写（4.8 没有） | 改用 |
|---|---|
| `ArgumentNullException.ThrowIfNull`、`ArgumentException.ThrowIfNullOrWhiteSpace`、`ObjectDisposedException.ThrowIf` | `Guard.NotNull`、`Guard.NotNullOrWhiteSpace`、`Guard.NotDisposed` |
| `double.IsFinite`、`Math.Clamp`、`Enum.GetValues<T>`、泛型 `Enum.IsDefined` / `Enum.Parse<T>` | `Polyfill.IsFinite`、`Polyfill.Clamp`、`Polyfill.GetEnumValues<T>`、`Polyfill.IsDefined`、`Polyfill.ParseEnum<T>` |
| `ValueTask.FromResult`、`ValueTask.CompletedTask` | `Polyfill.FromResult`、`default(ValueTask)`；被测试和示例共享的文件写 `new ValueTask<T>(value)` |
| `Split(c, RemoveEmptyEntries \| TrimEntries)` | `Polyfill.SplitTrimmed(text, c)` |
| `new Dictionary<K, V>(只读字典或序列, 比较器)` | `source.ToDictionary(比较器)` |
| `Path.GetFullPath(path, basePath)`、`Path.GetRelativePath`、`Path.TrimEndingDirectorySeparator`、`File.Move(a, b, true)`、`File.*Async`、`Convert.ToHexString`、`SHA256.HashData`、`Stopwatch.GetElapsedTime` | `Polyfill` 中同名方法 |
| 接口成员的默认实现 | 抽象成员，由实现类实现（4.8 运行时不支持默认接口实现，编译即报 CS8701） |
| 泛型数学（`INumber<T>`）、`DateOnly`/`TimeOnly`、`PeriodicTimer`、`EnumerationOptions`、`RegexOptions.NonBacktracking`、WPF `OpenFolderDialog` | 按类型写重载；或 `#if NETFRAMEWORK` 分支（`Polyfill.NonBacktracking` + 匹配超时、`WorkflowFolderPicker`） |
| 数组切片 `array[1..]` | `Skip` / `Take`（字符串切片可以用） |

缺少的实例方法（`string.Contains(string, StringComparison)`、`Dictionary.TryAdd`、`DistinctBy`、`Task.WaitAsync` 等）由 `NetFrameworkExtensions` 以同名扩展方法补齐，写法与 .NET 8 一致；缺少的类型（`ReferenceEqualityComparer`、非泛型 `TaskCompletionSource`、`PriorityQueue`）在 `NetFrameworkTypes` 中以同名内部类型补齐。这两个文件只在 4.8 目标上编译。

## 原因

设备主程序仍有 .NET Framework 4.8 的存量；把流程编辑和运行直接嵌入主程序，比跨进程调用 .NET 8 的流程服务简单（不需要跨进程传图像和状态）。依赖的 DP.Vision、DP.LabelInspection、ModernUI、ScriptEngine 均已支持 4.8 或 netstandard2.0。

## 结果

- 新代码按上表写，两个框架共用一份源码；`#if NETFRAMEWORK` 只用于没有等价写法的少数地方。
- 4.8 宿主自行声明高 DPI（`App.config` 的 `DpiAwareness` 与 `app.manifest` 的 Windows 10 兼容声明），示例宿主可作参考。
- 4.8 上的行为由 Windows 上运行示例宿主验证；单元测试仍只覆盖 .NET 8。
- 原来带默认实现的接口成员改为必须实现，外部插件需要补上（原默认值写在括号里）：
  `IWorkflowNodeEditorPageProvider.ExtensionId`（实现类型完整名称）、`IWorkflowNodeOperation.CanContinue`（`true`）、
  `IWorkflowNodeOutputProjection.Retire()`（空方法）、`IWorkflowKeyedOutput.DescribeMissingKey`（“不存在键 X。”）、
  `IWorkflowNodeExecutionContext.BindingScopeId / PlanPath / SubflowInputs`（`Guid.Empty`、`$`、`WorkflowSubflowValues.EmptyInputs`）、
  `IWorkflowChildExecutionContext.OpenChildWorkflowSession`、`IWorkflowRuntimeHost.RunNodeAsync`。
