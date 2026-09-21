# 图像采集 Provider：实施设计与验收基线

状态：**Proposed，尚未实施**  
适用范围：相机/采集卡等硬件图像源；文件和文件夹读取继续使用独立契约。  
目的：作为后续实现、代码评审和验收的对照基线。本文明确区分“当前事实”“目标状态”和“延期能力”。

## 1. 当前事实

当前正式链路为：

```text
Vision.CaptureFrame节点（CameraId）
  → CaptureVisionFrameNodeHandler
  → 宿主唯一ICameraCapture实例
  → HalconCameraCapture
  → HFramegrabber / HImage
  → 复制为中立IImageSource
  → ImageFrame
  → WorkflowVisionFrameScope
  → NodeOutput
```

当前限制：

1. `ICameraCapture`支持替换一个实现，不支持同一宿主组合多个Provider。
2. `CameraId`直接使用`接口名|设备名`，泄漏HALCON设备表达。
3. 示例直接引用并`new HalconCameraCapture()`，HALCON尚不是动态Provider插件。
4. 每次采集打开/关闭设备，没有统一的设备发现、长连接、健康、互斥和占用者诊断。
5. 两个流程访问同一设备时没有平台级协调，结果依赖SDK行为。
6. `ImageFrame`只携带FrameId和图像租约，不自带SourceId、采集时间、设备序号等来源事实。
7. HALCON路径先从SDK指针复制到临时数组，再经`VisionImage.CopyFrom`克隆到最终数组；正确但不是低拷贝实现。

这些是现状，不应在设计文档中描述为已完成能力。

## 2. 核心决策

### 2.1 Provider与Workflow节点是两类插件

- Workflow节点插件贡献NodeModel、Handler、能力要求和可选Studio页面。
- 采集Provider插件贡献硬件Adapter、设备工厂和配置校验，不注册Workflow节点。
- 两类Module可以位于同一部署包，但必须实现不同接口并分别组合。

### 2.2 Workflow只保存逻辑SourceId

流程文档只表达：

```text
从Camera.Top采集一帧
```

不表达：

```text
使用HALCON GigEVision2打开某IP
使用海康MvCamCtrl.NET打开某序列号
```

逻辑SourceId由机器配置绑定到唯一Provider和Provider设备配置。替换Provider不要求修改工作流文档。

### 2.3 Provider拥有硬件，运行拥有图像

```text
站点级Acquisition Runtime
  └─ Provider、设备连接、互斥、健康和采集

根运行级Vision资源
  └─ 已采集原图、过程图、预览和结果查看生命周期
```

Provider可以被多个运行请求；Provider返回的图像进入请求运行自己的资源作用域。禁止因为设备是公共的而共享FrameScope。

### 2.4 显式选择，不自动故障切换

一个SourceId在一份已发布配置中只绑定一个Provider。Provider失败时明确失败，不自动尝试另一个Provider。切换Provider必须生成并发布新配置，且只影响新运行。

### 2.5 公共契约不出现厂商SDK类型

公共程序集只允许中立接口和值对象，不允许`HImage`、`HFramegrabber`、海康/大华/Basler句柄、厂商枚举或原生指针进入契约。

## 3. 目标依赖结构

```text
DP.Vision
  IImageSource / ImageFrame / ImageInfo / 租约与缓冲

DP.Vision.Acquisition.Abstractions              （新增，netstandard2.0）
  Provider、设备、Source、请求、结果、错误契约
  ↓ 仅引用DP.Vision

DP.Vision.Acquisition.Runtime                   （新增）
  Provider组合、Source绑定、设备生命周期、互斥和路由
  ↓ 引用Acquisition.Abstractions

DP.Vision.Halcon                               （演进现有项目）
DP.Vision.Hikvision                            （未来）
DP.Vision.Dahua                                （未来）
DP.Vision.Basler                               （未来）
  各自引用Acquisition.Abstractions + 厂商SDK
  不引用DP.WorkFlow

DP.WorkFlow.Nodes.Vision
  Capture节点和Workflow身份Adapter
  ↓ 引用Workflow.Abstractions + Acquisition.Abstractions

应用宿主
  加载Provider插件、发布Provider组合、加载机器Source配置
```

禁止依赖：

```text
DP.Vision.*Provider → DP.WorkFlow
DP.Vision.Acquisition.Runtime → DP.WorkFlow
DP.WorkFlow Kernel → DP.Vision
公共采集契约 → 任意厂商SDK
```

## 4. 统一语言和身份

| 名称 | 含义 | 示例 |
|---|---|---|
| ProviderId | Provider实现的稳定身份 | `dp.vision.halcon` |
| SourceId | 工作流和操作员使用的逻辑源身份 | `Camera.Top` |
| ProviderBindingId | Provider配置内的设备绑定身份 | `top-camera` |
| ResourceKey | 用于识别同一物理硬件并协调互斥 | `camera:serial:DA123456` |
| CaptureId | 一次采集意图和结果的唯一身份 | GUID |
| DeviceSequence | 设备可选提供的帧序号 | 14582 |
| ProviderCompositionId | 一次完整Provider组合的不可变身份 | 内容摘要/GUID |
| DeviceLease | Provider Runtime授予的设备使用权 | 某次Capture或某次Run |

规则：

