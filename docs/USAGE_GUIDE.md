# DP.WorkFlow 整体使用指南

本文面向三类使用者：

1. 在 WinForms/WPF 应用中嵌入工作流设计器；
2. 在设备服务、控制程序或测试中直接运行工作流；
3. 开发自定义节点和宿主动作。

> 当前基线为 .NET 8，公共命名空间主要为 `DP.WorkFlow`、`DP.WorkFlow.UI`、`DP.WorkFlow.UI.WinForms` 和 `DP.WorkFlow.UI.Wpf`。

---

## 1. 工程结构

| 工程 | 用途 | 是否依赖桌面 UI |
|---|---|---|
| `DP.WorkFlow.Abstractions` | 节点、端口、绑定、Handler 和 Trace 契约 | 否 |
| `DP.WorkFlow.Core` | Document/Graph/Layout、编译计划、结构校验和绑定分析 | 否 |
| `DP.WorkFlow.Runtime` | Context、执行引擎、快照和 RuntimeHost | 否 |
| `DP.WorkFlow.Nodes.Standard` | Start、Action、Decision、并行等标准节点 | 否 |
| `DP.WorkFlow.Nodes.Composite` | Block 子流程节点 | 否 |
| `DP.WorkFlow.Nodes.Motion` | IO、轴、气动和扫码设备节点 | 否 |
| `DP.WorkFlow.Nodes.Process` | 产品流、工站、机器人和异常恢复节点 | 否 |
| `DP.WorkFlow.Persistence.Json` | Schema 4 Document JSON 和旧版迁移 | 否 |
| `DP.WorkFlow.UI.Shared` | 设计会话、导航、诊断、文档工作区和监视模型 | 否 |
| `DP.WorkFlow.UI.WinForms` | 原生 WinForms 工作台 | WinForms |
| `DP.WorkFlow.UI.Wpf` | 原生 WPF 工作台 | WPF |

无界面服务通常引用 Runtime、Standard、Composite 和 Persistence。桌面程序再引用对应 UI 工程。

---

## 2. 构建与验证

在 `DP.WorkFlow` 目录执行：

```powershell
dotnet restore DP.WorkFlow.sln
dotnet build DP.WorkFlow.sln --no-restore
dotnet test DP.WorkFlow.sln --no-build
```

当前预期结果：

```text
Build: 0 warning / 0 error
Tests: 163 passed / 0 failed
```

解决方案固定为 x64；Windows UI 工程目标框架为 `net8.0-windows`。

---

## 3. 最小运行示例

### 3.1 注册节点模型

节点目录用于设计器、编译器和 JSON 反序列化：

```csharp
using DP.WorkFlow;

var nodeCatalog = new WorkflowNodeCatalog()
    .RegisterStandardNodes()
    .RegisterCompositeNodes();
```

节点类型必须在打开 JSON 或编译画布之前注册。未注册的当前版节点会按持久化策略保留为 Unknown；旧版字符串属性迁移则要求节点类型已注册。

### 3.2 注册 Handler

模型只保存配置，执行行为由 Handler 提供：

```csharp
var handlers = new WorkflowNodeHandlerCatalog()
    .Register(new StartNodeHandler())
    .Register(new EndNodeHandler())
    .Register(new ActionNodeHandler())
    .Register(new DecisionNodeHandler())
    .Register(new WaitFunctionNodeHandler())
    .Register(new DelayNodeHandler())
    .Register(new LoopNodeHandler())
    .Register(new JumpNodeHandler())
    .Register(new ValueCompareNodeHandler())
    .Register(new StringCompareNodeHandler())
    .Register(new ParallelAllNodeHandler())
    .Register(new WaitAllInputsCompletedNodeHandler())
    .RegisterCompositeNodeHandlers();
```

实际应用可以只注册流程会使用的 Handler；缺少 Handler 会在运行时失败。建议宿主统一注册全部内置 Handler。

### 3.3 注册宿主动作和条件

