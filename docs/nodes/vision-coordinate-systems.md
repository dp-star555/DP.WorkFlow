# 模板定位坐标系与ROI随动

## 当前机制

本机制是显式的模板局部坐标→本帧图像坐标，不是全局可变“当前矩阵”，也不是图像校正或设备毫米标定。视觉模块仍为18个节点，没有增加万能坐标路由器。

- 模板局部原点：模板图像左上角像素边界，X右、Y下，单位模板像素。
- 图像坐标：当前原图左上角像素边界，X右、Y下，单位当前图像像素。
- 整数像素(column,row)的中心是(column+.5,row+.5)，仅SDK像素索引转换时处理半像素。
- 定位为离散旋转/尺度搜索的相似变换，正角为图像顺时针弧度。不宣称剪切、非等比例仿射、连续形状模型或物理精度。

`Vision.LocateTemplatePose`持久保存`CoordinateSystemId`，成功输出`TemplatePoseResult.CoordinateSystem`：

| 内容 | 含义 |
|---|---|
| CoordinateSystemId | 模板局部原点定义身份，区别于节点ID和帧ID |
| TemplateSignature | 模板布局/全部像素SHA256；同内容重新读取不改变签名 |
| FrameId / ImageWidth / ImageHeight | 本次定位适用的图像内容与尺寸 |
| Pose | 模板宽高、目标中心、角度、尺度 |
| LocalToImage / ImageToLocal | 正反二维矩阵，齐次末行为[0,0,1] |

像素签名最大64MiB，逐行读取，不取得图像所有权。坐标系不持有图像租约。

## 制作与运行

参考图制作时，设参考定位为`Tref`：

```text
ROI局部 = inverse(Tref) × ROI图上绘制
```

换图后重新匹配，得到`Tcurrent`：

```text
ROI当前图像 = Tcurrent × ROI局部
```

所以无需再拟合一次仿射。若从参考图绝对点出发，等效补偿为`Tcurrent × inverse(Tref)`，矩阵采用列向量约定。

ROI可以超出模板矩形，例如模板右侧的孔；不把检测范围限制在模板内容内部。但转换后超出当前图像仍明确失败，不静默裁剪。

文档只保存局部ROI、坐标系/模板身份和输入绑定，不保存运行矩阵。换图、打开页面、刷新预览不改正式配置；每次都从局部ROI生成本帧范围，不累计变换。

## 两平台交互制作

1. 配置场景图像和模板图像，先运行成功定位；当前模板通过既有`Template`图像绑定提供，本轮不新增模板资产库或裁剪保存工作台。
2. 在声明面积范围能力的节点（Blob、颜色、阈值Region、线圆测量、平移定位、旋转尺度定位）配置同一场景图像输入，打开“图像与测量范围”，选择“输入图像”。
3. 从定位节点下拉框选择来源，点击“绑定定位并转换ROI”：已有图上ROI逆变换为局部ROI；未绘制时，显式初始化为模板矩形。
4. 继续在原图上绘制矩形、旋转矩形、椭圆、多边形及排除区域。保存的是局部形状，显示的是本帧变换后的形状。
5. 确认节点才提交；取消保留原文档。一次节点提交可以Undo/Redo。
6. 换图重新执行后，页面按新的同帧定位显示ROI和实际检测事实。

“解除定位转原图”是显式操作，将局部ROI转成本帧的固定原图ROI；尚未确认节点时仍只改隔离副本。必须先显示有效的同帧定位才能解除，不能借用缓存矩阵。定位模式下“全图”不生效，要求显式包含ROI；删除全部ROI会使配置无效，不会悄悄检查全图。

模板/手动预览不能用于借用其他帧定位进行编辑。定位尚未到达、失败、定义变化或帧不匹配时禁用画布ROI交互；新有效帧到达时重建显示，不保留无效视图中的手势。

交互来源列表是当前文档中的定位节点，直接绑定其`CoordinateSystem`成员。公共数据坐标系绑定可以用于执行，但此页面不猜测公共数据图像/变换的预览来源。

## 强类型节点配置

以制作时成功定位的`referenceSystem`为准：

```csharp
blob.Coordinates = new WorkflowVisionCoordinateBinding
{
    System = WorkflowInput<LocatedCoordinateSystem>.FromBinding(
        new WorkflowBindingKey("pose", "CoordinateSystem")),
    CoordinateSystemId = referenceSystem.CoordinateSystemId,
    TemplateSignature = referenceSystem.TemplateSignature
};
blob.FullImage = true; // 不再叠加原图整数矩形，实际范围由局部Regions限定。
blob.Regions = new()
{
    new() { Id = "hole-check", CenterX = 9.5, CenterY = 1.5, Width = 3, Height = 3 }
};
```

未绑定时`Coordinates = null`，保持原图固定范围语义。绑定对象不能保存运行实例Literal，也不能省略制作身份。

### 支持范围

| 节点 | 定位行为 |
|---|---|
| AnalyzeBlobs / AnalyzeColor / ThresholdRegion | 局部连续几何先变换，再在当前原图栅格化；包含并集减排除并集，保留孔洞；可再与同帧显式Mask相交 |
| MeasureCaliper | 配置端点为局部坐标；起终点、方向、采样带宽、边缘间距随尺度；在当前原图双线性采样，不旋转整图 |
| FitRobustLine | 聚合同帧卡尺的原图点；绑定时距离阈值以模板局部像素配置，执行前乘尺度 |
| SelectBlobs / MorphRegion | 不新增局部ROI绑定，处理已经定位的事实并保留其坐标来源；筛选面积与形态学核仍是原图像素单位 |
| MeasureEdges | 使用精确ROI筛选原图Canny边缘后拟合线/圆；不把掩码边界制造成假边缘，提供LocatedA/B、LocalRadius/LocalRmsError |
| LocateTemplate | 父姿态固定旋转/尺度，搜索平移；精确掩码约束整个有效采样足迹；MatchGeometry为实际旋转轮廓，Bounds仅为诊断外接矩形 |
| LocateTemplatePose | 搜索ROI绑定父定位；角度候选相对父角度、尺度候选乘父尺度；成功产生自己定义ID的子坐标系，结果已是原图姿态，不再重复乘父矩阵 |