- SourceId唯一，但SourceId唯一不等于物理设备唯一。
- 多个SourceId可有意映射同一ResourceKey；它们必须共享同一互斥状态，且ProviderId和ProviderBindingId必须一致，否则配置拒绝发布。
- Provider打开设备后应尽可能报告序列号/MAC等规范身份；与配置ResourceKey不一致时必须失败或进入需人工确认状态，不能继续形成两个锁域。
- CaptureId由Acquisition Runtime生成，不能用预览Sequence或NodeExecutionCount代替。
- FrameId可采用CaptureId；像素变化产生派生帧时必须生成新的FrameId并记录ParentFrameId。

## 5. 公共契约草案

以下为目标形状；命名可在实现前做一次API评审，但职责不得合并回单一`ICameraCapture`。

### 5.1 值对象

```csharp
public sealed record VisionSourceReference(string SourceId);

public enum EVisionTriggerMode
{
    KeepCurrent,
    FreeRun,
    Software,
    External
}

public sealed record VisionCaptureRequest(
    TimeSpan Timeout,
    double? ExposureMicroseconds = null,
    double? GainDecibels = null,
    EVisionTriggerMode TriggerMode = EVisionTriggerMode.KeepCurrent);

public sealed record VisionCaptureMetadata(
    string CaptureId,
    string SourceId,
    string ProviderId,
    string ResourceKey,
    DateTimeOffset CapturedAtUtc,
    long? DeviceSequence);
```

公共物理量必须有明确单位。Provider不能把厂商原始曝光刻度冒充微秒或把原始Gain刻度冒充分贝；无法转换时应拒绝该可选参数，并要求在Provider配置中设置。

### 5.2 面向采集消费者的深Module

```csharp
public sealed record VisionAcquisitionOwner(
    string OwnerId,
    string OperationId,
    string? DisplayName = null);

public interface IVisionAcquisition
{
    ValueTask<VisionCapturedImage> CaptureAsync(
        VisionSourceReference source,
        VisionCaptureRequest request,
        VisionAcquisitionOwner owner,
        CancellationToken cancellationToken);
}

public sealed class VisionCapturedImage : IDisposable
{
    public ImageFrame Frame { get; }
    public VisionCaptureMetadata Metadata { get; }
    public void Dispose();
}
```

`IVisionAcquisition`是Workflow和其他业务唯一需要学习的采集Interface。它内部隐藏Provider选择、连接复用、互斥、超时和来源元数据。`OwnerId`用于根运行或上层任务身份，`OperationId`用于节点执行/采集意图身份；二者都不是设备句柄。

### 5.3 Provider Adapter契约

公共Provider接口保持最小；发现和健康查询使用可选能力，不强迫所有SDK伪造支持：

```csharp
public interface IVisionAcquisitionProvider : IAsyncDisposable
{
    string ProviderId { get; }

    ValueTask<IVisionAcquisitionDevice> OpenAsync(
        string providerBindingId,
        CancellationToken cancellationToken);
}

public interface IVisionAcquisitionDevice : IAsyncDisposable
{
    VisionDeviceIdentity Identity { get; }

    ValueTask<VisionProviderFrame> CaptureAsync(
        VisionCaptureRequest request,
        CancellationToken cancellationToken);
}

public interface IVisionDeviceDiscovery
{
    ValueTask<IReadOnlyList<VisionDeviceDescriptor>> DiscoverAsync(
        CancellationToken cancellationToken);
}

public interface IVisionDeviceHealthSource
{
    ValueTask<VisionDeviceHealth> GetHealthAsync(
        CancellationToken cancellationToken);
}

public sealed class VisionProviderFrame : IDisposable
{
    public IImageSource Image { get; }
    public DateTimeOffset CapturedAtUtc { get; }
    public long? DeviceSequence { get; }
    public void Dispose();
}
```

Provider返回的`VisionProviderFrame`由Acquisition Runtime立即加入CaptureId和Source来源事实，并包装为`VisionCapturedImage`；不得向上暴露厂商对象。Runtime以`ResourceKey`缓存或打开设备，不能让每个SourceId各自打开同一物理设备。

### 5.4 Provider插件Module

```csharp
public interface IVisionAcquisitionProviderModule
{
    string ExtensionId { get; }
    void Contribute(IVisionAcquisitionProviderContributionBuilder builder);
}

public interface IVisionAcquisitionProviderContributionBuilder
{
    void Register(VisionAcquisitionProviderRegistration registration);
}

public sealed record VisionAcquisitionProviderRegistration(
    string ProviderId,
    string Version,
    Func<IVisionAcquisitionProvider> Factory);
```

Module只提交Provider候选工厂和元数据，不接触当前正式Provider目录。Composer完整验证后一次发布不可变组合。正式实现中的工厂上下文可以增加受限日志和Provider私有配置入口，但不能暴露Workflow ServiceProvider。

## 6. Source绑定配置

工作流文档：

```json
{
  "nodeType": "Vision.CaptureFrame",
  "sourceId": "Camera.Top",
  "exposureMicroseconds": 1500,
  "gainDecibels": 2.5,
  "triggerMode": "External"
}
```

机器级公共配置：

```json
{
  "sources": [
    {
      "sourceId": "Camera.Top",
      "providerId": "dp.vision.hikvision",
      "providerBindingId": "top-camera",
      "resourceKey": "camera:serial:DA123456",
      "sharingPolicy": "ExclusiveOperation"
    }
  ]
}
```

