# ADR-0012：能力插件与节点详情页组合

- 状态：Accepted；视觉 Provider/Profile/Importer 与旧页面部分已由 [ADR-0014](0014-vision-clean-break.md) 取代。以下相关描述为历史决策，不是当前 API。
- 日期：2026-09-03

工作流文档保存稳定节点语义和视觉操作，不保存插件 CLR 类型。视觉节点通过 `VisionBackendSelection` 选择明确 Provider、部署侧后端配置或确定性自动解析；运行准备阶段递归收集根计划和子计划的视觉需求，由 `VisionRuntime` 冻结注册集合并一次创建运行专用 `VisionOperationBindingSet`。节点执行只读取该不可变集合，不再解析 Provider。运行插件、视觉 Provider 插件和桌面 Studio 插件通过 Manifest v1 `plugin.json` 的独立 Module 分组装载；插件依赖必须完整且无环，后台宿主不加载 WinForms/WPF 程序集。

节点详情编辑统一由页面提供器和平台 Renderer 组合。Blob、Fixture、Measure、Color 等普通视觉操作共用图像、命名过程帧选择和 ROI 页面；只有确有独特交互模型的节点才贡献专属页面。Provider 绑定属于只读运行状态，不进入节点编辑事务。页面提供器只创建 UI 无关强类型模型，Renderer 通过稳定 `RendererKey` 直接解析并声明接受的 `ModelType`；同一页面槽位使用独立 Priority 替换，重复扩展标识、RendererKey 或同优先级槽位必须在打开编辑器前失败。缺少当前平台 Renderer 或模型类型不匹配同样属于组合错误，不能退化为占位页面。Block、Script、Image 与第三方轨迹规划页面使用同一扩展机制，节点模型不得引用桌面控件类型；平台 Studio 通过 `IWorkflowBlockMappingNode` 使用映射能力，不引用具体 `BlockNodeModel`。

Runtime Module 统一成组贡献节点、Handler 和运行能力要求。组合完成后必须 Freeze：节点工厂必须产生与描述一致的模型，每种节点必须唯一匹配一个 Handler，此后目录不可变。能力要求可以是固定集合，也可以从冻结节点配置确定；RuntimeBinder 将其写入绑定计划，RuntimeHost 在调用运行准备 Module 及启动引擎之前递归预检根计划和子计划。缺失能力是运行准备失败，不是节点执行到一半才发生的故障。

## Consequences

插件程序集视为进程内受信任代码，新增插件通常需要重启宿主。后端配置可以在不修改工作流文档的情况下切换实际视觉实现，但一次运行开始后不得因算法异常自动换用其他 Provider。跨 Provider 可移植节点必须使用公共参数和强类型事实契约；Provider 专用参数或节点必须明确标识其不可移植性。