`Action` 节点通过稳定的 `FunctionKey` 调用宿主动作：

```csharp
var actions = new WorkflowActionRegistry()
    .Register("Machine.Clamp", async (context, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 调用真实设备服务。
        await Task.Delay(20, cancellationToken);
        context.SetVariable("ClampCompleted", true);

        // 返回值会成为该节点的标准输出。
        return new ClampResult(true, DateTimeOffset.UtcNow);
    });
```

`Decision` 和 `WaitFunction` 可调用条件注册表：

```csharp
var conditions = new WorkflowConditionRegistry()
    .Register("Machine.IsReady", (context, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            context.TryGetVariable<bool>("MachineReady", out var ready) && ready);
    });
```

将服务放入 Context：

```csharp
var services = new WorkflowServiceProvider()
    .Add<IWorkflowActionRegistry>(actions)
    .Add<IWorkflowConditionRegistry>(conditions);

var context = new WorkflowContext(services);
context.SetVariable("MachineReady", true);
```

### 3.4 构造工作流文档

```csharp
var start = new StartNodeModel { Id = "Start", Title = "开始" };
var action = new ActionNodeModel
{
    Id = "Clamp",
    Title = "夹紧",
    FunctionKey = "Machine.Clamp"
};
var end = new EndNodeModel { Id = "End", Title = "结束" };

var document = new WorkflowDocument { Name = "夹紧流程", EntryNodeId = start.Id };
var canvas = document.CanvasProjection; // 当前桌面设计器过渡投影
canvas.Nodes.Add(new WorkflowCanvasNode { Node = start, X = 80, Y = 100 });
canvas.Nodes.Add(new WorkflowCanvasNode { Node = action, X = 320, Y = 100 });
canvas.Nodes.Add(new WorkflowCanvasNode { Node = end, X = 560, Y = 100 });

canvas.Connections.Add(new WorkflowConnectionModel
{
    FromNodeId = start.Id,
    FromPort = WorkflowPorts.Success,
    ToNodeId = action.Id,
    ToPort = WorkflowPorts.Input
});
canvas.Connections.Add(new WorkflowConnectionModel
{
    FromNodeId = action.Id,
    FromPort = WorkflowPorts.Success,
    ToNodeId = end.Id,
    ToPort = WorkflowPorts.Input
});
```

节点 ID 在同一画布中必须唯一。连接必须使用节点声明的端口，不能依赖集合顺序推断分支。

### 3.5 使用 RuntimeHost 运行

```csharp
using var host = new WorkflowRuntimeHost(nodeCatalog, handlers);

host.SnapshotChanged += snapshot =>
{
    Console.WriteLine($"{snapshot.State}: {string.Join(",", snapshot.ActiveNodeIds)}");
};

host.Configure(document, context);
var result = await host.RunAsync();

Console.WriteLine($"State={result.State}, Success={result.Success}");
```

`Configure` 会立即编译文档并递归绑定 Handler；入口、结构、端口、并行、绑定或 Handler 错误都会在此处抛出。运行中不能重新配置。

---

## 4. 使用 WorkflowContext

### 4.1 全局变量

```csharp
context.SetVariable("LotId", "LOT-2025-001");

if (context.TryGetVariable<string>("LotId", out var lotId))
    Console.WriteLine(lotId);

var snapshot = context.ExportVariables();
context.RemoveVariable("LotId");
```

规则：

- Key 不能为空；
- `SetVariable` 不接受 null；
- 删除必须显式调用 `RemoveVariable`；
- 并行执行时变量容器线程安全，但业务层仍应避免多个分支无约束写同一个 Key。

### 4.2 节点输出

```csharp
if (context.TryGetNodeOutput("Clamp", out var latest))
    Console.WriteLine(latest?.Value);

foreach (var output in context.ExportNodeOutputs())
{
    Console.WriteLine(
        $"{output.NodeId} Token={output.TokenId} Scope={string.Join('/', output.ScopeIds)}");
}
```