Provider私有配置由Provider拥有，例如：

```json
{
  "bindings": {
    "top-camera": {
      "serialNumber": "DA123456",
      "packetSize": 9000,
      "triggerLine": "Line0",
      "pixelFormat": "Mono8"
    }
  }
}
```

公共配置不解释Provider私有字段。Provider负责自己的配置模型、校验、可选管理UI和敏感信息处理。

## 7. Provider组合与加载

### 7.1 包结构

```text
plugins/
└─ dp.vision.hikvision/
   ├─ plugin.json
   ├─ DP.Vision.Hikvision.dll
   ├─ 厂商托管程序集
   └─ 厂商原生依赖
```

建议Manifest继续采用统一包格式，但增加独立Module组：

```json
{
  "manifestVersion": 1,
  "pluginId": "dp.vision.hikvision",
  "version": "1.0.0",
  "modules": {
    "visionAcquisition": ["DP.Vision.Hikvision.dll"]
  }
}
```

DP.Vision不能依赖位于Workflow.Abstractions中的现有Loader。实施时有两个合法选择：

1. 抽取与领域无关的受信任插件包读取器到Platform Extensibility；Workflow和Vision分别提供Module组合器。
2. DP.Vision先实现最小Provider Loader，保持Manifest兼容，后续再抽取重复部分。

不得让DP.Vision反向引用Workflow以复用Loader。

### 7.2 候选发布流程

```text
发现插件包
→ 验证Manifest与路径
→ 加载Provider Module
→ 每Module写入私有ContributionBuilder
→ 验证ProviderId/ExtensionId/版本/工厂唯一
→ 创建候选Provider组合
→ 验证机器Source绑定
→ 生成ProviderCompositionId
→ 一次发布不可变组合
```

失败要求：

- 任一Module抛异常时丢弃候选；现有正式组合不变。
- 重复ProviderId、SourceId或非法路径在发布前失败。
- 缺少原生依赖、CPU架构不匹配和SDK初始化失败必须带ProviderId诊断。
- 不允许“HALCON失败后自动尝试海康”等运行时隐式回退。
- Provider组合与Workflow Runtime组合分别拥有CompositionId；运行制品同时记录二者以及机器Source配置修订。
- 已开始运行持有自己验证过的Provider组合快照；新组合只供新运行使用。若原生SDK不支持版本并存，则有活动运行/设备Session时拒绝发布新组合。

## 8. 设备生命周期

目标状态机：

```text
Created
  → Opening
  → Open
  → Capturing
  → Open
  → Closing
  → Closed

任何阶段可进入Faulted；Faulted不能伪装为Closed或自动成功。
```

所有权：

- Provider Composition拥有Provider实例。
- Acquisition Runtime拥有已打开设备Session。
- DeviceLease拥有一次操作或一次根运行的设备使用权。
- `VisionCapturedImage`拥有返回给调用方的中立图像租约。
- Workflow FrameScope只拥有进入该运行后的图像租约，不拥有相机Session。

关闭顺序：

```text
禁止新请求
→ 取消/等待在途采集
→ 释放DeviceLease
→ 关闭设备Session和SDK回调
→ 释放Provider
→ 退役Provider加载上下文
```

有活动设备Lease时禁止热卸载Provider。设备Session默认按`ResourceKey`惰性打开并复用，由Runtime串行化Open/Close状态转换；Provider如有专用线程或COM/SDK线程亲和要求，应在自身内部维持该线程，不要求调用方用`Task.Run`模拟。

取消Token只表示请求取消，不证明原生SDK调用已经退出。Provider必须同时配置SDK级超时；若原生调用仍在途，Runtime不能提前释放同一ResourceKey的Lease并启动下一次采集，而应等待受控退出或将设备标记为Faulted并阻断后续使用。

## 9. 并发与共享策略

```csharp
public enum EVisionSourceSharingPolicy
{
    ExclusiveOperation,
    Serialized,
    ExclusiveRun,
    Broadcast
}
```

### 第一阶段必须实现

- `ExclusiveOperation`：同一ResourceKey同时只允许一个采集；冲突立即确定性失败并报告占用者。
- `Serialized`：按到达顺序串行；等待支持取消和超时，不能无限排队。

### 延期到RunScope完成后

- `ExclusiveRun`：根运行开始取得Lease，结束/退役时释放；依赖AR-01阶段3的RunScope所有权。

### 延期到有真实连续流需求后

- `Broadcast`：单一设备流向多个订阅者发布同一不可变像素内容，各订阅者持有独立租约；必须定义触发、丢帧、序号和背压。不得用“读取全局最新图”假装Broadcast。

安全默认：未配置策略时使用`ExclusiveOperation`并在冲突时失败，不静默共享、不静默排队。所有策略先提供进程内确定性；跨进程仍必须依靠SDK独占、操作系统命名互斥或独立设备进程，不能把进程内Semaphore宣传成跨进程保护。

## 10. Workflow接入

### 10.1 节点模型

短期为减少文档破坏，可保留属性名`CameraId`，但立即将语义改为逻辑SourceId并更新UI说明。待节点迁移基础设施完成后显式迁移为：

```csharp
public VisionSourceReference Source { get; set; }
```

不能同时长期保留`CameraId`和`SourceId`两套路径。

