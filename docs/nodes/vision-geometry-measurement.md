# 视觉节点盘点与带来源几何测量

更新于2026-10-02。按实际注册入口和部署包盘点：20种内置节点，条码1种、水平单行OCR1种、几何及坐标10种，共32种注册类型。四种旧取图类型保留配方兼容且从工具箱隐藏，因此工具箱展示28种视觉节点。算法实现与节点模板分别部署；几何计算加入现有托管模块managed.geometry，没有增加功能×引擎组合DLL。独立业务坐标的完整构建、ROI绑定和兼容规则见[坐标系统](vision-coordinate-systems.md)。

## 当前能力和缺口

| 领域 | 实际节点/能力 | 仍缺少的能力 |
|---|---|---|
| 图像来源（1个入口、4个兼容类型） | AcquireFrame统一选择文件、文件夹、面阵和线扫；旧LoadFile、LoadFolder、CaptureAreaFrame、CaptureLineScanFrame仍可运行 | 相机现场触发、重连和吞吐验收 |
| 预处理与区域（3） | PreprocessImage、ThresholdRegion、MorphRegion | 独立区域并/差、输出裁剪或校正图像及来源映射 |
| Blob和颜色（3） | AnalyzeBlobs、SelectBlobs、AnalyzeColor | 凸度、孔洞计数、真实亚像素周长、Lab/ΔE |
| 引导定位（3） | LocateTemplate、LocateTemplatePose、MapPoseCoordinate | 连续姿态细化、多实例、学习式形状模板、模板制作与资产库 |
| 边缘测量（3） | MeasureEdges、MeasureCaliper、FitRobustLine | 业务边缘配对、鲁棒圆/圆弧 |
| 旧标定与距离（3） | SolveCalibration、MapCoordinate、MeasureDistance | 带单位和来源的物理标定、畸变及手眼标定 |
| 独立业务节点（2） | ReadBarcode、RecognizeTextLine | 真实OCR模型现场精度复核 |
| 新几何包（10） | 下表全部节点 | 角度、圆相关测量、标定设备身份/有效期管理 |

计数是NodeType数量，不是算法接口数量。引擎层还有未接入Workflow的字符分割、字符比对、空白/固定区域质量、条码印刷质量、块异常检测等算法；它们不等于已有对应工具箱节点。OpenCV没有IBarcodeReader实现，读码使用ZXing；两种块异常检测实现属于同一能力。已取消的整图OCR/切字/打印质量节点未恢复。

## 新增节点

包为plugins/workflow.vision.geometry，含节点DLL和deps.json。生成直线和距离节点通过IGeometryMeasurer选择实现，默认managed.geometry；纯点生成、选择及坐标转换不要求几何工厂。

| NodeType | 输入与输出 | 明确语义 |
|---|---|---|
| Vision.DefineCoordinateSystem | 稳定业务配置 → VisionCoordinateDefinition | ID/版本/单位/原点与轴，不依赖模板 |
| Vision.BuildCoordinateSystem | 图像＋定义＋构建参数 → VisionCoordinateSystemResult | 姿态、双点、交线、父坐标、矩阵或标定点对构建本帧映射 |
| Vision.CreatePoint | 显式空间X/Y → VisionPoint | 原图像素或所选业务局部单位；局部输入必须绑定本帧坐标 |
| Vision.SelectPoint | IReadOnlyList<VisionPoint>＋Index → VisionPoint | 从卡尺MeasuredEdges或Blob的MeasuredCentroids选择；越界失败 |
| Vision.TransformPoint | VisionPoint＋可选目标定位 → VisionPoint | 保持原图位置，换目标局部表达；无目标时显式清除局部来源 |
| Vision.TransformLine | VisionLine＋可选目标定位 → VisionLine | 同帧两端点一起转换，原图几何不变 |
| Vision.GenerateLine | 两个VisionPoint → VisionLine | 非退化直线，端点同时定义有限线段；不冒充拟合 |
| Vision.MeasurePointDistance | 两个VisionPoint → GeometricDistanceResult | 同帧、共同坐标来源的点到点距离 |
| Vision.MeasurePointLineDistance | 点＋线＋Space＋Mode → GeometricDistanceResult | 无限直线垂足或有限线段最近点 |
| Vision.MeasureLineDistance | 两条线＋Space＋Mode → GeometricDistanceResult | 无限直线或有限线段最短距离 |

既有结果新增投影：BlobAnalysisResult.MeasuredCentroids、CaliperResult.MeasuredEdges、RobustLineResult.MeasuredA/MeasuredB/MeasuredLine、EdgeMeasurementResult.MeasuredA/MeasuredB/MeasuredLine，以及两种模板定位的MeasuredCenter。圆模型的MeasuredLine为空，不能将圆参数当直线。旧Centroid、Position、A/B和裸Coordinate2D保持原有语义。

## 坐标如何影响后续节点

VisionPoint保存FrameId、原图ImagePosition和可选VisionCoordinateSystem；LocalPosition由本帧逆矩阵计算。VisionLine两端点必须同帧、同定义语义签名、同映射矩阵；定义ID相同不足以证明可混用。

模板匹配（平移与旋转尺度）只输出位姿测量值：中心X/Y、角度(°)、缩放、参考点X/Y、参考方向(°)，角度顺时针为正；坐标系由“构建本帧坐标系”的模板方式生成。父坐标控制搜索，匹配输出已经是原图姿态，不能重复乘父矩阵。界面通过IVisionCoordinateResult识别坐标来源，外部节点也能加入来源列表。

