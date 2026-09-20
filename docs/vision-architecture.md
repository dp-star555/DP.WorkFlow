# 视觉架构：独立 DP.Vision，单一新版链路

旧视觉工程、节点、页面和兼容 Adapter 已删除。不是通过 Compile Remove、条件编译或默认隐藏保留两套实现。

```text
DP.Vision                       统一图像租约、帧身份、纯几何与精确Region
  ↑
DP.Vision.Algorithms            中立采集、Blob、颜色、测量、定位、标定契约
  ↑
DP.Vision.OpenCv / .Halcon      独立实际实现，SDK对象不进入Workflow

DP.WorkFlow.Nodes.Vision        NodeModel + WorkflowInput<T> + Handler + 能力要求
  ↓
WorkflowImageRuntimePluginModule → Catalog Freeze
Document → Compiler → Plan → RuntimeBinder → BoundPlan
  → 能力预检 → 文件夹/帧仓准备 → Engine → 标准事实输出

DP.WorkFlow.Vision.UI           隔离编辑模型、同帧预览、ROI事务
  → DP.Vision.UI                共享RoiEditor / IVisionCanvas
  → DP.Vision.Winform / .WPF    两个真正原生画布，不使用HALCON窗口或WPF互操作宿主
```

## 所有权

- Workflow Kernel 不引用 Vision 或 SDK；Nodes.Vision 只依赖 Workflow.Abstractions 与 DP.Vision.Algorithms。
- DP.Vision 不反向引用 Workflow、标签业务或桌面工作台。
- 宿主选择实际接口实现，运行中不得替换；没有旧 Profile/Provider/Importer 自动选择体系。
- `WorkflowImageRuntimePluginModule` 是节点程序集唯一 Runtime Module；直接注册入口为 `RegisterImageNodes / RegisterImageNodeHandlers`。
- 页面统一 RendererKey `DP.Vision.FrameEditor`，平台不匹配在创建控件前失败。

## 数据和生命周期

使用 `IImageSource / ImageFrame` 和强类型事实；不把 SDK 原生对象、活动编辑器或弱类型参数字典作为节点契约。

输入帧的 Image 是借用句柄。运行帧仓拥有标准输出租约，UI 通过 Retain 独立持有；默认每轮 512MiB/1024 帧，超限失败，不淘汰仍可被下游绑定的输出。预览只保留每节点最新帧，但检测任务和标准事实不能被预览丢帧替代。

结果 FrameId 必须匹配输入，模板结果还必须匹配模板 FrameId。像素变化换身份；仅预览更新使用序号。成功执行不等于产品合格；空 Blob/空定位为正常完成，证据不足无法拟合则失败，失败不提交输出/暂存变量。

坐标统一原图像素边界和半开范围；只有明确源为像素中心时才转换。新增共享定位坐标系以定义ID、模板内容签名和FrameId约束正反矩阵；显式绑定的局部ROI先连续变换再栅格化，不修改文档或借用旧矩阵。参见[定位坐标系](nodes/vision-coordinate-systems.md)。ROI 配置与结果图元分开，包含并集减排除并集保留孔洞；编辑只改副本，确认一次提交，取消不污染正式文档。

## 相机与验证边界

`DP.Vision.Halcon` 直接通过 HFramegrabber 获取真实图像并复制为中立租约；没有旧设备服务 Adapter。当前按请求打开/关闭，非长连接高帧率设备栈。设备在线、许可证、曝光、触发和现场吞吐必须实机验收。SDK复制边界测试使用真实 HALCON 合成图像，不冒充相机测试。

平移定位与Canny线/圆测量保留原图结果语义，并已接入精确ROI、定位随动与双坐标表达；平移定位的Bounds仅为诊断外接矩形，实际范围使用MatchGeometry。范围解析和页面依赖能力声明，旋转尺度定位可绑定父坐标系产生子定位；新增算子提供梯度峰插值卡尺、RANSAC直线拟合、离散旋转/尺度模板搜索及正反坐标映射。详细边界见[新增算子](nodes/vision-operators.md)：不是连续形状模型、鲁棒圆拟合或现场精度认证。WPF物理输入仍需要现场验证。

详见[节点与宿主用法](nodes/new-vision-file-pipeline.md)、[算子补充路线](nodes/vision-operator-roadmap.md)、[清理记录](dp-vision-integration-plan.md)。