### 10.2 Handler

从：

```csharp
context.GetRequiredCapability<ICameraCapture>()
```

迁移为：

```csharp
context.GetRequiredCapability<IVisionAcquisition>()
```

Handler把`WorkflowExecutionIdentity`转换为中立`VisionAcquisitionOwner`，调用采集后把`VisionCapturedImage.Frame`交给当前运行的图像资源所有者。节点标准输出继续保持`ImageFrame`，避免18种下游节点改成厂商或采集专用类型；`VisionCaptureMetadata`进入本运行的帧来源记录和Trace，不进入普通公共数据。

Provider和Acquisition Runtime不得直接发布Workflow预览。正式预览只能由“节点输出成功提交”事件驱动；取消、Handler返回后的提交失败、输出失效和运行代次切换必须同步撤销或更新预览。这一项与AR-29一起实施，不能复制当前Handler返回前调用`Publish`的行为。

### 10.3 能力声明

`WorkflowImageRuntimePluginModule`对采集节点声明：

```text
IVisionAcquisition
IWorkflowVisionFrameScope（在AR-01阶段3前）
```

不再声明裸`ICameraCapture`。

### 10.4 运行准备

Vision侧准备检查扫描计划中的Capture节点，验证：

- SourceId非空并已发布；
- Provider存在且版本有效；
- Source配置通过Provider校验；
- 请求的触发模式和通用参数被Provider支持；
- `ExclusiveRun`资源能够在根RunScope原子取得。

准备检查不是最终互斥；执行采集时必须再次由Acquisition Runtime原子取得Lease。

## 11. 图像内存与来源事实

Provider输出必须在设备/SDK对象释放后仍然有效。第一阶段允许显式复制，不以零拷贝为目标。

```text
厂商SDK缓冲
→ Provider验证格式与预算
→ 中立IImageSource租约
→ Acquisition Runtime生成CaptureId和Metadata
→ ImageFrame
→ Workflow运行资源
```

规则：

1. 不向公共契约暴露裸指针或厂商图像对象。
2. SDK复用缓冲时必须复制或转移受控租约，不能让下一帧覆盖仍在使用的像素。
3. Provider失败或取消时不发布半成品帧。
4. SourceId、ProviderId、ResourceKey、CaptureId、采集时间和设备序号进入采集元数据与Trace。
5. 下游图像处理产生新像素时生成新FrameId并保留ParentFrameId；不继续冒充原采集帧。
6. 当前HALCON双复制先保留正确性；低拷贝优化必须有峰值内存和吞吐基准，并保持相同租约测试。

## 12. 失败与诊断

公共错误至少区分：

| 类别 | 示例 | 默认处理 |
|---|---|---|
| Source配置错误 | SourceId不存在、Provider缺失 | 运行前失败 |
| 资源冲突 | 同一ResourceKey被其他Run占用 | 确定性失败/按显式Serialized等待 |
| 设备离线 | 打开失败、断线 | 节点故障，不自动换Provider |
| 参数不支持 | 曝光单位/触发模式不支持 | 运行前或执行前明确拒绝 |
| 采集超时 | 外部触发未到 | 节点故障或节点显式Timeout出口（另行决定） |
| 数据错误 | 空帧、像素格式非法、超预算 | 节点故障，不发布输出 |
| SDK缺失 | DLL、许可证、架构不匹配 | Provider组合或宿主启动失败 |

资源冲突诊断必须包含：

```text
SourceId
ResourceKey
请求Run/Node/执行次数
当前占用Run/Node
策略
等待或失败原因
```

不得把设备冲突包装成“未找到图像”或普通False业务结果。

## 13. 分阶段实施

### 实施状态（截至 2026-09-21）

| 阶段 | 状态 | 证据 |
|---|---|---|
| A 公共契约与内存Adapter | **已完成** | `DP.Vision.Acquisition.Abstractions`、HALCON Adapter、内存 Fake；契约与依赖边界用例 |
| B Source Registry与多Provider组合 | **已完成** | `DP.Vision.Acquisition.Runtime`（Composer / Runtime / Source绑定）；组合、路由、并发、内存租约用例 |
| C Workflow迁移到逻辑SourceId | **已完成** | `Vision.CaptureFrame` 改用逻辑源 + `IVisionAcquisition`；AR-29 预览改为已提交输出的派生投影；旧文档一次性迁移 |
| D HALCON正式Provider插件 | **已完成** | `plugin.json` + `HalconAcquisitionProviderPlugin` + 私有配置校验 + 插件目录加载；两个示例不再编译期选择Provider |
| E 第二个真实厂商Provider | **已完成（Basler pylon）** | `DP.Vision.Basler`：官方 NuGet 包 `Basler.Pylon.NET.x64`（免费）+ `plugin.json` + `BaslerAcquisitionProviderPlugin`；`DP.Vision.Acquisition.Integration.Tests` 验证同一组合内两个真实Provider按SourceId各管自己的设备 |
| F RunScope与高级共享模式 | **阻塞（外部依赖）** | `ExclusiveRun` 依赖 AR-01 阶段3；`Broadcast` 待真实连续流需求 |

已实现的共享策略只有 `ExclusiveOperation` 与 `Serialized`；`ExclusiveRun` 与 `Broadcast` 在运行准备阶段被显式拒绝，不使用进程内锁冒充跨进程互斥。

