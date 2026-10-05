# 业务坐标定义、本帧变换与ROI随动

更新于2026-10-02。坐标系可以独立于模板构建。内置19种视觉节点，独立几何包10种，条码/OCR各1种，共31种部署节点。

2026-10-03新增[节点内模板制作](vision-template-authoring.md)：资源模型采用明确参考原点/方向，模型内容和坐标定义分开。

2026-10-05起模板匹配只输出位姿测量值（中心、角度°、缩放、参考点及参考方向，角度顺时针为正），不再输出坐标系；坐标系统一由“构建本帧坐标系”生成，模板方式（Template）直接绑定匹配结果。

## 三个独立概念

| 对象 | 内容 | 生命周期 |
|---|---|---|
| VisionCoordinateDefinition | 业务ID、名称、版本、局部单位、原点及轴含义、语义签名 | 配方中的稳定定义，不含图像或模板 |
| VisionCoordinateSystem | 定义、本帧FrameId/尺寸、LocalToImage、ImageToLocal、来源信息 | 本帧不可变映射，不保存到配方 |
| WorkflowVisionCoordinateBinding | 来源数据绑定＋制作时定义ID/版本/语义签名 | 下游节点显式选择坐标，持久化在配方 |

同文档定义由WorkflowVisionCoordinateCatalog统筹。定义节点保存配置，构建节点引用它；一个定义可有多个来源。目录只读取配置元数据，运行矩阵通过工作流绑定传递，没有进程级“当前矩阵”或上一帧回退。

修改名称不会使ROI失效；修改原点含义、轴约定、单位或版本会改变语义签名。改变基准关系、标定、固定父坐标偏移的业务含义时，应主动递增定义版本；程序不能从描述文字或任意数值变化推断物理基准是否改变。

## 节点和构建参数

现有plugins/workflow.vision.geometry包新增两个节点，不增加DLL数量：

- Vision.DefineCoordinateSystem：保存独立业务定义，输出VisionCoordinateDefinition；同文档ID唯一。
- Vision.BuildCoordinateSystem：绑定图像和定义，输出VisionCoordinateSystemResult；通过CoordinateSystem成员供下游选择，同时预览本帧原点和轴方向。

| Mode | 关键参数 | 规则 |
|---|---|---|
| Template | Template（模板匹配结果）、Scale | 原点取模板参考点、X轴取参考方向；Scale为模板像素/局部单位。坐标定义并入模板参考签名，模板参考变化后下游ROI被拒绝。未找到目标时停止，不构建坐标系 |
| Pose | OriginX/Y、Angle(°)、Scale | 原点为原图像素边界，角度单位度、顺时针为正；Scale为像素/局部单位。数值可绑定上游，例如模板匹配的中心X/Y与角度 |
| TwoPoints | OriginPoint、DirectionPoint、ReferenceLength | 同帧两个视觉点；首点为原点，首点到次点为正X；像素点距/已知局部长度确定尺度 |
| LineIntersection | AxisLine、CrossLine、Scale | 两线延长线交点作原点，首线A→B为正X；拒绝平行/近平行 |
| Parent | Coordinates父绑定、OriginX/Y、Angle(°)、Scale | 业务→父的固定关系与本帧父→原图组合；原点和尺度用父单位解释。模板只是父来源之一 |
| Matrix | M11/M12/Tx/M21/M22/Ty | 明确局部→原图的可逆仿射矩阵，系数可绑定上游；支持剪切及非等比 |
| Correspondences | Samples、CalibrationImageWidth/Height、MaximumRms | 3..1024个不共线局部/原图点对求仿射；固定标定限定图像尺寸，检查拟合RMS |

点对通过两平台已有集合表格编辑，列为LocalX、LocalY、ImageX、ImageY，也可编辑结构化配置。界面按构建方式显示参数。定义必须直接绑定本文档定义节点根输出`$`，不接受运行对象Literal或公共数据伪装成稳定定义。

Parent采用列向量：`T业务→原图 = T父→原图 × T业务→父`，不重复乘定位矩阵。原点可在图像外，实际ROI/采样足迹仍受边界检查。

Pose固定数值适合固定夹具/相机基准，不会自动跟踪移动工件。移动工件应使用Template，或绑定本帧检测值、使用TwoPoints/LineIntersection/Parent动态来源；多个模板结果可用参考点组成TwoPoints。

Template方式的参考点：资源模板取模板制作时设置的参考原点和方向；图像绑定的动态模板取模板中心、沿模板X轴，参考签名来自模板像素。资源模板的参考签名可在编译时与下游ROI核对；动态模板的签名只在运行时核对，编译时仍检查定义ID和版本。

## 制作与换图运行

样图构建所选定义的映射Tref，绘制图上ROI，点击“绑定/更换坐标系”：

```text
保存ROI局部 = inverse(Tref) × 样图ROI
本帧ROI图上 = Tcurrent × 保存ROI局部
```

换图只重建Tcurrent并重新检测，局部配置不改写、不累计变换。等效样图补偿为`Tcurrent × inverse(Tref)`。业务原点可以在模板中心、孔中心或两线交点，不必是模板左上角。

1. 配置并执行图像来源、定义和构建节点。先完成会产生新FrameId的预处理，再构建坐标。
2. 下游面积节点绑定同一图像，打开“图像与测量范围”，显示输入图像，先绘制包含ROI。
3. 选择来源并绑定：图上ROI逆变换为业务局部形状。通用定义没有隐含模板边界，未绘制时会要求先绘制。
4. 继续在图上编辑矩形、旋转矩形、椭圆、多边形或排除区域。确认才提交，取消/Undo/Redo沿用隔离编辑。
5. 换图执行后，ROI和重新检测的事实按本帧映射显示。