`TryGetNodeOutput` 只适合监视和兼容代码。流程内部应使用强类型绑定，让 Runtime 按 Run、Token 和 Scope 检查可见性。

---

## 5. 强类型输入绑定

`WorkflowInput<T>` 支持 Literal 和 Binding 两种来源。

### 5.1 固定值

```csharp
var decision = new DecisionNodeModel
{
    Id = "Check",
    ConditionSource = E_DecisionConditionSource.Binding,
    Condition = WorkflowInput<bool>.FromLiteral(true)
};
```

### 5.2 引用上游输出

```csharp
decision.Condition = WorkflowInput<bool>.FromBinding(
    new WorkflowBindingKey("Inspect", "Passed"));
```

绑定键由两部分构成：

- `NodeId`：来源节点 ID；
- `MemberPath`：输出对象成员路径，例如 `Result.Code`；`$` 表示整个输出值。

设计器会根据以下规则生成候选并提前诊断：

- 来源节点必须存在；
- 来源在控制流上必须对消费者可见；
- 成员路径必须存在；
- 来源类型必须可转换为 `T`；
- 并行兄弟分支在汇聚前不可互相读取；
- 汇聚完成后允许读取已完成分支输出。

WinForms/WPF 属性面板使用可搜索绑定树，可按节点、成员路径和 CLR 类型搜索。将参数来源切换为 `Binding` 后，值区域显示带下拉标识的绑定按钮；选择器按“前道节点输出”和“公共数据”分组，并支持树形折叠、展开和搜索。

宿主可声明允许设计器绑定的公共数据类型：

```csharp
session.PublicDataCatalog
    .Register<bool>("MachineReady", "设备就绪", "设备数据")
    .Register<IVisionImage>("VisionImage", "当前视觉图像", "视觉数据")
    .Register<ProductContext>("CurrentProduct", "当前产品", "生产数据");
```

公共数据声明只提供设计期名称和类型。运行时值位于独立 `IWorkflowPublicDataStore`，不再写入流程局部变量：

```csharp
var publicData = new WorkflowPublicDataStore();
publicData.Apply(new WorkflowPublicDataChangeSet(
    new Dictionary<string, object>
    {
        ["MachineReady"] = true,
        ["CurrentProduct"] = product
    },
    Array.Empty<string>()));
var context = new WorkflowContext(publicData: publicData);
```

节点可通过 `context.PublishData(key, value)` 暂存显式发布；仅节点成功后提交。整个公共值使用 `$`，复杂对象可继续选择 `ProductId`、`Recipe.Name` 等兼容成员。未发布、成员不存在或运行时类型不兼容时会产生明确的 `WorkflowBindingException`。

---

## 6. 标准节点速查

| 节点 | 主要配置 | 输出端口 |
|---|---|---|
| Start | 无 | `Success` |
| End | 无 | 无 |
| Action | `FunctionKey` | `Success` |
| CSharpScript | `Script`、`ResultVarKey`、`ContinueOnError` | `Success`，可选 `Error` |
| Decision | 条件来源、`FunctionKey` 或 `Condition` | `True` / `False` |
| ValueCompare | 左右输入、比较操作和容差 | `True` / `False` |
| StringCompare | 左右输入、Ordinal 比较或 Like | `True` / `False` |
| WaitFunction | `FunctionKey`、超时和轮询间隔 | `Success` / `Timeout` |
| Delay | 延迟时间 | `Success` |
| Loop | 循环配置 | `Loop` / `Completed` |
| Jump | 跳转配置 | `Success` |
| ParallelAll | 多条 Branch 连接 | `Branch` |
| WaitAllInputsCompleted | 并行汇聚 | `Success` |
| Block | 子画布和输入输出映射 | `Success` |

连接时始终使用 `WorkflowPorts` 常量，避免硬编码显示文本。