卡尺目前通过强类型/JSON配置绑定和局部端点，不新增交互采样带绘制工具；`HalfWidth`为局部单侧采样步数，执行时垂直采样间隔为定位尺度。最小梯度仍是灰度/当前图像像素；采样长度和预算限制仍生效。Blob最小面积、特征筛选面积等仍明确使用原图像素，不自动变成模板面积。

## 范围能力与扩展入口

`AnalyzeVisionFrameNodeModel.RangeCapability`声明`Region`、`SamplingBand`、`GeometryFacts`或`None`。共享校验和双平台编辑器只依赖能力，不维护具体节点类型白名单。外部领域节点声明Region后即可使用同一面积ROI编辑页面。

面积Handler统一调用公开`ResolveRange(frame, context, token)`，得到`WorkflowVisionResolvedRange`的原图计算矩形、精确Region和已验证定位。不能只使用Bounds而忽略Region；采样带/事实算子使用公开`ResolveCoordinates`。

`IEdgeMeasurer`、`ITemplateLocator`、`ITemplatePoseLocator`已扩充显式掩码参数，固定宿主实现必须落实这些契约，不能忽略。平移定位另接收固定父姿态。没有新增SDK服务路由或坐标全局变量。

## 结果坐标

检测在当前图像上重新执行，不变换上一帧检测结果。

- 既有`Centroid`、`Region`、卡尺`Position`、拟合`A/B`等保持原图坐标，避免旧绑定突然改变语义。
- Blob新增`LocatedCentroids`，卡尺新增`LocatedEdges/LocatedStart/LocatedEnd`，直线新增`LocatedA/LocatedB/LocalRmsError`。
- `LocatedPoint`同时给出`ImagePosition`、`LocalPosition`、`FrameId`、定义ID和模板签名。
- Region/颜色结果也携带`CoordinateSystem`。Region仍是本帧像素游程，不声称旋转后仍存在无损整数“局部Region”。

例如`blob.LocatedCentroids[0].LocalPosition.X`适合工件内相对位置判定；`blob.Blobs[0].Centroid.X`适合原图叠加和下一步设备标定。未绑定定位时，定位相关结果成员为空，不提供虚假的恒等坐标系。

## 失败与边界

- 未检出：定位本身正常完成，但CoordinateSystem为空；随动消费者失败，不提交消费者输出或复用旧矩阵。
- 同尺寸不同FrameId、不同模板定义ID、不同模板内容签名：拒绝。
- 像素变化生成新FrameId；定位之后再预处理图像不能隐式沿用旧帧定位。建议先预处理，再定位和检测。
- 支持沿工作流显式绑定的父子定位；不支持自身定位结果作为本节点搜索坐标系。数据依赖仍接受通用编译/可见性校验；不是任意全局坐标系变换图、手眼标定或单位类型系统。
- 父子尺度乘积必须保持在当前定位能力的0.1..10范围内，预算超限失败而非截断。旋转模板有效像素采样足迹必须完全位于搜索Region内；孔洞不能只靠匹配中心检查。无合法候选为正常未检出。
- Canny先处理原图再筛选范围内证据，避免ROI边界产生伪边缘；阈值和最小点数仍使用原图单位。
- 设备毫米标定继续使用SolveCalibration/MapCoordinate，需调用方显式声明来源与目标单位。
- 原始`Coordinate2D`和裸double仍可在通用绑定中混用；本机制对显式随动契约进行身份校验，不宣称整个工作流都已有单位静态检查。

实现入口：`DP.Vision.Algorithms/Coordinates/LocatedCoordinateSystem.cs`、`WorkflowVisionCoordinateBinding.cs`、`VisionCoordinateEditing.cs`。

## 验证入口

当前Workflow全量817项（`-Suite All`，与`dotnet test DP.WorkFlow.sln`一致；默认`-Suite Auto`跑481项，跳过控件库298项与`ScriptEngine.Windows.Tests`38项）通过；Vision两框架各核心115、算法67、HALCON边界5及原生探针/Demo smoke通过。Workflow Debug/Release与独立WPF Debug/Release均零警告/错误，Solution43/43。包含父子定位、线圆测量、精确足迹与外部节点能力声明测试；间歇UI测试情况见[实施记录](../dp-vision-integration-plan.md)。

- `LocatedCoordinateSystemTests`：正反矩阵、0/90/45度与非整数尺度、逐像素逆映射真值、孔洞、身份/模板签名、取消/越界、卡尺及直线双坐标。
- `VisionCoordinatePipelineTests`：真实OpenCV模板匹配，参考图→平移旋转新图→未检出，三种面积算子、卡尺与拟合、JSON重跑、失败不提交、隔离编辑/确认/Undo、无效预览禁用及恢复。
- 已还原依赖的离线环境可使用`tools/Test-DPWorkFlow.ps1 -NoRestore`及同级`DP.Vision/verify.ps1 -NoRestore`，避免验证时重新访问NuGet。该脚本默认`-Suite Auto`：现代控件库源码没改动时跳过控件库用例，需要全跑用`-Suite All`。

自动合成图像与原生控件测试不是相机现场、物理输入或测量精度认证。