已绑定时换成同定义的其他来源，只改变绑定，保留局部ROI/参数。其他业务定义不能直接替换：先在有效同帧图像上“解除坐标系转原图”，再绑定目标定义。

来源列表按输出是否实现IVisionCoordinateResult发现，构建节点和外部结果都能加入，模板匹配节点本身不是坐标来源；通用来源显示名称、版本和单位。公共数据坐标可运行，但交互制作需要直接节点预览，不猜测来源。模板/手动图像不能借用其他帧矩阵编辑。

制作时使用`WorkflowVisionCoordinateBinding.Capture(sourceNodeId, referenceSystem)`保存定义身份与来源。System为WorkflowInput<VisionCoordinateSystem>，引用CoordinateSystem成员。配方只存此绑定和局部Regions，不存referenceSystem或运行矩阵。未绑定时Coordinates=null，沿用原图范围。

## 算子和单位

- 面积ROI先精确连续变换，再在本帧栅格化；剪切矩形变成真实多边形，椭圆使用等价仿射椭圆，不用外接框代替。包含并集减排除并集，可再与同帧Mask相交。
- 卡尺端点、MinimumSeparation、BandSampleStep及鲁棒直线DistanceThreshold在绑定/解除时换算；HalfWidth是单侧采样步数。这些算子要求正方向相似变换，剪切/非等比/镜像明确失败，制作失败时不部分修改配置。
- 模板匹配的父搜索坐标也要求相似变换，保留现有0.1..10尺度与候选预算，不声称引擎支持一般仿射。
- VisionPoint/VisionLine保留原图位置和业务表达；距离在所选Image或Local空间计算。仿射下两种垂足可能不同，按所选空间求最近点，再回投原图。
- 单位为image-px、reference-px或明确配置的mm。选择毫米不产生标定，必须提供可靠的已知长度、点对或单位明确的矩阵。倍率和拟合RMS不能证明现场物理精度。
- 面积、形态学核及灰度梯度仍是原图单位。圆LocalRadius与拟合LocalRmsError只在相似变换时提供，一般仿射返回空。

旧Centroid、Position、A/B保持原图语义，LocatedPoint增加Definition单位；MeasuredCentroids/MeasuredEdges/MeasuredLine供几何节点直接绑定。Region仍是本帧像素游程。检测重新执行，不变换上一帧结果。

## 校验和特殊情况

| 阶段/情况 | 处理 |
|---|---|
| 编译 | 检查定义内容、同作用域重复ID、引用丢失、类型和路径可见性；静态可解析时核对ROI制作身份 |
| 常量配置 | 退化矩阵、无效数值、共线标定及超RMS提前拒绝；绑定数值在运行时检查 |
| 运行准备 | 引擎/资源检查沿用插件准备，不被坐标编译检查替代 |
| 本帧构建 | 检查点/线同帧、FrameId/尺寸、可逆性、退化及标定尺寸/残差 |
| 消费者 | 验证本帧身份＋定义ID/版本/语义签名；缺映射即失败，无恒等回退 |
| 模板未找到 | 模板匹配正常完成、数值输出为NaN；构建节点停止，消费者不复用旧矩阵或提交旧输出 |
| 换定义/单位/基准 | 制作身份不符，需要明确重新确认；只改显示名称可继续 |
| ROI越界、删除全部或只有排除范围 | 失败，不静默裁剪或扩大成全图 |
| 几何事实换表达 | 只允许同帧，经共同原图显式转换，不能将旧帧事实转成新帧 |
| 复合/分支流程 | 定义目录限当前文档作用域；数据绑定仍受路径可见性约束，不是任意全局变换图 |

## 兼容及边界

模板匹配节点不再有坐标系定义ID，绑定不再保存模板像素签名，模板像素坐标单位和TemplateLocal别名已删除；旧配方需要按“模板匹配→构建本帧坐标系（Template）→下游绑定”重新制作。

共享契约的参数类型已扩展，外部编译插件须与新契约重新构建；JSON兼容不代表旧二进制方法签名兼容。

旧SolveCalibration/MapCoordinate仍是裸矩阵/坐标，不自动成为带身份事实。新Correspondences支持平面仿射拟合，尚无畸变补偿、手眼标定、设备/标定有效期自动审计、模板资产工作台或裁剪图像回投。镜头和设备基准变化须更新标定及定义版本。

## 复核入口

两套示例支持`--coordinate-demo`：平移模板匹配→工件中心定义→模板方式构建本帧坐标→点线测量→业务ROI的Blob分析。两项距离为2 reference-px，ROI覆盖4×3模板，总面积12。`--geometry-demo`为同一坐标链路上的点线测量，不含ROI。

GeneralCoordinateSystemTests覆盖独立定义、参考ROI、双点/交线、父子组合、仿射几何及局部距离；VisionGeometryPluginPipelineTests覆盖真实插件、编译错误、构建方式、JSON、随动/失败、来源更换、表格标定及示例。VisionCoordinateSystemTests和VisionCoordinatePipelineTests验证模板方式构建后的ROI、卡尺、拟合随动及换模板拒绝。实际结果见[插件复核记录](../plugins/vision-plugin-review.md)。