`CSharpScript` 使用 Roslyn 在宿主进程内执行，必须显式注册 `CSharpScriptNodeHandler`。脚本可调用 `GetVariable<T>`、`SetVariable`、`RemoveVariable`、`Trace`，并可通过 `Context` 访问宿主服务。双击节点会打开统一节点工作台并自动增加“脚本”和“诊断”页面，支持 Ctrl+Space API/关键字补全和实时 Roslyn 诊断。它不是安全沙箱，只能运行受信任流程文档。

节点信息窗口按能力接口自动适配：普通节点直接使用整个窗口展示分组参数表；特殊节点左侧展示参数，右侧直接平铺子流程、脚本、图像或插件页面，不显示页面导航。Property、SubWorkflow、Script、Image 都通过内置 `IWorkflowNodeEditorPageProvider` 进入同一 Page Catalog；插件以独立 `Priority` 替换同名 PageId 槽位，并由稳定 `RendererKey` 精确匹配 WinForms/WPF Renderer。参数修改先进入编辑副本，只有“应用/确定”才整体提交，“取消”不影响正式节点。节点参数可使用 `[WorkflowProperty("中文名称", "中文说明", Category = "分类", Unit = "ms")]` 声明双 UI 共享元数据；未标注参数由中文约定表提供名称、分类和说明回退。宿主通过 `WorkflowStudioControl.NodeEditorExtensions.Register(...)` 成对注册平台扩展，或调用 `LoadNodeEditorPlugins(pluginDirectory)` 自动装载 `studio` 与当前平台 Module；视觉宿主仍通过 `ImageFrameSourceResolver` 注入图像帧源。

---

## 7. 并行流程

标准结构为：

```text
ParallelAll --Branch--> 分支 A --┐
             Branch--> 分支 B --┼--> WaitAllInputsCompleted --> 后续
             Branch--> 分支 C --┘
```

要求：

1. `ParallelAll.Branch` 至少连接到有效分支；
2. 分支必须有编译器可以确定的共同汇聚点；
3. 使用 `WaitAllInputsCompleted` 表达汇聚；
4. 不要在汇聚前跨兄弟分支绑定输出；
5. 任一兄弟分支失败时，其余受监管分支会被取消；
6. 嵌套并行会创建独立 Scope。

运行监视器可查看 Token、ParallelScope、已完成分支数和当前活动节点。

---

## 8. Block 子流程

### 8.1 基本模型

```csharp
childDocument.EntryNodeId = "ChildStart";
var block = new BlockNodeModel
{
    Id = "InspectBlock",
    Title = "检测子流程",
    SubDocument = childDocument
};
```

父文档编译时会递归编译 `SubDocument`，并检查子文档循环引用。

### 8.2 输入映射

输入映射将父作用域数据写入子 Context：

- Literal；
- 父变量；
- 父节点绑定。

```csharp
block.InputMappings.Add(new BlockInputMapping
{
    TargetVariableName = "PartId",
    Source = E_BlockInputSource.ParentVariable,
    ParentVariableName = "CurrentPartId"
});
```

### 8.3 输出映射

输出映射将子流程结果写回父变量：

- 子变量；
- 子节点绑定。

```csharp
block.OutputMappings.Add(new BlockOutputMapping
{
    TargetVariableName = "InspectPassed",
    Source = E_BlockOutputSource.ChildNodeBinding,
    ChildBinding = new WorkflowBindingKey("ChildDecision", "Value")
});
```

子 Context 默认不复制父变量，并隔离 NodeOutputs。进入和返回子流程的数据都必须通过 InputMapping/OutputMapping 显式声明。

双 UI 属性面板在选中 Block 后提供“编辑输入/输出映射…”入口。双击 Block 会弹出节点窗口，在其中的子画布里编辑子流程，左侧工具箱可双击或拖放添加节点。

---

## 9. JSON 保存、打开与迁移

### 9.1 直接使用 JSON Store

