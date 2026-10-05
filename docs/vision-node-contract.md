# 视觉节点契约

当前只提供 `WorkflowImageRuntimePluginModule` 的18种新版节点，类型、参数和标准输出见[使用说明](nodes/new-vision-file-pipeline.md)。旧视觉契约已删除，不提供兼容别名或静默文档转换。

## 节点职责

1. NodeModel 保存明确参数、WorkflowInput<T> 绑定、ROI 配置，不保存 SDK、服务或 UI 对象。
2. Handler 解析输入并调用已装配中立能力；算法属于独立 DP.Vision，不能在 Workflow 中复制第二套算法。
3. Module 注册节点/Handler，并声明必需能力。Catalog Freeze 校验唯一绑定；RuntimeHost 运行前递归预检根/子计划服务。
4. 控制出口与事实输出分离。Success 只表示契约完成，产品判定交给后续比较/业务节点。空 Blob、空定位不是执行异常；无法拟合、非法范围、离线设备是失败。
5. 失败不提交标准输出和暂存数据。局部变量只做显式命名暂存，公共数据只能显式发布。

## 图像与事实

- API：IImageSource / ImageFrame。ImageBuffer 是内部实现，不进入客户契约。
- 输入为借用；跨调用或窗口持有必须 Retain，消费者只释放自己的租约。
- 采集输出由运行帧仓拥有；关闭和重置前先等待在途运行退出。
- 检测证据 FrameId 必须与输入一致；模板结果另校验 TemplateFrameId。
- 像素变化必须产生新的 FrameId，预览刷新序号不能冒充新内容身份。
- 结果不含产品级结论；RMS、模板分数和 RGB 均值不是合格判定或概率。

## 范围与编辑

- 原图像素边界坐标，半开范围，显式弧度。坐标绑定启用时ROI/卡尺端点保存为业务局部坐标，运行时显式转换；既有结果点仍为原图坐标，另提供带身份的双坐标表达。
- 模板匹配只输出位姿测量值；坐标系由“构建本帧坐标系”生成，必须匹配当前FrameId、尺寸及制作时的定义ID/版本/语义签名（模板方式含模板参考签名）；模板未找到时不构建，消费者不得沿用旧矩阵。见[定位坐标系与ROI随动](nodes/vision-coordinate-systems.md)。
- Blob/颜色支持面积 ROI 的包含/排除组合，精确 Region 不得用外接框替代。
- 面积范围能力通过RangeCapability声明；Blob/颜色/阈值Region/线圆测量/两类模板定位均支持精确ROI及定位绑定。测量先取真实边缘再筛选ROI；模板候选有效采样足迹须完整落在ROI内，支持父子定位但禁止自身绑定。卡尺使用采样带，形态学/筛选处理输入事实，预处理作用于整图。
- 后台仅消费独立配置快照，不访问活动 RoiEditor。
- 页面编辑 EditingNode，确认一次提交/Undo；取消、打开、导航、手动预览不改正式配置。

## 不支持的旧能力

没有 ParametersJson 万能工具节点、旧 Profile/Provider/Importer 路由、旧显示 Hub 或 HALCON 桌面窗口。旧 NodeType 通过通用未知节点机制保留原始文档信息，但无法绑定 Handler 运行；不覆盖用户文件、不新增迁移器。

新增能力应采用独立中立接口、强类型选项与事实、明确 FrameId/坐标变换和真实 seam 测试；建议路线见[算子补充](nodes/vision-operator-roadmap.md)。
