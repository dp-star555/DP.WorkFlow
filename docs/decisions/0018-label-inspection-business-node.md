# ADR-0018：完整标签检测作为独立业务节点

状态：Accepted。

标签检测复用 DP.LabelInspection 的完整逐 ROI 引擎，作为 `LabelInspection.Inspect` 独立业务节点；不拆成 OCR/读码/质量基础节点，也不继承携带通用 ROI/坐标系语义的 AnalyzeVisionFrameNodeModel。可移植节点契约、Windows 资源宿主、共享编辑桥和原生 WinForms 页面分别部署，Workflow 内核及 DP.Vision 不反向依赖标签业务。

有效报告中的 Ok/Ng/Review 都是正常输出，Success 只代表返回报告，不代表产品通过或全部必检完成。配置/资源在运行准备中捕获实际快照，更新在下一轮生效；配方指定的字库修订仍保持其既有领域语义。编辑确认捕获完整配方到隔离副本，取消不回写；发布外部库修订不伪装成可随节点取消撤销。

首版选择完整报告与显式资源引用，而不承诺一键资源包、生产历史、通用坐标随动或 WPF Renderer。使用与验证范围见 [标签检测节点](../nodes/label-inspection.md)。