```csharp
using DP.WorkFlow.Persistence.Json;

var store = new WorkflowDocumentJsonStore(nodeCatalog);

store.SaveToFile(document, @"D:\Workflows\Clamp.json");
var loaded = store.LoadFromFile(@"D:\Workflows\Clamp.json");

WorkflowDocument restored = loaded.Document;
WorkflowMigrationReport migration = loaded.Migration;
```

也可使用字符串：

```csharp
string json = store.Serialize(document);
WorkflowLoadResult loaded = store.Deserialize(json);
```

保存采用临时文件加原子替换。当前 Schema 为 4，显式保存根入口和子文档；Schema 1/2/3 会迁移，高于当前版本的文档会被拒绝。

迁移后应：

1. 展示 `WorkflowMigrationReport`；
2. 检查迁移警告；
3. 执行编译诊断；
4. 由用户确认后另存为 Schema 4；
5. 在迁移验证完成前保留旧文件以便回退。

### 9.2 使用文档工作区

```csharp
using var workspace = new WorkflowDocumentWorkspace(nodeCatalog);

var navigator = workspace.New("新流程");
workspace.SaveAs(@"D:\Workflows\NewWorkflow.json");

navigator = workspace.Open(@"D:\Workflows\Clamp.json");
Console.WriteLine(workspace.IsDirty);
Console.WriteLine(workspace.LastMigrationReport);
```

工作区提供：

- 新建、打开、保存、另存为；
- 脏状态；
- 最近 10 个文件；
- 根画布保存；
- 迁移报告。

当前最近文件列表仅在工作区实例生命周期内保存，尚未跨进程持久化。

---

## 10. 嵌入 WinForms 工作台

### 10.1 初始化

```csharp
using DP.WorkFlow;
using DP.WorkFlow.UI;
using DP.WorkFlow.UI.WinForms;

var nodeCatalog = new WorkflowNodeCatalog()
    .RegisterStandardNodes()
    .RegisterCompositeNodes();

var workspace = new WorkflowDocumentWorkspace(nodeCatalog);
var navigator = workspace.New("设备流程");

var studio = new WorkflowStudioControl
{
    Dock = DockStyle.Fill,
    Workspace = workspace,
    ConfirmDiscardChanges = () =>
        MessageBox.Show(
            "当前流程尚未保存，是否放弃修改？",
            "确认",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes
};

studio.InteractionError += (_, message) =>
    MessageBox.Show(message, "工作流错误", MessageBoxButtons.OK, MessageBoxIcon.Error);

Controls.Add(studio);
```

设置 `Workspace` 后，Studio 会接管新建、打开、保存、另存为、脏状态和 Navigator 切换。

### 10.2 接入运行时

```csharp
var context = new WorkflowContext(services);
var host = new WorkflowRuntimeHost(nodeCatalog, handlers);

// 每次正式运行前，用根文档重新 Configure。
host.Configure(workspace.Navigator!.RootDocument, context);

var runtimeBinding = new WorkflowStudioRuntimeBinding(host, navigator)
{
    AutoConfigureBeforeRun = true,
    RunContext = context
};
studio.RuntimeBinding = runtimeBinding;
```

> `AutoConfigureBeforeRun` 会在每次新运行前编译目标画布。位于根画布时运行整个流程；进入 Block 后运行按钮会变为“运行当前子流程”，只编译并运行当前子画布。若关闭自动配置，则由宿主通过 `ConfigureBeforeRun` 或直接调用 Host.Configure。运行中不能 Configure。

释放窗口时同时释放：

```csharp
runtimeBinding.Dispose();
host.Dispose();
workspace.Dispose();
```

---

## 11. 嵌入 WPF 工作台

```csharp
using DP.WorkFlow;
using DP.WorkFlow.UI;
using DP.WorkFlow.UI.Wpf;

var nodeCatalog = new WorkflowNodeCatalog()
    .RegisterStandardNodes()
    .RegisterCompositeNodes();

var workspace = new WorkflowDocumentWorkspace(nodeCatalog);
var navigator = workspace.New("设备流程");

var studio = new WorkflowStudioControl
{
    Workspace = workspace,
    ConfirmDiscardChanges = () =>
        MessageBox.Show(
            "当前流程尚未保存，是否放弃修改？",
            "确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes
};

Content = studio;
```

