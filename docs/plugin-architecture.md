# 插件架构

## 插件包

每个插件包使用独立目录和 `plugin.json`：

```text
plugins/
└── trajectory-planning/
    ├── plugin.json
    ├── Trajectory.Runtime.dll
    ├── Trajectory.Studio.dll
    ├── Trajectory.Studio.WinForms.dll
    └── Trajectory.Studio.Wpf.dll
```

```json
{
  "manifestVersion": 1,
  "pluginId": "trajectory-planning",
  "version": "1.0.0",
  "requires": [],
  "modules": {
    "runtime": ["Trajectory.Runtime.dll"],
    "studio": ["Trajectory.Studio.dll"],
    "winForms": ["Trajectory.Studio.WinForms.dll"],
    "wpf": ["Trajectory.Studio.Wpf.dll"]
  }
}
```

程序集路径只能位于插件包目录内。当前只接受 `manifestVersion: 1`；`requires` 中的依赖插件必须存在且依赖图无环，Module 按依赖拓扑顺序装载。`PluginId`、Runtime `ExtensionId`、页面提供器 `ExtensionId` 和平台 `RendererKey` 均必须唯一。插件在进程内运行，宿主只能装载受信任程序集。

## Runtime Module

Runtime 插件实现 `IWorkflowRuntimePluginModule`，并在一次注册中贡献节点描述和 Handler：

```csharp
public sealed class TrajectoryRuntimePlugin : IWorkflowRuntimePluginModule
{
    public string ExtensionId => "Trajectory.Runtime";

    public void Register(WorkflowRuntimePluginCatalog extensions)
    {
        extensions.Nodes.Register(/* node descriptor */);
        extensions.Handlers.Register(/* node handler */);
    }
}
```

宿主只需要一次组合调用，并在创建工作区和运行宿主前冻结：

```csharp
var loader = new WorkflowPluginLoader();
var plugins = new WorkflowRuntimePluginCatalog(nodes, handlers);
plugins.LoadPlugins(pluginDirectory, loader);
plugins.Freeze();
```

`Freeze()` 会验证每个节点工厂返回的 ModelType/NodeType，并确认每种已注册节点恰好匹配一个 Handler；冻结后 Node、Handler 和 Runtime Module 目录不再接受注册。内置 `Standard`、`Composite`、`Motion`、`Process`、`Vision` 均提供同形的 Runtime Module，宿主不再逐个列出 Handler。

Handler 注册可以同时声明固定或配置相关的 `WorkflowRuntimeCapabilityRequirement`。`WorkflowRuntimeBinder` 将当前节点配置对应的要求冻结到 `WorkflowBoundExecutionPlan`；`WorkflowRuntimeHost` 在任何运行准备 Module 和节点执行之前递归检查根计划及子计划。节点执行通过 `GetRequiredCapability<T>()` 获取已预检能力；可选观察能力和受信任脚本仍可使用兼容 `Services` 入口。

```csharp
handlers.Register(
    new AxisActionNodeHandler(),
    WorkflowRuntimeCapabilityRequirement.Require<IWorkflowAxisService>());
```

## 视觉能力装配

`WorkflowImageRuntimePluginModule` 是视觉节点程序集唯一的 Runtime Module，成组贡献18种 NodeModel、Handler 与中立能力要求。节点保存 `WorkflowInput<T>`、明确参数和 ROI 配置，不保存 SDK 对象。

最终宿主固定装配 `IImageFileReader`、`ICameraCapture`、`IBlobAnalyzer`、`IColorAnalyzer`、`IEdgeMeasurer` 和 `ITemplateLocator` 等中立接口的实现；新增预处理、Region、Blob筛选、卡尺、鲁棒拟合和姿态定位服务见[算子装配](nodes/vision-operators.md)。算法和厂商边界位于同级 DP.Vision，不反向依赖 Workflow。`WorkflowRuntimeHost` 使用统一能力预检，文件夹清单与帧租约分别由采集会话和帧仓准备。

当前没有视觉 Profile/Provider 候选选择器、旧 Importer 或独立 vision 分组装载器；不自动故障切换。目录 Freeze 不是服务容器 Freeze，宿主必须保证运行中不更换实例。完整示例见[视觉使用说明](nodes/new-vision-file-pipeline.md)。

## Studio Module

UI 无关页面实现 `IWorkflowNodeEditorPageProvider`，`studio` 分组可以通过 `IWorkflowStudioPluginModule` 为 WinForms/WPF 共同注册页面提供器。平台 Module 分别实现：

- `IWorkflowWinFormsStudioExtension`
- `IWorkflowWpfStudioExtension`

简单插件可以在平台 Module 中一次贡献页面提供器和 Renderer；需要共享页面模型时，则由 `studio` Module 注册页面、平台 Module 只注册对应 Renderer：

```csharp
public void Register(WorkflowWinFormsStudioExtensionCatalog extensions)
{
    extensions
        .RegisterPageProvider(new TrajectoryPageProvider())
        .RegisterRenderer(new TrajectoryWinFormsRenderer());
}
```

宿主自动装载：

```csharp
studio.LoadNodeEditorPlugins(pluginDirectory, loader);
```

详情页匹配顺序：

```text
节点编辑副本
  → PageProvider.CanProvide
  → PageId 槽位按 Priority 解析
  → RendererKey 精确查找平台 Renderer
  → WinForms Control / WPF FrameworkElement
```

`Order` 只控制显示顺序，`Priority` 只控制同一 `PageId` 的替换。二者不得混用。自定义页面必须直接提供强类型 Model 和显式 `RendererKey`；平台 Renderer 同时声明 `ModelType`，不再通过弱类型包装对象传递数据。缺少当前平台 Renderer 或 ModelType 不匹配时，节点工作台在创建任何页面控件前失败。页面始终编辑 `EditingNode`；确定时作为一次事务提交，取消时丢弃。Block 映射入口只依赖 `IWorkflowBlockMappingNode`，平台 Studio 不引用具体 Composite 节点程序集。

## 当前内置插件

- `WorkflowStandardRuntimePluginModule`：全部标准节点和 Handler。
- `WorkflowCompositeRuntimePluginModule`：Block 等复合节点和 Handler。
- `WorkflowMotionRuntimePluginModule`：IO、Axis、气动和读码器节点及 Handler。
- `WorkflowProcessRuntimePluginModule`：工艺、产品流、恢复和机器人节点及 Handler。
- `WorkflowImageRuntimePluginModule`：新版18种视觉节点、Handler 与能力要求。
- `VisionWinFormsStudioExtension`：`DP.Vision.FrameEditor` 页面与原生 WinForms Renderer。
- `VisionWpfStudioExtension`：同一共享页面与原生 WPF Renderer。