阶段 E 厂商选型实测（2026-09-21，本机联网）：**Basler 是唯一提供官方 NuGet 包的工业相机厂商**——`Basler.Pylon.NET.x64` 无依赖、可从 nuget.org 直接还原、在 net48 与 net8.0-windows 下均编译通过；pylon 相机软件套装与 .NET API 均为免费软件，仅运行期需要安装 pylon 运行时。海康（Hikrobot）官方 SDK 只能从官网手工下载 MVS 客户端，nuget.org 上只有第三方非官方封装（`MvCameraControl.Net` 等，非厂商发布）；大华、大恒同理。因此第二个真实 Adapter 选择 Basler：它能在**没有相机、没有装 SDK** 的机器上完成全部契约与边界验证，只有真实出图路径需要现场验收。

自动化实测（本机 Debug）：`DP.Vision.sln` **650 例 0 失败**（含 net48 与 net8.0 两套目标框架）；`DP.WorkFlow.sln` **811 例 0 失败**，构建 0 警告 0 错误。

**插件包必须自包含厂商依赖（阶段D遗留缺陷，2026-09-21 修复）**：加载器用 `Assembly.GetExportedTypes()`
发现入口，这要求插件程序集的引用全部可解析。阶段D的示例投放只拷了插件程序集本身，漏掉厂商程序集
（HALCON 的 `halcondotnet.dll`、Basler 的 `Basler.Pylon.dll`），而 `ReferenceOutputAssembly="false"`
恰好不会把厂商依赖带进宿主根目录——结果是**插件在运行时整包加载失败**，界面只会显示"Provider 未安装"。
修复：投放目标改为按厂商输出目录的 `*.dll` 通配投放，**但排除宿主已提供的契约程序集**
（`DP.Vision.dll`、`DP.Vision.Acquisition.Abstractions.dll`）——把契约放进插件包会让插件拿到第二份类型，
与宿主的中立接口不是同一个类型，组合阶段必然失败。两个厂商测试工程各加一条回归：
`PluginDirectory_ContainsVendorDependencies`（插件程序集的非框架、非宿主引用必须能在同目录找到）。
示例的诊断信息也改为附带 `Failures` 的真实原因，不再只显示"未安装"。

示例投放后的实测（在两个示例输出目录直接跑加载器）：两个插件包均加载成功、0 失败；
`Camera.Top → dp.vision.halcon`（可用）、`Camera.Side → dp.vision.basler`（**已安装但缺 pylon 运行时**，
给出带 ProviderId 的诊断），组合产出 2 个逻辑源，宿主根目录无任何厂商程序集。

依赖关系：

- 阶段A可以独立开始。
- 阶段B必须采用AR-02b同样的“候选贡献→完整验证→一次发布”原则，但不要求DP.Vision依赖Workflow的具体Composer。
- 阶段C必须同时处理AR-29的正式输出提交后预览；否则新Provider会继续放大预览与正式输出不一致。
- `ExclusiveRun`和根运行持有的设备Lease依赖AR-01阶段3；在此之前不得模拟该能力。
- AR-30的预览无变化读取优化不阻塞Provider迁移，但应在高帧率现场验收前完成。

### 阶段 A：建立契约和内存Adapter，不改设备行为

1. 新建`DP.Vision.Acquisition.Abstractions`。
2. 定义Source、请求、结果、Provider和Device接口。
3. 用Adapter包装当前`HalconCameraCapture`，保持每次打开/关闭及复制语义。
4. 建立内存Fake Provider和契约测试。
5. 不引入热卸载、Broadcast和长连接承诺。

验收：HALCON行为不变；Workflow尚可继续旧入口；公共契约无厂商类型。

### 阶段 B：Source Registry与多Provider组合

1. 新建`DP.Vision.Acquisition.Runtime`。
2. 实现候选Contribution、Composer和不可变ProviderComposition。
3. 实现逻辑Source配置、ProviderBindingId和ResourceKey。
4. 支持同进程同时注册至少两个Fake Provider并按SourceId确定路由。
5. 实现`ExclusiveOperation`和`Serialized`。

验收：两个Provider可并存；重复注册和部分失败不污染正式组合；同ResourceKey冲突可复现。

### 阶段 C：Workflow迁移

1. 新增`IVisionAcquisition`能力装配。
2. Capture Handler改用逻辑SourceId和Acquisition Runtime。
3. Runtime Module能力要求从`ICameraCapture`切换到`IVisionAcquisition`。
4. Studio属性编辑器从自由文本升级为已配置Source候选。
5. 增加输出成功提交后的Vision预览/来源事实发布入口，删除Handler内提前发布。
6. 示例不再直接`new HalconCameraCapture()`作为Workflow能力。
7. 明确现有`CameraId`文档迁移或一次性重建规则。

验收：同一流程文档在不同机器Source映射下可使用不同Provider；不存在Source时在首节点前失败。

### 阶段 D：HALCON成为正式Provider插件

1. `DP.Vision.Halcon`实现Provider Module和设备Adapter。
2. 发布插件Manifest和Provider私有配置校验。
3. 示例通过插件目录加载HALCON，不再编译期选择具体Provider。
4. 保留SDK关闭后中立图像仍有效的真实边界测试。