RuntimeHost 和 RuntimeBinding 的接法与 WinForms 相同。WPF 控件会通过 Dispatcher 切回 UI 线程。

---

## 12. 设计器操作说明

### 节点和连接

- 从工具箱创建节点；
- 拖动节点移动；
- 从输出端口拖到输入端口创建连接；
- 选中节点后会显示左、上、右、下四个落点；
- 将紫色输入端口或绿色输出端口拖到落点，可直接调整端口所在边；
- 输出端口拖离节点一定距离后自动进入创建连线模式；
- 节点和端口不提供右键端点选择菜单；需要调整端口所在边时直接拖动端口；
- 单击连接选择，按 Delete 删除；
- 拖动正交连线的中间水平/垂直线段可整体调整该段；
- 双击连接增加 Waypoint；
- 拖动 Waypoint 修改路径；
- Shift+单击 Waypoint 删除路径点。

### 视口

- 鼠标滚轮：以指针为中心缩放；
- 中键或右键拖动：平移；
- FitToView：自动适配全部节点。

### 多选和布局

- Ctrl+单击：追加或移除选择；
- 空白区域拖动：框选；
- Ctrl+框选：追加框选；
- 多选后拖动：整体移动；
- Delete：批量删除；
- 支持左右、顶底、水平中心、垂直中心对齐；
- 支持水平和垂直等距分布；
- 支持按连接层次自动布局。

### Undo/Redo

- Ctrl+Z：Undo；
- Ctrl+Y：Redo；
- 节点、连接、Waypoint、批量布局和 Block 映射均进入共享撤销栈。

---

## 13. 诊断与运行阻断

设计期诊断覆盖：

- 节点 ID、Start 和可达性；
- 端口方向和连接基数；
- 重复或无效连接；
- 并行分支和共同汇聚；
- Block 递归编译；
- 绑定来源、成员路径、类型和作用域可见性。

诊断面板中存在 Error 时，Studio 会阻止运行。双击诊断可定位节点。

兼容旧定义的防御性测试可显式关闭编译期绑定检查：

```csharp
var compiler = new WorkflowCompiler(nodeCatalog, validateBindings: false);
```

生产流程不建议关闭。

---

## 14. 生命周期控制

```csharp
host.Pause();
host.Resume();
host.Cancel();
```

人工 Pause 与 ExternalHold 相互独立：

```csharp
host.AddExternalHold("SafetyDoorOpen");
host.AddExternalHold("EmergencyCircuit");

host.RemoveExternalHold("SafetyDoorOpen");
host.RemoveExternalHold("EmergencyCircuit");
```

仅当人工 Pause 已 Resume 且全部 Hold 都移除后，流程才会继续。Hold 原因应使用稳定字符串 Key。

重新开始前可清理当前引擎：

```csharp
await host.ResetAsync();
```

`ResetAsync` 会取消并释放当前引擎，但保留 Host 的配置和 Context 引用。

---

## 15. 快照、Trace 和运行监视

### 15.1 快照

```csharp
WorkflowRuntimeSnapshot snapshot = host.GetSnapshot();

Console.WriteLine(snapshot.RunId);
Console.WriteLine(snapshot.State);
Console.WriteLine(string.Join(",", snapshot.ActiveNodeIds));
```

快照是不可变对象，可安全交给 UI。主要内容包括：

- RunId 和 Sequence；
- 当前状态和耗时；
- 活动节点与活动 Token；
- ParallelScope；
- ChildWorkflow；
- 节点运行信息；
- Pause/Hold 状态。

### 15.2 Trace

```csharp
var batch = host.Engine?.GetTraceBatch();
if (batch is not null)
{
    foreach (var entry in batch.Entries)
        Console.WriteLine($"{entry.Sequence} {entry.NodeId} {entry.Step} {entry.Message}");
}
```

