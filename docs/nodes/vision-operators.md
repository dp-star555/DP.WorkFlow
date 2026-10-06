# 已落地的视觉算子

2026-10-06 节点调整（不兼容旧配方中被删除的节点类型）：

- 删除重复节点：读取图像文件、顺序读取图像目录、采集面阵帧、采集线扫帧（统一用“图像获取”）；平移模板定位（并入“模板定位”，角度、尺度区间都固定即平移）；测量两点距离（用“视觉点间距”）；定位坐标映射（用“构建本帧坐标系”＋“点坐标系转换”）；边缘线圆测量（用“找线”“找圆”）。
- 新增“找线”“找圆”：在图像页画搜索ROI，节点内排布多把卡尺取边缘点，再鲁棒拟合直线或圆。
- 列表结果可直接使用：读码支持码制/文本过滤、排序、期望个数，输出首个文本、文本列表、合并文本和个数合格；筛选连通域支持排序，结果“首个”即排序第一个。详见下方“列表结果的用法”。
- “识别单行文字”改为画一个可旋转的矩形文字框，绑定坐标系后随工件移动旋转。
- 工具箱分类按作业顺序改为 5.Vision 下的“1.取图/2.图像处理/3.区域/4.定位/5.测量/6.几何/7.检测/8.识别/9.标定”；属性面板的枚举选项显示中文。
- 内置模块15种节点，加几何包9种、读码和文字识别各1种，共26种注册类型，全部在工具箱中显示。

2026-10-05增加“创建区域／掩膜”：内置模块现有21种节点，手绘ROI可输出同帧区域并供下游复用。普通图像页和模板制作／试匹配页支持有效掩膜预览，使用PropertyGrid现有绑定功能选择上游区域，详见下方“掩膜创建、绑定与显示”。

