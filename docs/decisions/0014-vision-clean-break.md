# ADR-0014：删除旧视觉兼容，统一独立 DP.Vision

- 状态：Accepted
- 取代：ADR-0012 中旧视觉 Provider/Profile/Importer 及旧图像页面部分；通用 Module、Freeze、能力要求、PageProvider/Renderer 规则不变。

## Context

用户明确要求不保留旧兼容、删除旧代码并全面替换新版。双轨导致同程序集多 Module、两套图像所有权和旧宿主依赖继续存在，不符合长期独立视觉底座目标。

## Decision

1. 从 DP.WorkFlow 主线删除全部旧视觉工程（src/Vision）、旧节点/兼容 Adapter、旧页面、Paddle 节点和专属工具；移除 11 个旧解决方案工程、项目引用和旧依赖包条目。
2. 保留唯一 WorkflowImageRuntimePluginModule、十种明确语义节点、统一 IImageSource/ImageFrame 和原生双平台 FrameEditor。
3. 相机实现位于独立 DP.Vision.Halcon，直接使用 SDK，不通过旧设备服务。不提供伪造成功或运行中换后端。
4. 算法/设备测试属于 DP.Vision；Workflow 节点、真实 Manifest、未知旧类型拒绝运行以及 Studio 事务测试属于 Workflow。
5. 用户旧文档不覆盖、不静默转换、不新增版本迁移器。通用未知节点机制保留原始信息，运行绑定阶段明确拒绝。
6. WorkFlow.Rebuild 是主线之外的历史对照，本次不删除。DP.Vision 中其他业务仍使用的文本/质量算法及标签适配不属于 Workflow 旧兼容清理范围。

## Consequences

这是有意的破坏性删除：旧 CLR API、节点类型、Provider 配置和旧页面不可再执行。需要用新节点明确重建流程。

宿主固定能力装配，不再承诺旧 Profile/Importer 路径。相机当前每次请求打开/关闭，现场触发/性能、长连接设备管理和 WPF 物理输入仍需专项验收。定位/测量精度不能因 API 已存在而扩大承诺。

补充算子按独立中立接口和真实测试落地，建议优先级见[算子路线](../nodes/vision-operator-roadmap.md)。取消的整图 OCR、物理切字和无参考打印质量不恢复为工作流目标。