Trace 使用有界缓存。双 UI 监视页支持按节点、Token、Scope、步骤和消息筛选，并可将当前筛选结果导出为 UTF-8 CSV。

观察者事件异常由 Runtime 隔离，不会因为监控 UI 抛错而中断工作流。

---

## 16. 自定义节点

### 16.1 定义模型

```csharp
[WorkflowNode("ReadSensor", DisplayName = "读取传感器", Category = "Device/Sensor")]
public sealed class ReadSensorNodeModel : WorkflowNodeModel
{
    public override string NodeType => "ReadSensor";

    public string SensorKey { get; set; } = string.Empty;
}

public sealed record ReadSensorOutput(double Value, string Unit);
```

节点模型只保存可持久化配置，不应引用 WinForms、WPF、Control、Brush、Dispatcher 等 UI 类型。

### 16.2 定义 Handler

```csharp
public sealed class ReadSensorNodeHandler : WorkflowNodeHandler<ReadSensorNodeModel>
{
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(
        ReadSensorNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var sensor = (ISensorService?)context.Services.GetService(typeof(ISensorService))
            ?? throw new InvalidOperationException("未注入 ISensorService。");

        var value = await sensor.ReadAsync(node.SensorKey, cancellationToken);
        context.Trace("Read", $"读取 {node.SensorKey} 完成。");
        return NodeExecutionResult.Continue(
            WorkflowPorts.Success,
            new ReadSensorOutput(value, "mm"));
    }
}
```

Handler 必须遵守 CancellationToken；等待设备、网络或计时器时不得阻塞 UI 线程。

### 16.3 注册描述符和 Handler

```csharp
nodeCatalog.Register(
    WorkflowNodeDescriptor.Create<ReadSensorNodeModel, ReadSensorOutput>(
        ports: new[]
        {
            WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
            WorkflowPortDescriptor.Output(WorkflowPorts.Success)
        }));

handlers.Register(new ReadSensorNodeHandler());
```

将输出类型声明为 `ReadSensorOutput` 后，设计期绑定分析器会生成 `Value` 和 `Unit` 候选。

### 16.4 稳定性要求

- `NodeType` 是持久化协议，不要随意改名；
- 端口 Key 是图协议，不要使用本地化显示文本；
- 配置属性应可由 System.Text.Json 序列化；
- 公共输出类型应稳定；
- 设备对象、线程对象和 UI 控件应通过服务注入，不应写入节点模型；
- 节点升级时应提供版本迁移，而不是静默改变旧配置含义。

---

## 17. 推荐宿主组织方式

生产程序建议集中创建一个 Composition Root：

```csharp
public sealed record WorkflowServices(
    WorkflowNodeCatalog Nodes,
    WorkflowNodeHandlerCatalog Handlers,
    IServiceProvider Services);

public static WorkflowServices CreateWorkflowServices()
{
    var nodes = new WorkflowNodeCatalog()
        .RegisterStandardNodes()
        .RegisterCompositeNodes();

    var handlers = new WorkflowNodeHandlerCatalog()
        .Register(new StartNodeHandler())
        .Register(new EndNodeHandler())
        .Register(new ActionNodeHandler())
        .Register(new DecisionNodeHandler())
        .Register(new WaitFunctionNodeHandler())
        .Register(new DelayNodeHandler())
        .Register(new LoopNodeHandler())
        .Register(new JumpNodeHandler())
        .Register(new ValueCompareNodeHandler())
        .Register(new StringCompareNodeHandler())
        .Register(new ParallelAllNodeHandler())
        .Register(new WaitAllInputsCompletedNodeHandler())
        .RegisterCompositeNodeHandlers();

    var actions = new WorkflowActionRegistry();
    var conditions = new WorkflowConditionRegistry();
    var services = new WorkflowServiceProvider()
        .Add<IWorkflowActionRegistry>(actions)
        .Add<IWorkflowConditionRegistry>(conditions);

    return new WorkflowServices(nodes, handlers, services);
}
```