验收：不安装HALCON插件时非HALCON流程仍可运行；安装但缺SDK时给出Provider级诊断。

### 阶段 E：第二个真实厂商Provider

只有第二个真实Adapter接入后，多Provider接口才算经真实变化验证。建议选一个实际项目必需品牌，不为了证明架构创建空壳。

已落地：选择 **Basler pylon**（理由与厂商选型实测见上文阶段 E 行）。`DP.Vision.Basler` 与 `DP.Vision.Halcon` 结构同构，两者都只通过中立契约被使用。

厂商差异被显式处理，而不是抹平：

- **缺 SDK 的时机不同**。HALCON 的 SDK 缺失是编译期问题（未找到 `halcondotnet.dll` 就不编译采集实现），因此 `HalconCameraCapture.IsSdkEnabled` 是编译期开关；Basler 的托管程序集随 NuGet 包还原、编译期一定在，缺的是**原生运行时**，因此健康探测改为检查进程能否解析 `PylonBase_v10.dll`。两者都必须让采集节点在首节点执行前失败，而不是等到采集时抛原生异常（Basler 缺运行时直接调 API 会抛 `SEHException`）。
- **设备选择器语义不同**。HALCON 用 `接口名|设备名`；Basler 用 `SerialNumber` 或 `UserDefinedName`，且必须且只能给出一个——两个都给无法判断以哪个为准，都不给会匹配到任意一台。
- **像素格式落地规则不同**。Basler 的格式映射表（`BaslerPixelFormats`）是**纯逻辑、不依赖 SDK**，因此可以在没有相机、甚至没有 pylon 运行时的机器上被完整验证；映射之外的格式一律拒绝，特别是不把 10/12/16 位彩色静默降位到 8 位。
- **触发表达能力不同**。HALCON Adapter 无法表达软件触发，因此明确拒绝；Basler 能表达，于是支持 `Software`，但对 `External` 要求私有配置声明 `triggerSource`，否则明确拒绝而不是沿用设备当前设置。

验收：同一进程两个真实Provider各管理自己的设备；公共Workflow/Vision程序集无厂商引用。

- 前者由 `DP.Vision.Acquisition.Integration.Tests` 覆盖（两个真实Provider在同一组合里按SourceId路由、互不串台、各自报告自己的规范资源键）。
- 后者由 `DP.Vision.Acquisition.Tests` 的 `AssemblyBoundaryTests` 覆盖：公共契约程序集与组合/路由程序集都不得引用任何厂商程序集，且厂商名单是**显式清单**，新增厂商时必须补一项。

**未完成的部分必须说清楚**：真实出图路径（打开设备、写曝光/增益、抓图、像素转换）没有真实 Basler 相机与 pylon 运行时验收，本仓库的自动化测试不能替代现场验收；曝光/触发精度与现场吞吐同样未验证。

### 阶段 F：RunScope与高级模式

依赖AR-01阶段3后实现`ExclusiveRun`。只有出现真实连续流需求后再设计Broadcast、背压和丢帧策略。Provider热卸载须在全部设备和图像相关租约退役后评估；原生SDK不能可靠卸载时明确要求进程重启。

## 14. 文件级改动对照

已新增（阶段 A–D 全部落地）：

```text
DP.Vision/src/DP.Vision.Acquisition.Abstractions/    公共契约（netstandard2.0，只引用 DP.Vision）
  IVisionAcquisition / IVisionAcquisitionProvider / IVisionAcquisitionProviderModule
  IVisionAcquisitionProviderPlugin / IVisionAcquisitionProviderHealth
  VisionSourceReference / VisionCaptureRequest / VisionCaptureMetadata / VisionProviderFrame
  VisionDeviceIdentity / VisionDeviceDescriptor / VisionDeviceHealth / VisionAcquisitionOwner
  EVisionSourceSharingPolicy / VisionAcquisitionException 系列

DP.Vision/src/DP.Vision.Acquisition.Runtime/         组合、设备生命周期与插件加载
  VisionAcquisitionProviderComposer / VisionAcquisitionProviderComposition
  VisionAcquisitionSourceBinding / VisionAcquisitionRuntime
  VisionAcquisitionProviderPluginLoader（读取 plugin.json，按 visionAcquisition 分组发现插件）

DP.Vision/src/DP.Vision.Halcon/                      HALCON Provider 插件
  HalconAcquisitionProvider / HalconAcquisitionProviderModule / HalconAcquisitionDevice
  HalconAcquisitionProviderPlugin / HalconProviderConfiguration / plugin.json

DP.Vision/src/DP.Vision.Basler/                      Basler pylon Provider 插件（阶段E）
  BaslerAcquisitionProvider / BaslerAcquisitionProviderModule / BaslerAcquisitionDevice
  BaslerAcquisitionProviderPlugin / BaslerProviderConfiguration / plugin.json
  BaslerAcquisitionBinding（选择器语义，不引用厂商程序集）
  BaslerPixelFormats（像素格式映射表，纯逻辑，不引用厂商程序集）
  BaslerPylonRuntime（原生运行时部署探测）

DP.Vision/tests/DP.Vision.Acquisition.Tests/         契约、边界、组合、路由、并发、插件目录
DP.Vision/tests/DP.Vision.Halcon.Tests/              厂商边界、Manifest与私有配置校验
DP.Vision/tests/DP.Vision.Basler.Tests/              像素映射表、绑定选择器、Manifest与私有配置校验、缺运行时诊断
DP.Vision/tests/DP.Vision.Acquisition.Integration.Tests/  两个真实Provider并存与按SourceId路由（阶段E验收）
```