2026-10-02独立几何及坐标包提供9节点：本帧坐标构建（自带坐标系定义）、点生成/选择、点和直线转换、生成直线、点点/点线/线线距离。模板仅是坐标来源之一，卡尺/Blob/拟合可接入同一几何链路。图像获取已融合为AcquireFrame：内置20＋独立12，共32种注册类型，工具箱隐藏四种兼容取图类型后展示28种；见[取图说明](new-vision-file-pipeline.md#acquisition-入口与图像来源)、[几何测量](vision-geometry-measurement.md)和[业务坐标](vision-coordinate-systems.md)。下文早期数量和验证数字保留为历史记录。

算子扩展时增加了8个节点，`WorkflowImageRuntimePluginModule` 目前共注册19个节点。算法属于独立DP.Vision；其中13个算法节点已按节点配置选择引擎，运行前统一准备。两个示例从插件包发现实现，编译期不引用具体引擎。相机现场工作按用户安排延期，不阻塞这些算子。默认选择、配置归属及部署方式见[算法插件文档](../../../DP.Vision/ALGORITHM_PLUGINS.md)。

| NodeType | 标准输出 | 已实现语义 |
|---|---|---|
| Vision.CreateRegion | RegionAnalysisResult | 由包含／排除ROI、矩形及上游掩膜创建精确同帧区域，无需厂商引擎，可供多个下游复用 |
| Vision.PreprocessImage | ImageFrame | 灰度、反相、Gaussian、中值、固定增益/偏置、显式Gray16→Gray8 |
| Vision.ThresholdRegion | RegionAnalysisResult | 0..255灰度闭区间分割，原图矩形及精确ROI/绑定掩码交集 |
| Vision.MorphRegion | RegionAnalysisResult | 膨胀、腐蚀、开闭、填孔；方形/离散椭圆/十字核 |
| Vision.SelectBlobs | BlobAnalysisResult | 面积、栅格圆度、面积矩长短轴比筛选；可按面积/质心X/质心Y/圆度排序，`First`为排序首个；保留精确Region |
| Vision.MeasureCaliper | VisionCaliperMeasurement | 直线或圆弧卡尺：双线性带采样、灰度剖面、极性、梯度峰抛物线插值、间距抑制 |
| Vision.FitRobustLine | RobustLineResult | 聚合同帧卡尺边缘，确定性RANSAC＋正交TLS，输出内点索引与RMS |
| Vision.FindLine | VisionFindLineResult | 矩形搜索框内排布N把卡尺（沿框宽度排布、沿高度扫描），每把取首个/最强/末个边缘，鲁棒拟合直线；输出拟合直线、角度、边缘点 |
| Vision.FindCircle | VisionFindCircleResult | 期望圆周上排布N把径向卡尺（由内向外/由外向内，可只扫圆弧），鲁棒拟合圆；输出圆心、半径、直径、边缘点 |
| Vision.LocateTemplatePose | TemplatePoseResult | “模板定位”：显式角度／尺度区间，都固定时即平移定位；OpenCV按步长采样，HALCON资源模型原生范围搜索；单个最佳姿态 |

## 成功/失败出口（检测NG不中止）

- 所有视觉节点（含几何、读码、OCR）都有“成功”和“失败”两个控制出口。
- “失败”出口连线后，节点失败（例如找不到边缘、模板未匹配、读码失败）不再中止本次运行，而是沿失败支路继续，便于做NG分流、计数、剔除。故障照常记录：节点在画布上标红，“运行监控”轨迹里有“NodeFailed”和“NodeFaultRouted”两条记录。
- “失败”出口未连线时保持原规则：本次运行以故障结束（或交给恢复处理）。
- 工作台运行失败不再弹窗：失败节点标红，自动切到“运行监控”页查看原因；宿主需要额外记录时可订阅 `RunFaulted` 事件。
- 失败节点没有输出结果，失败支路上的节点不要绑定它的结果。

## 定位随动扩展

后续增加两个独立节点包，内置模块仍为19个节点：`Vision.ReadBarcode`（默认 zxing.code，精确掩码、同帧读码事实）与 `Vision.RecognizeTextLine`（默认 ppocr.recognize，显式预处理依赖；2026-10-06起为一个可旋转矩形文字框，识别前校正为水平小图，可跟随定位）。宿主从目录发现，不编译引用节点包。机器资源配置、取消检查、准备错误定位及人工复核详见 [复核说明](../plugins/vision-plugin-review.md)。

模板匹配输出位姿测量值，经“构建本帧坐标系”生成`CoordinateSystem`后，Blob/颜色/阈值Region、卡尺及鲁棒直线可显式绑定。局部ROI编辑、正反矩阵、双坐标结果及不支持的范围详见[定位坐标系机制](vision-coordinate-systems.md)。下方653项为算子扩展时的历史验证基线。

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

在同一输入帧上放置至少三个扫描带。例如三个水平带穿过一条近竖直边缘，分别得到卡尺结果（VisionCaliperMeasurement）；将它们按顺序绑定到`FitVisionRobustLineNodeModel.Samples`。

```csharp
var fit = new FitVisionRobustLineNodeModel {
    Frame = WorkflowInput<ImageFrame>.FromBinding(new("image", "$")),
    Samples = new() {
        WorkflowInput<VisionCaliperMeasurement>.FromBinding(new("caliper1", "$")),
        WorkflowInput<VisionCaliperMeasurement>.FromBinding(new("caliper2", "$")),
        WorkflowInput<VisionCaliperMeasurement>.FromBinding(new("caliper3", "$"))
    },
    DistanceThreshold = .5, MinimumInliers = 3
};
```

- 卡尺仅接受Gray8。起终点是原图像素边界坐标；像素中心为`.5`。整个采样带必须处于可采样像素中心范围，不裁剪越界带。
- 沿带约1px均匀采样，垂直方向使用BandSampleStep（默认1px）平均；绑定定位后间隔乘尺度，不暗中滤波。端部各约两个采样步长不输出梯度峰，那里没有足够插值邻域。
- `Profile`保存灰度剖面；`Edges`按扫描距离排序，含Position、Distance、带符号Gradient。Rising/Falling均相对于起点→终点，反向扫描会反转极性。
- 输出`VisionCaliperMeasurement`保留直线卡尺原有成员名（Start/End/Profile/Edges/Count/MeasuredEdges/LocatedEdges），已有绑定路径不变；直线形状、1px采样间隔时原始`CaliperResult`在`Line`成员中。
- 卡尺节点是**单个卡尺**（对应HALCON `gen_measure_rectangle2`/`gen_measure_arc`），用于测位置、宽度、间距、个数；它只给出扫描线上的点，不拟合直线/圆。要用多把卡尺找直线或圆，请用“找线”“找圆”。
- `ScanStep`（采样间隔，默认1，原图0.1..10px）：沿扫描方向每隔多少像素取一个剖面点。直线卡尺为1px时调用可替换的直线卡尺算法实现，其它间隔使用同一规则的托管采样。

### 边缘对（测宽度）

- `EdgeMode = 边缘对`时（对应HALCON `measure_pairs`）：边缘按扫描距离排序，从近到远找第一个极性符合“边缘极性”的边缘，再向后找第一个极性相反、宽度在`[MinimumPairWidth, MaximumPairWidth]`内的边缘配成一对；配对后从第二个边缘之后继续，不重叠。“边缘极性”为任意时第一个边缘可为任一极性。
- 边缘对模式下卡尺先取两种极性的全部边缘，再配对；`Edges`为全部边缘，`Pairs`为边缘对（`First`/`Second`/`Midpoint`/`Width`），另有`PairCount`、`Width`（第一对）、`Widths`、`MeanWidth`/`MinimumWidth`/`MaximumWidth`、`MeasuredPairCenters`。
- 宽度为两边缘沿扫描路径的距离差：直线为长度，圆弧为弧长；绑定坐标系时宽度范围为业务单位，按尺度换算。
- 结果图：全部边缘为小灰点，每对的两个边缘为青色点并用宽度线连接，中点为黄色；不画文字，点击后状态栏显示宽度。

### 圆弧卡尺

`Shape = Arc`时沿圆弧扫描：圆心`CenterX/CenterY`、半径`Radius`、起始角`StartAngle`与扫描角度`SweepAngle`（度，X轴正向起顺时针，图像Y向下；正为顺时针，绝对值(0,360]）。

- 沿扫描圆弧按采样间隔均匀取点，每点沿**半径方向**取`2×HalfWidth+1`个点（间隔BandSampleStep）求平均；剖面、梯度、峰值插值和间距抑制与直线卡尺相同。
- `Edges[i].Distance`为从起始角开始的弧长，`AngleDegrees`为边缘所在角度；Rising/Falling相对起始角→终止角。
- 采样带内侧不能越过圆心（`Radius ≥ HalfWidth×BandSampleStep`），整条环形采样带必须在图像内。
- 绑定坐标系时圆心、半径、起始角为局部表达，运行时按相似变换换算到原图（角度加坐标系旋转，半径与间隔乘尺度）。
- 圆弧卡尺是工作流内置的托管实现，不经过可替换的`ICaliperMeasurer`算法实现选择。
- RANSAC最多8192点、1024次采样，总距离评估不超过400万；正交重拟合内点集合不稳定、方向不可辨识、重合或证据不足均失败。不是鲁棒圆/圆弧拟合。
- 亚像素插值已用非整数边缘合成真值验证，但不代表现场光学、标定与机械测量精度已经验收。

## 旋转/尺度定位与坐标变换

`MinimumAngleRadians`／`MaximumAngleRadians`定义顺时针角度区间，界面使用度；`MinimumScale`／`MaximumScale`定义0.1..10尺度区间。OpenCV按`AngleStepRadians`／`ScaleStep`采样并包含端点；HALCON资源模型直接使用原生区间。旧候选列表已删除，配置不迁移。制作与搜索范围的关系见[模板制作说明](vision-template-authoring.md)。

旋转使用原图像素边界坐标；仅在调用OpenCV的像素索引矩阵时处理中心偏移。有效模板掩码排除旋转后的空白角；分数为`1 - maskedSqDiff/(65025*validPixelCount)`，不是概率。

`TemplatePoseResult.Transform`提供模板中心、角度、尺度以及正反映射。需要把模板参考坐标换算到原图时，用“构建本帧坐标系”（模板匹配结果方式）＋“点坐标系转换”；Found=false时数值为NaN，构建坐标系明确失败，不会返回虚假的(0,0)。旋转模板的四角按真正变换绘制，不用轴对齐外接框冒充姿态。

OpenCV搜索采样最多4096组，默认保守工作量预算2亿（位置数×模板面积）；资源模板缓存最多512项、64MiB。**搜索前**检查完整预算，超限失败；缩小ROI或搜索范围、增大步长、显式调整预算，不截断搜索后宣称全局最佳。

## 列表结果的用法

连通域、读码、卡尺都会产生列表。节点输出的列表字段可以整体绑定，绑定路径不支持下标（如`Blobs[0]`）。按用途处理：

| 用途 | 做法 |
|---|---|
| 只看数量或是否合格 | 绑定`Count`（读码为“个数”）到“数值比较”节点；读码另有“个数合格”（与期望个数比较）可直接接“判断” |
| 按规则取一个 | 筛选连通域设置排序依据和降序，绑定`First`（首个）；读码设置码制、文本规则和排序，绑定“首个文本”；点集用“选择视觉点”按序号取 |
| 全部都要 | 读码绑定“文本列表”或“合并文本”（按排序、用分隔符连接），可直接写入通讯或MES |

读码的“个数合格”只比较过滤后的数量与期望个数（0表示至少一个），不代替产品判定；原始读取结果`读码结果`保留被过滤掉的码。码位置取引擎定位点的平均值，绑定坐标系时按业务坐标排序；连通域质心排序同理。

## 找线与找圆

- 找线：在图像页画一个可旋转矩形搜索框。卡尺沿框的宽度方向均匀排布，沿高度方向从上到下扫描（框旋转时随之旋转，可设“反向扫描”）；极性按扫描方向判断。
- 找圆：画一个正圆作为期望圆。卡尺沿圆周排布，沿半径方向扫描“搜索长度”；“扫描角度”小于360时只找圆弧。每把卡尺的采样带是圆环扇形（宽度按期望圆上的弧长，扇形外侧更宽），沿圆弧方向求平均，因此找圆固定使用托管扇形采样，“卡尺算法”只对找线生效。
- 每把卡尺按“边缘选择”取一个点（首个/最强/末个），超出图像的卡尺跳过并在摘要中说明；找到的点少于“最少内点”时执行失败。
- 拟合使用固定种子的RANSAC：直线为正交TLS重拟合，圆为代数拟合后几何细化。“内点距离阈值”是点到拟合结果的距离。
- 绑定坐标系后搜索ROI随工件移动旋转，长度参数为业务单位；属性面板换绑坐标系时ROI、长度参数和找圆起始角一并换算。
- 结果`MeasuredLine`（拟合直线）和`MeasuredCenter`（圆心）带帧和坐标来源，可直接接距离节点。
- 图像页使用与卡尺一致的专用图上编辑器（不再用通用ROI工具）：找线画黄色期望直线（拖两端改位置与方向）、青色搜索框与每把卡尺、黄色箭头为各卡尺的搜索方向，白色方块调整搜索长度和卡尺宽度，拖动框内整体平移；找圆画黄色期望圆/圆弧、内外搜索边界与径向卡尺，可拖圆心、半径（黄色菱形）、搜索长度、卡尺宽度、起止角。修改参数即时更新，图上不画文字标签。
- 新建节点带默认搜索范围，首次拿到图像时自动居中；旧配方搜索范围为空时同样补一个居中的默认范围。
- “忽略点数”（对应VisionPro的NumToIgnore）：拟合时按残差从大到小逐个剔除并重新拟合，被剔除的点算忽略点；范围0..卡尺数量−最少内点。
- 结果图：绿色 × 为参与拟合的计算点，红色 × 为忽略点，黄色为拟合直线/圆；卡尺为半透明填充的青色采样带，搜索边界为淡灰色；不画文字，点击后状态栏显示说明。结果另有`Inliers`（与`EdgePoints`同序）、`InlierPoints`、`OutlierPoints`、`OutlierCount`、`CaliperScans`。
- “边缘模式”为边缘对时，每把卡尺按同一配对规则找边缘对（“边缘选择”的最强为两边梯度绝对值之和最大），用中点拟合中心线/中心圆，`EdgePairs`给出各卡尺的边缘对，`MeanWidth`/`MinimumWidth`/`MaximumWidth`为计算点的宽度统计；图上另画青色宽度线。

## 掩膜创建、绑定与显示

掩膜复用ROI编辑器，矩形、旋转矩形、椭圆及闭合多边形均可作为包含／排除区域。有效区域是包含集合并集减排除集合，再与矩形计算范围和可选上游掩膜取交集；空交集保持为空，禁用全部ROI明确报错。普通检测节点无需再建立一份重复的手绘掩膜配置。

1. 只用于一个节点时，在“图像与测量范围”直接绘制ROI，选中需要屏蔽的区域后点击“设为排除”。勾选“显示有效掩膜”检查半透明绿色区域及状态栏像素面积。
2. 多个节点需要共用时，增加“创建区域／掩膜”（`Vision.CreateRegion`），绑定取图节点的同一帧，在其图像页绘制包含／排除ROI。它输出`RegionAnalysisResult`，不需要HALCON/OpenCV算法选择。没有ROI时显式使用全图，也可只绘制排除区域。
3. 下游在PropertyGrid的“上游区域掩膜”选择绑定，将其绑定到创建区域、阈值分割或形态学节点的标准输出。掩膜绑定仅在支持面积范围的节点展示，不能绑定自身。不同输入帧或尺寸的区域在执行时拒绝，预览提示原因并清除旧掩膜叠加。
4. 创建节点可以绑定业务坐标，运行时将局部ROI变换到当前图像再栅格化，输出保留同帧坐标来源。下一帧重新执行创建节点，不能将上一帧的区域直接套用到新图。

模板制作页同样显示真实有效掩膜，“制作掩膜”属性显示有效像素面积或错误。制作预览、模型生成和保存使用相同的包含／排除组合规则，保留孔洞。模板试匹配显示实际搜索掩膜，与模型制作掩膜分别处理；读取新测试图或重设测试条件会清理旧叠加。显示开关只影响画布，不改计算配置、不触发模型重建。当前提供形状ROI绘制及区域输出绑定，不提供灰度掩膜图片导入或笔刷涂抹。

## 宿主算法装配

当前示例从插件包发现引擎，注册ManagedVisionAlgorithmModule与WorkflowVisionAlgorithmBindings，按节点槽位统一运行前准备；不再在宿主中逐个new具体引擎。装配代码见[算法插件说明](../../../DP.Vision/ALGORITHM_PLUGINS.md)。预处理仍由IWorkflowVisionFrameScope接管输出租约。目录冻结后不能在运行中替换服务；几何证据通过共享契约进入FrameEditor及两平台Renderer。

## 验证

掩膜更新回归：视觉／属性Windows179项、视觉节点95项、UI.Shared88项通过，共362项。新增7项覆盖同图ROI变更后的叠加、不同帧拒绝、创建区域节点无厂商引擎执行及JSON保存／PropertyGrid绑定、双平台显示开关、模板孔洞预览与保存一致性，以及全部ROI禁用后的制作拒绝。WinForms/WPF示例复用已构建项目引用，在独立目录构建均0警告／0错误；没有重跑完整Windows套件或生产图像验收。

- 新增算法合成真值/边界数据用例11项（含非整数卡尺、离群点、45°/90°与尺度变化），net48/net8均通过；算法项目合计各57项。
- 新增Workflow集成9项，覆盖JSON、重跑、真实共享示例、能力预检、跨帧失败不提交、卡尺聚合与姿态正反映射；完整Workflow回归653项通过，Windows UI329项。
- 独立Vision完整verify通过（核心92、算法57、SDK边界5，两框架）；原生画布探针和Demo smoke通过。
- Workflow Release、独立WPF Debug/Release零警告/错误。相机现场与WPF物理输入仍不属于这些自动验收。

## 后续仍未实现

独立Region并/差节点、凸度/孔洞数量特征、自动边缘配对策略、鲁棒圆/圆弧、连续姿态细化和多实例、像素重采样后的图像校正/裁剪节点、Lab色差及畸变/手眼标定仍是后续能力。没有以空接口或改名包装声称完成这些功能。