运行链：

1. 获取图像，完成会生成新FrameId的预处理。
2. 选择独立业务定义，根据模板匹配结果、参数、双点、交线、矩阵或标定建立映射。
3. 成功输出本帧CoordinateSystem；制作页面显式绑定，保存定义ID、版本、语义签名及数据来源；模板方式的语义签名包含模板参考签名。
4. 局部配置按本帧定位转到原图，执行卡尺/Blob；也可由CreatePoint创建已知局部基准点。
5. 从本轮检测选择视觉点，或绑定拟合结果MeasuredLine。
6. 明确选择测量空间/距离模式，输出距离、最近点、坐标来源和原图叠加证据。

新距离单位为image-px、reference-px或明确标定的mm。相似尺度2下局部距离3对应原图距离6；一般仿射在所选局部空间计算垂足和最短距离，再回投原图，不能简单用原图距离除一个倍率。

未配置目标Coordinates的生成/测量节点保留输入来源，来源不同则拒绝。明确配置目标Coordinates时，两侧输入经同一原图换成共同目标表达。混合独立定位的同帧点需显式统一来源，或使用TransformPoint/TransformLine转回原图。转换不会将上一帧点搬到下一帧。

## 特殊情况

| 情况 | 处理 |
|---|---|
| 无限直线不平行 | 最短距离0，输出交点；即使有限端点不相交也如此 |
| 无限平行线 | 返回垂直距离；无量纲1e-12角度容差处理浮点旋转噪声 |
| 有限线段相交/重叠 | 距离0；否则取端点与内部投影的最小距离 |
| 垂足在线段外 | 无限模式返回垂足；线段模式返回最近端点 |
| 重合端点或原图长度小于1e-9px | 不能生成直线，明确失败 |
| 帧不一致、不同定位来源 | 拒绝；尺寸或定义ID相同也不足以接受 |
| 模板未找到 | 匹配正常完成、数值为NaN；构建节点停止，消费者不提交输出、不用旧矩阵 |
| 换模板/改坐标定义 | 签名/定义与制作配置不符时拒绝，需重新确认绑定 |
| 节点或引擎包丢失 | 缺节点保留未知节点原配置；缺实现运行前阻止，恢复包后重启 |
| 数值无效或超限 | 拒绝NaN/Infinity及超过±1e9的输入/生成点；遥远交点超限也失败 |

完成测量不等于产品合格，业务允差需由后续明确规则判断。

## 制作页面

面积节点继续保存局部ROI，按本帧矩阵显示。卡尺和几何节点也能选择定位来源，不显示无意义的面积绘制工具。

卡尺绑定时，将端点、MinimumSeparation、BandSampleStep逆变换为局部配置；解除时正变换，保持实际采样带。HalfWidth是单侧采样步数。鲁棒直线绑定/解除同时转换DistanceThreshold。实际原图长度、采样间隔和预算在执行时再次校验。

CreatePoint的Space和X/Y保持显式配置，绑定/解除定位不会猜测并重写数值或外部绑定。解除后仍选Local会在配置检查报错，需要明确选择Image并配置原图值。确认才写正式配方；取消、撤销继续遵循隔离编辑。

卡尺/鲁棒拟合要求正方向相似变换；非等比、剪切或镜像不会以单一尺度冒充。面积ROI及点线几何支持一般可逆仿射。同定义更换来源保留已保存局部ROI，不重复逆变换。

## 可直接复核的示例

在DP.WorkFlow目录构建后启动：

```powershell
dotnet run --project samples/DP.WorkFlow.WinForms.Sample/WinFormsApp_test.csproj -- --geometry-demo
dotnet run --project samples/Legacy/WpfApptest/WpfApptest.csproj -- --geometry-demo
```

流程是场景文件→模板文件→平移模板匹配→定义坐标系→构建本帧坐标系（模板方式）→4个局部点→2条直线→点线距离→线线距离。文件为VisionData/geometry-scene.pgm、geometry-template.pgm，不依赖相机和外部模型。两项距离均为2 reference-px，局部原点为模板中心，对应场景(7,7.5)。

人工复核：运行后检查点/直线/距离页面；移动场景中的模板后重跑，局部距离保持2，原图点随模板移动。删除目标后，模板未找到，构建节点及后续停止，不复用历史结果。保存重开，确认绑定、Space、Mode和算法选择保留。

两套命令将参数改为`--coordinate-demo`可复核独立工件中心定义：模板提供父姿态，构建节点将原点设在父坐标(2,1.5)，下游选择业务坐标，距离2 reference-px、业务ROI的Blob面积12。样图当前业务原点为(7,7.5)，不再是模板左上角。

## 尚需后续落地

SolveCalibration/MapCoordinate仍是裸矩阵/坐标，不与带身份几何隐式混用。通用构建已增加局部单位、定义版本、仿射点对拟合、适用尺寸及RMS；畸变/手眼标定、设备身份和有效期仍未实现。毫米声明和拟合残差不能证明现场精度。裁剪/校正仍需要新FrameId及明确回投映射。

自动测试入口GeometryMeasurementTests、VisionGeometryPluginPipelineTests覆盖几何真值、旋转/缩放、平行/相交/线段、混帧拒绝、取消、实际独立包、真实模板随动、失败无旧输出、JSON重跑、共享示例。记录见[插件人工复核](../plugins/vision-plugin-review.md)。