已演进：

```text
DP.Vision/src/DP.Vision.Halcon/DP.Vision.Halcon.csproj          输出 plugin.json，引用 Acquisition.Abstractions
DP.WorkFlow/src/Workflow/Nodes/DP.WorkFlow.Nodes.Vision/Acquisition/CaptureVisionFrameNode.cs
DP.WorkFlow/src/Workflow/Nodes/DP.WorkFlow.Nodes.Vision/Acquisition/VisionSourceCatalog.cs
DP.WorkFlow/src/Workflow/Nodes/DP.WorkFlow.Nodes.Vision/Acquisition/WorkflowVisionFrameScope.cs
DP.WorkFlow/src/Workflow/Nodes/DP.WorkFlow.Nodes.Vision/Catalog/WorkflowImageRuntimePluginModule.cs
DP.WorkFlow/src/Workflow/Kernel/DP.WorkFlow.Abstractions/Execution/IWorkflowRunPreparationService.cs
DP.WorkFlow/src/Workflow/Kernel/DP.WorkFlow.Abstractions/Nodes/WorkflowPropertyEditorAttribute.cs
DP.WorkFlow/src/Workflow/Persistence/DP.WorkFlow.Persistence.Json/WorkflowDocumentJsonStore.cs
DP.WorkFlow/src/Workflow/Studio/DP.WorkFlow.UI.Shared/Editors/WorkflowPropertyInspectorModel.cs
DP.WorkFlow/src/Workflow/Studio/DP.WorkFlow.UI.WinForms/Editors/WorkflowPropertyPanel.cs
DP.WorkFlow/src/Workflow/Studio/DP.WorkFlow.UI.Wpf/Editors/WorkflowPropertyPanel.cs
DP.WorkFlow/samples/DP.WorkFlow.WinForms.Sample/                插件目录装配 + 插件包投放目标
DP.WorkFlow/samples/Legacy/WpfApptest/                          插件目录装配 + 插件包投放目标
DP.WorkFlow/samples/VisionData/vision-providers/                示例的Provider私有配置（宿主不解释）
```

禁止修改方向（仍然成立，且有自动化回归）：

```text
DP.WorkFlow.Runtime不得引用DP.Vision
DP.WorkFlow.Abstractions不得增加相机/Provider契约
DP.Vision Provider不得引用Workflow
DP.Vision.Acquisition.Runtime不得引用Workflow
公共采集契约不得出现厂商SDK类型
```

## 15. 自动化验收矩阵

用例归属：

```text
契约与架构   DP.Vision.Acquisition.Tests / AssemblyBoundaryTests、ValueObjectContractTests
组合         DP.Vision.Acquisition.Tests / ComposerTests、VisionAcquisitionProviderPluginLoaderTests
路由         DP.Vision.Acquisition.Tests / RoutingTests
并发         DP.Vision.Acquisition.Tests / SharingPolicyTests
内存和租约   DP.Vision.Acquisition.Tests / FakeProviderContractTests
Workflow     DP.WorkFlow.Nodes.Vision.Tests / VisionAcquisitionNodeTests、WorkflowNodeOutputProjectionTests
厂商边界     DP.Vision.Halcon.Tests / HalconBoundaryTests、HalconAcquisitionProviderPluginTests
             DP.Vision.Basler.Tests / BaslerPixelFormatsTests、BaslerAcquisitionBindingTests、BaslerAcquisitionProviderPluginTests
双真实Provider  DP.Vision.Acquisition.Integration.Tests / CrossVendorProviderCoexistenceTests
```

仍未自动化、只能现场签署的项见本节末尾「现场验收」。

### 契约与架构

- 公共采集程序集不引用厂商SDK或Workflow。
- 组合/路由程序集（`DP.Vision.Acquisition.Runtime`）同样不引用厂商SDK或Workflow。
- 厂商程序集名单是显式清单（HalconDotNet、Basler、MvCamCtrl/MvCameraControl、Dahua、Galaxy）；新增厂商Provider时必须补一项，否则该断言失去强制力。
- 每个Provider程序集只引用自己的SDK和中立采集契约。
- DP.Vision全仓无Workflow反向引用。
- Workflow Kernel全仓无Vision引用。
- Provider公共返回类型不包含厂商对象和裸指针。

### 组合

- 两个不同ProviderId可同时发布。
- 两个**真实厂商**Provider（HALCON + Basler）可同进程组合并按SourceId各自路由，互不串台。
- Provider只打开自己私有配置里的绑定，不接受另一个厂商的绑定身份。
- Source绑到未参与本次组合的Provider在组合阶段失败。
- 重复ProviderId/ExtensionId拒绝。
- Module贡献中途抛异常时正式组合不变。
- 缺少Provider的Source绑定拒绝发布。
- 重复SourceId拒绝。
- 两个SourceId映射同ResourceKey时共享同一互斥状态。
- CompositionId和Provider/Source版本清单稳定可导出。

### 厂商差异（Basler 侧）

