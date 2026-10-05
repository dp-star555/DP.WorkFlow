# 新视觉插件接入模板

可从两个真实独立工程开始：

- [条码节点](../../src/Workflow/Nodes/DP.WorkFlow.Nodes.Vision.Barcode/ReadVisionBarcodeNode.cs)：中立读码接口、附加特征、精确 ROI、同帧事实与预览提交。
- [水平单行 OCR](../../src/Workflow/Nodes/DP.WorkFlow.Nodes.Vision.Ocr/RecognizeVisionTextLineNode.cs)：模型配置、明确依赖、矩形限制与可释放识别器。
- 新能力的完整独立契约/节点/引擎示例：tests/PluginFixtures/Intensity.Contracts、Intensity.Nodes、Intensity.Engine；测试宿主无编译引用。

新增模板匹配等已有能力，只部署节点包和实现包。新增此前没有的能力时，加一个共享契约 DLL 放入 plugins/contracts，再部署节点与引擎。工具箱依据 WorkflowNode 元数据，算法清单依据引擎模块登记，两者独立发现。

## 节点作者

1. 工程引用中立能力契约和 Nodes.Vision，不能引用具体引擎。
2. 模型声明 WorkflowNode 属性与稳定 NodeType，实现 IWorkflowVisionAlgorithmNode 并返回唯一槽位名、契约、选择与可选特征。Algorithm 持久化一次，标记 Browsable(false)，界面由通用参数提供者生成。
3. 模型自己的 ValidateConfiguration 检查输入绑定、几何与业务参数，不加载模型、不访问算法实例。引擎和资源可用性由宿主的静态检查/运行准备检查。
4. Handler 从 IWorkflowVisionAlgorithmBindings.Invoke/InvokeAsync 取得本轮已准备对象。槽位必须与模型声明一致；异步调用需等待完成以保护租约。
5. 视觉结果实现 IWorkflowVisionFrameFact，保留原图 FrameId 和实际结果说明；WorkflowVisionFrameScope.Stage 提供正常提交/回撤的预览投影。没有运行输出前不要提前发布预览。结果需要强类型端口，不能为插件化把一切改成 object 数据。
6. 公开无参 IWorkflowRuntimePluginModule 显式登记模型、类型化输出、Handler 和能力要求。放入 plugins/<packageId> 后重启，不修改宿主内置登记列表。

## 引擎作者

1. 公开无参 IVisionAlgorithmModule 只登记 VisionAlgorithmDescriptor 和工厂。不要在模块构造/登记时加载模型或校验许可。
2. 描述提供稳定 ImplementationId、Version、能力契约、Features、Parameters；文件参数需 IsFilePath。特征不是额外工具箱节点，例如读码的 masked。
3. 工厂 GetDependencies 明确声明依赖槽位；配方明确选择它们。不要在工厂偷偷 new 另一引擎。
4. IVisionAlgorithmConfigurationValidator 提供不初始化的版本/未知字段/必填设置规则。可用 WithConfigurationPolicy 组合简单规则；复杂工厂自行实现可选接口。
5. PrepareAsync 解释资源配置、验证实际环境、捕获内容快照，返回 VisionAlgorithmActivation。资源身份必须包括改变行为的初始化内容，模型建议使用内容哈希，不仅用文件名。
6. Activation 声明 Exclusive、SharedSerial 或 SharedConcurrent，CreateAsync 返回 VisionAlgorithmResource，并明确清理半成品和释放职责。共享不是默认线程安全；最后租约归还才释放，当前没有永久空闲模型缓存。
7. 发布不兼容配置版本时，通过 IVisionAlgorithmConfigurationMigrator 提供显式规则。未知版本无规则则拒绝，不修改调用方的原字典。
8. 包包含 deps.json、私有托管依赖和原生资产；模型放机器资源目录并由配方引用。不要发布同名异内容的宿主契约，也不要把私有依赖注册成共享契约。

样例投放目标在 samples/VisionData/AlgorithmPlugins.targets，宿主项目引用使用 ReferenceOutputAssembly=false、Private=false。构建引用仅用于演示自动投放；生产部署可独立构建并复制整包。节点/引擎 DLL 更新需要重启。

验收至少包括：不安装包时保持配方配置、恢复包后恢复节点、错误选择在首节点前失败、同资源共享和不同资源隔离、取消与资源释放、同帧事实以及实际引擎输入输出。不要只检查反射能找到类。

## 参数与依赖编辑

提供 Parameters 和 GetDependencies 后，主属性面板及节点详情窗口自动生成实现候选、类型化参数、版本与嵌套依赖分组。GetDependencies 和配置验证可能在每次编辑刷新时调用，应只读取不可变配置并返回轻量描述，不加载模型或做许可/网络检查。正式资源初始化仍在 PrepareAsync 中完成。

槽位名和参数 Id 是持久化身份，不要仅为了改显示名称而改 Id。嵌套选择不默认挑选候选；节点作者需要默认值时明确写入节点的默认选择。依赖声明随配置变化时，旧选择会保留并标注未知槽位；引擎验证应明确拒绝未声明的依赖，或提供显式迁移规则。

有配置的实现切换前，用户通过“高级”子组里的可撤销清空操作移除当前层参数与下级依赖。初始化参数与递归依赖直接在属性面板内展开，使用布尔、枚举、数值、文本和文件编辑器；不再把空参数字典或依赖字典显示为二次弹窗。未安装插件或未识别键按字符串子项保留；未知依赖也保留并标注诊断。界面展示用 GroupPath 分组，叶属性的稳定身份、配方 JSON 和统一撤销机制保持一致。新宿主只需给 Properties 设置 ChoiceProvider 和 AdditionalProperties，Studio 会将扩展传给隔离节点工作台，不需要按引擎注册专用编辑窗口。