这样设计器、持久化和 Runtime 使用同一套节点目录，避免“能打开但不能运行”或“能运行但工具箱缺节点”。

---

## 18. 测试建议

自定义节点至少应覆盖：

1. Literal 输入；
2. 上游绑定输入；
3. 取消；
4. 超时；
5. Handler 异常；
6. JSON 往返；
7. 编译期端口和绑定诊断；
8. 并行作用域下的输出可见性；
9. Block 输入输出映射；
10. RuntimeHost Pause/Hold/Reset。

测试可直接构造 `WorkflowCanvasModel`，无需启动 WinForms/WPF。

---

## 19. 可恢复故障与告警子流程

设备节点配置 `Interrupt.AlarmCode > 0` 且返回故障时，引擎只对 `RequestRecovery` 策略调用恢复协调器。宿主可将专用告警块注册为协调器：

```csharp
var warningBlock = canvas.Nodes
    .Select(item => item.Node)
    .OfType<WarningHandlerBlockNodeModel>()
    .Single();

var coordinator = new WorkflowWarningHandlerCoordinator(
    warningBlock,
    nodeCatalog,
    handlers);

var services = new WorkflowServiceProvider()
    .Add<IWorkflowFaultRecoveryCoordinator>(coordinator);
```

恢复期间引擎添加 `Recovery:{NodeId}` ExternalHold。告警子流程必须以 `WarnRetryCurrentNode`、`WarnJumpToNode` 或 `WarnStopStation` 产生 `WarningResolution`。`StopRun` 和 `StopRun` 不会进入恢复流程。

---

## 20. 当前限制

当前版本仍需注意：

- 最近文件尚未跨进程保存；
- 节点实例动态端口已经支持，但绑定树中的成员 Schema 仍主要依赖描述符声明的静态输出 CLR 类型；
- Dictionary、List 和复杂对象已支持通用 JSON 编辑，针对特定业务集合的表格化编辑器仍可继续扩展；
- Trace 已支持筛选、导出、暂停滚动和节点耗时趋势；
- 旧 JSON 的复杂 Block、告警子画布和部分节点版本迁移仍需扩展；
- 可版本化子流程模板引用尚未实现；
- Studio 自动运行绑定会在运行前重新编译当前正式文档；自定义宿主仍应显式调用 RuntimeHost.Configure。

---

## 21. 常见问题

### 点击运行提示“尚未 Configure”

先调用：

```csharp
host.Configure(rootDocument, context);
```

### 打开旧 JSON 提示节点类型未注册

在创建 `WorkflowDocumentJsonStore` 或 `WorkflowDocumentWorkspace` 前注册对应节点描述符。

### 绑定候选为空

检查：

- 来源节点是否位于消费者上游；
- 是否跨越了尚未汇聚的并行兄弟分支；
- 描述符是否声明输出 CLR 类型；
- 成员类型是否兼容目标 `WorkflowInput<T>`；
- EntryNodeId 是否正确。

### Block 子流程写入的变量在父流程看不到

这是默认隔离行为。添加 `BlockOutputMapping` 显式导出。

### Pause 后移除 Hold 仍未继续

人工 Pause 和 ExternalHold 是独立条件。还需要调用 `Resume()`，并确认所有 Hold 原因都已移除。

### 修改流程后运行的仍是旧版本

编辑器修改的是 Canvas，RuntimeHost 使用的是上次 Configure 得到的编译 Definition。停止当前运行并重新 Configure。

---

## 22. 延伸文档

- 架构总方案：`../../WORKFLOW_REBUILD_MASTER_PLAN.md`
- UI 说明：`ui/overview.md`
- 当前进度：`progress.md`
- 旧类型迁移表：`migration/type-map.md`
- 架构决策：`decisions/0001-*.md` 至 `decisions/0009-*.md`