- 像素格式映射表覆盖直接复制、单色族、8位彩色族与拒绝分支；高位深彩色必须被拒绝而不是降位。
- 绑定选择器必须唯一：两个都给或都不给都拒绝。
- 缺 pylon 运行时通过Provider级诊断暴露，而不是等到采集时抛原生异常。
- 私有配置拒绝未知字段、缺失字段、非法类型与重复绑定身份。

### 路由

- `Camera.Top`只调用其绑定Provider。
- 修改机器映射而不修改文档即可更换Provider。
- Provider失败不自动调用其他Provider。
- 不存在Source在运行准备阶段失败。

### 并发

- 同ResourceKey的ExclusiveOperation只能有一个持有者。
- 不同ResourceKey可以并行。
- Serialized按确定顺序执行并支持取消。
- 打开失败、采集失败、取消、超时均释放Lease。
- 两个Host共享Acquisition Runtime但使用独立FrameScope时互不清理图像。

### 内存和租约

- Provider设备/SDK对象释放后返回图像仍可读取。
- Retain不复制像素，独立Dispose不影响其他租约。
- 最后一个租约释放后底层缓冲可回收。
- 失败或取消不产生Workflow节点输出和正式预览。
- 超预算明确失败，不静默丢帧。

### Workflow

- Capture节点能力预检要求`IVisionAcquisition`。
- Capture输出仍可被18节点链路绑定消费。
- Block和恢复Nested不关闭设备或清理父运行图像。
- SourceId和CaptureId进入Trace/运行制品。
- 同一物理设备冲突包含双方运行身份。

### 现场验收（自动测试不能替代）

- 各厂商SDK部署、许可证和驱动；Basler 侧还需部署 pylon 运行时（免费），否则Provider在首节点前即报不可用。
- 设备枚举、序列号稳定性、断线重连。
- 曝光/增益单位转换。
- 软件触发/外部触发和超时；Basler 外部触发需要现场确认触发源接线名与私有配置一致。
- 长连接吞吐、连续运行内存和温度。
- 多进程设备独占。
- 停机、取消、安全互锁和应用关闭顺序。
- 真实出图路径：Basler 的打开设备、写参数、抓图与像素转换只在装有 pylon 与相机的现场可验证。

## 16. 明确不做

- 不恢复旧Workflow Vision Profile/Provider/Importer体系。
- 不让Workflow节点直接引用HALCON、海康、大华或Basler SDK。
- 不在Provider失败时自动切换另一Provider。
- 不把设备句柄或SDK图像保存到公共数据。
- 不把公共FrameScope作为设备共享手段。
- 不为了插件化立即拆成微服务。
- 不承诺运行中热卸载任意原生SDK。
- 不在没有真实需求时实现Broadcast和无限视频历史。

## 17. 实施完成定义

只有同时满足以下条件，才可声明“多Provider采集架构完成”：

1. Workflow文档只引用逻辑SourceId。
2. 至少两个Provider可在同一进程组合并确定性路由，其中至少一个为真实硬件Provider；最终完成要求两个真实Provider。
3. Provider候选失败不污染正式组合。
4. 同物理设备并发访问有确定性策略和诊断。
5. Provider管理设备生命周期，Workflow只管理取得后的帧生命周期。
6. Kernel无Vision/厂商依赖，Provider无Workflow反向依赖。
7. HALCON不再由样例直接`new`成唯一Workflow采集能力。
8. 自动化矩阵通过，真实设备验收项单独签署。

在此之前，应使用“契约已建立”“HALCON Adapter已迁移”“Workflow已切换逻辑Source”等精确状态，不得笼统宣称全部完成。

### 当前达成情况

| 条件 | 状态 |
|---|---|
| 1 Workflow文档只引用逻辑SourceId | **已达成** |
| 2 至少两个Provider同进程组合，其中至少一个真实硬件Provider；最终要求两个真实Provider | **已达成**：真实 HALCON Provider + 真实 Basler Provider 同进程组合并按SourceId路由（`CrossVendorProviderCoexistenceTests`） |
| 3 Provider候选失败不污染正式组合 | **已达成** |
| 4 同物理设备并发访问有确定性策略和诊断 | **部分**：`ExclusiveOperation` 与 `Serialized` 已实现并有诊断；`ExclusiveRun` 已按根运行作用域所有权实现（V1-C：宿主在首节点之前取得、运行结束时退役），但只对缓冲源有效，主动采集源声明它会在运行前被拒绝；`Broadcast` 仍待真实连续流需求 |
| 5 Provider管理设备生命周期，Workflow只管理取得后的帧生命周期 | **已达成** |
| 6 Kernel无Vision/厂商依赖，Provider无Workflow反向依赖 | **已达成**（有依赖边界回归用例） |
| 7 HALCON不再由样例直接`new`成唯一Workflow采集能力 | **已达成**：示例只扫描插件目录，编译期不引用HALCON类型 |
| 8 自动化矩阵通过，真实设备验收项单独签署 | **部分**：自动化矩阵通过（806 + 834 例，DP.Vision 侧需补 `HALCONROOT` 才是完整口径）；真实设备、许可证、驱动与多进程独占仍需现场签署 |

因此当前应表述为：**契约已建立、HALCON与Basler均已插件化、Workflow已切换逻辑Source、两个真实Provider已同进程组合、`ExclusiveRun` 已按根运行作用域所有权落地；`Broadcast` 未实现，真实设备出图与现场指标未签署。**
