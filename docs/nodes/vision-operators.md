# 已落地的视觉算子

2026-10-02独立几何及坐标包提供10节点：业务定义、本帧坐标构建、点生成/选择、点和直线转换、生成直线、点点/点线/线线距离。模板仅是坐标来源之一，卡尺/Blob/拟合可接入同一几何链路。图像获取已融合为AcquireFrame：内置20＋独立12，共32种注册类型，工具箱隐藏四种兼容取图类型后展示28种；见[取图说明](new-vision-file-pipeline.md#acquisition-入口与图像来源)、[几何测量](vision-geometry-measurement.md)和[业务坐标](vision-coordinate-systems.md)。下文早期数量和验证数字保留为历史记录。

算子扩展时增加了8个节点，`WorkflowImageRuntimePluginModule` 目前共注册19个节点。算法属于独立DP.Vision；其中13个算法节点已按节点配置选择引擎，运行前统一准备。两个示例从插件包发现实现，编译期不引用具体引擎。相机现场工作按用户安排延期，不阻塞这些算子。默认选择、配置归属及部署方式见[算法插件文档](../../../DP.Vision/ALGORITHM_PLUGINS.md)。

| NodeType | 标准输出 | 已实现语义 |
|---|---|---|
| Vision.PreprocessImage | ImageFrame | 灰度、反相、Gaussian、中值、固定增益/偏置、显式Gray16→Gray8 |
| Vision.ThresholdRegion | RegionAnalysisResult | 0..255灰度闭区间分割，原图矩形及精确ROI/绑定掩码交集 |
| Vision.MorphRegion | RegionAnalysisResult | 膨胀、腐蚀、开闭、填孔；方形/离散椭圆/十字核 |
| Vision.SelectBlobs | BlobAnalysisResult | 面积、栅格圆度、面积矩长短轴比筛选；保留原顺序和精确Region |
| Vision.MeasureCaliper | CaliperResult | 双线性带采样、灰度剖面、极性、梯度峰抛物线插值、间距抑制 |
| Vision.FitRobustLine | RobustLineResult | 聚合同帧卡尺边缘，确定性RANSAC＋正交TLS，输出内点索引与RMS |
| Vision.LocateTemplatePose | TemplatePoseResult | 显式离散旋转/尺度候选、有效模板掩码SqDiff、单个最佳姿态 |
| Vision.MapPoseCoordinate | Coordinate2D | 模板→图像或图像→模板的姿态坐标映射；未检出明确失败 |

## 定位随动扩展

后续增加两个独立节点包，内置模块仍为19个节点：`Vision.ReadBarcode`（默认 zxing.code，精确掩码、同帧读码事实）与 `Vision.RecognizeTextLine`（默认 ppocr.recognize，显式预处理依赖、水平单行矩形）。宿主从目录发现，不编译引用节点包。机器资源配置、取消检查、准备错误定位及人工复核详见 [复核说明](../plugins/vision-plugin-review.md)。

已有模板定位现在输出共享`CoordinateSystem`；Blob/颜色/阈值Region、卡尺及鲁棒直线可显式绑定。局部ROI编辑、正反矩阵、双坐标结果及不支持的范围详见[定位坐标系机制](vision-coordinate-systems.md)。下方653项为算子扩展时的历史验证基线。

## 双宿主默认示例

WinForms/WPF现在默认调用 `WorkflowImageDemo.PopulateProcessing`：

```text
Start → 文件 → 预处理 → 阈值Region → 填孔 → Blob → 特征筛选 → 掩码颜色
```

源仍是随输出复制的`VisionData/demo.pgm`，无需相机和模型。结果为两个Blob，掩码颜色均值170；基础示例`Populate`仍用于说明不带掩码的原图均值26.5625。

注意：控制流不是数据流。此示例颜色绑定形态学Region，而不是自动使用上一个筛选节点；筛选结果用于后续数量、面积、轴比判断。需要其他数据来源必须显式绑定。

## 预处理与Region链路

```csharp
var process = new PreprocessVisionImageNodeModel {
    Frame = WorkflowInput<ImageFrame>.FromBinding(new("file", "$")),
    Operation = EImagePreprocessing.Gray16ToGray8,
    Gain = 1d / 257, Offset = 0
};
var blob = new AnalyzeVisionBlobsNodeModel {
    Frame = WorkflowInput<ImageFrame>.FromBinding(new("process", "$")),
    Mask = WorkflowInput<RegionAnalysisResult>.FromBinding(new("morph", "$")),
    MinimumGray = 0, MaximumGray = 255
};
```

- 预处理保持尺寸和坐标系，但无论像素恰巧相同与否，输出均使用**新FrameId**。默认16位增益是1，不会偷偷自动归一化；常见满量程映射须显式设置1/257。
- 除Grayscale外，操作只接受明确灰度位深。Gaussian边界Reflect101，中值Replicate，增益/偏置饱和到0..255。
- Region不拥有图像，保存FrameId、宽高和精确游程。形态学不改变图像，因此保持FrameId。
- Blob、颜色、阈值节点新增`Mask`绑定；默认空Literal表示未启用，不能保存运行Region事实Literal。掩码与配置ROI求交，跨帧或尺寸不一致拒绝，不因尺寸相同就认为同帧。
- 形态学半径0..31，0为恒等。画布外恒为零背景，每一步都在有限画布上运算；贴边闭运算可能收缩边缘，不会隐式扩展画布。FillHoles填补不与画布边界四连通的背景，忽略核和半径。
- 所有Blob观测新增`Features`：GridPerimeter、Circularity、MajorAxisLength、MinorAxisLength、OrientationRadians、Elongation。周长包含孔洞边界，是**栅格单元边界长度**，不是亚像素轮廓周长；单像素圆度为π/4。

## 卡尺与鲁棒直线

在同一输入帧上放置至少三个扫描带。例如三个水平带穿过一条近竖直边缘，分别得到CaliperResult；将它们按顺序绑定到`FitVisionRobustLineNodeModel.Samples`。

```csharp
var fit = new FitVisionRobustLineNodeModel {
    Frame = WorkflowInput<ImageFrame>.FromBinding(new("image", "$")),
    Samples = new() {
        WorkflowInput<CaliperResult>.FromBinding(new("caliper1", "$")),
        WorkflowInput<CaliperResult>.FromBinding(new("caliper2", "$")),
        WorkflowInput<CaliperResult>.FromBinding(new("caliper3", "$"))
    },
    DistanceThreshold = .5, MinimumInliers = 3
};
```

- 卡尺仅接受Gray8。起终点是原图像素边界坐标；像素中心为`.5`。整个采样带必须处于可采样像素中心范围，不裁剪越界带。
- 沿带约1px均匀采样，垂直方向使用BandSampleStep（默认1px）平均；绑定定位后间隔乘尺度，不暗中滤波。端部各约两个采样步长不输出梯度峰，那里没有足够插值邻域。
- `Profile`保存灰度剖面；`Edges`按扫描距离排序，含Position、Distance、带符号Gradient。Rising/Falling均相对于起点→终点，反向扫描会反转极性。
- 可用两条已选边缘坐标绑定现有距离节点计算宽度；当前不自动选择业务意义上的边缘对。
- RANSAC最多8192点、1024次采样，总距离评估不超过400万；正交重拟合内点集合不稳定、方向不可辨识、重合或证据不足均失败。不是鲁棒圆/圆弧拟合。
- 亚像素插值已用非整数边缘合成真值验证，但不代表现场光学、标定与机械测量精度已经验收。

## 旋转/尺度定位与坐标变换

`AnglesRadians`是顺时针弧度列表，`Scales`是0.1..10尺度列表。搜索是**离散候选**，不声称连续角度优化、学习式形状模型或多实例检测。

旋转使用原图像素边界坐标；仅在调用OpenCV的像素索引矩阵时处理中心偏移。有效模板掩码排除旋转后的空白角；分数为`1 - maskedSqDiff/(65025*validPixelCount)`，不是概率。

`TemplatePoseResult.Transform`提供模板中心、角度、尺度以及正反映射。节点`Vision.MapPoseCoordinate`可直接绑定定位根输出，输出Coordinate2D；Found=false时不会返回虚假的(0,0)。旋转模板的四角按真正变换绘制，不用轴对齐外接框冒充姿态。

候选组合最多512，默认保守工作量预算2亿（位置数×模板面积）。**搜索前**检查完整预算，超限失败；缩小搜索ROI或显式调整预算，不截断候选后宣称全局最佳。

## 宿主算法装配

当前示例从插件包发现引擎，注册ManagedVisionAlgorithmModule与WorkflowVisionAlgorithmBindings，按节点槽位统一运行前准备；不再在宿主中逐个new具体引擎。装配代码见[算法插件说明](../../../DP.Vision/ALGORITHM_PLUGINS.md)。预处理仍由IWorkflowVisionFrameScope接管输出租约。目录冻结后不能在运行中替换服务；几何证据通过共享契约进入FrameEditor及两平台Renderer。

## 验证

- 新增算法合成真值/边界数据用例11项（含非整数卡尺、离群点、45°/90°与尺度变化），net48/net8均通过；算法项目合计各57项。
- 新增Workflow集成9项，覆盖JSON、重跑、真实共享示例、能力预检、跨帧失败不提交、卡尺聚合与姿态正反映射；完整Workflow回归653项通过，Windows UI329项。
- 独立Vision完整verify通过（核心92、算法57、SDK边界5，两框架）；原生画布探针和Demo smoke通过。
- Workflow Release、独立WPF Debug/Release零警告/错误。相机现场与WPF物理输入仍不属于这些自动验收。

## 后续仍未实现

独立Region并/差节点、凸度/孔洞数量特征、自动边缘配对策略、鲁棒圆/圆弧、连续姿态细化和多实例、像素重采样后的图像校正/裁剪节点、Lab色差及畸变/手眼标定仍是后续能力。没有以空接口或改名包装声称完成这些功能。
