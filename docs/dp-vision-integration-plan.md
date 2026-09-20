# 独立 DP.Vision 接入与旧视觉退役记录

## 最新：全部面积ROI算子接入坐标系

范围能力改为`RangeCapability`声明，公开`ResolveRange`统一解析定位、连续ROI及精确Mask；共享UI不再维护具体节点类型白名单。新增线圆测量、平移定位、旋转尺度定位搜索范围随动；平移匹配使用固定父姿态，旋转尺度匹配支持相对父姿态的候选，产生独立子坐标系，禁止自身绑定。模板候选的整个有效采样足迹受ROI/孔洞约束；显示实际匹配轮廓，不把外接框充当范围。

最终Workflow **663/663**（Windows **339**）通过，坐标管线10项；DP.Vision两框架各核心 **115**、算法 **67**、HALCON边界 **5**，原生探针与Demo smoke通过。Workflow Debug/Release、独立WPF Debug/Release均零警告/错误，Solution43/43。

期间既有`Select_KeyboardNavigation_HomeEndAndPageKeysUseVisibleRowCount`一次出现下拉框关闭断言失败；独立及最终全量通过，未修改该控件或测试，未宣称间歇根因已修复。现场与物理输入边界不变。

## 历史：模板定位坐标系首轮

已接入共享定位身份/正反矩阵、模板像素签名、局部ROI随动及双坐标结果。Blob/颜色/阈值Region、卡尺、鲁棒直线显式绑定；两平台面积ROI页面支持制作时逆变换、隔离确认/Undo、换图只读刷新和显式解除。当前仍18节点，不新增全局当前矩阵。用法与限制见[定位坐标系机制](nodes/vision-coordinate-systems.md)。

最终Workflow **661/661**（Windows **337**）通过；DP.Vision两框架各核心 **115**、算法 **63**、HALCON边界 **5**，原生探针及Demo smoke通过。Workflow Debug/Release、独立WPF Debug/Release均零警告/错误，Solution 43/43。新坐标集成8项、算法数据用例6项；验证真实模板匹配后的平移旋转随动、同帧身份、换模板拒绝、失败不提交、编辑隔离与无效预览恢复。

一次默认NuGet还原停留在“确定要还原的项目”并超时，未宣称网络根因已修复；两个验证脚本新增可选`-NoRestore`，使用已还原依赖完成全量验证。相机现场及WPF物理输入仍未验收。

## 历史：算子增补

已在同一Module新增8个节点，总数18：预处理、阈值Region、形态学、Blob筛选、亚像素采样卡尺、鲁棒直线、离散旋转/尺度定位、姿态坐标映射。实现、能力装配、JSON/绑定、同帧验证、原生证据页面及双宿主默认算子流程均已接入。相机现场工作按用户安排延期。

用法与边界见[新增算子](nodes/vision-operators.md)。本轮最终Workflow全量 **653/653**（Windows **329**）；DP.Vision两框架分别核心 **92**、算法 **57**、HALCON边界 **5**，原生探针与Demo smoke通过。Workflow Release、WPF Debug/Release均零警告/错误，Solution仍43/43。

期间原有net48脚本worker三秒硬超时测试再次出现一次超时，独立及最终全量通过；未更改或宣称修复其间歇根因。下方记录是上一轮旧视觉退役时的十节点/644测试验收基线，不代表当前节点数量。

## 当前结果

主线已经从“新旧并存”改为**只保留新版视觉链路**。旧代码是物理删除，不是仅隐藏工具箱、条件排除编译或留下兼容注册入口。决定见[ADR-0014](decisions/0014-vision-clean-break.md)。

### 删除范围

- `src/Vision` 全部八个旧 Kernel/Provider/Desktop 工程。
- `DP.WorkFlow.Nodes.Vision.PaddleOcr`、旧视觉 NodeModel/Handler/Module、旧相机 Adapter、旧运行准备/Profile 绑定。
- 旧显示 Hub、Overlay/ROI 页面、HALCON 原生视口及详情窗口。
- 两个旧视觉测试工程；新版集成测试保留并改用新注册入口，通用属性检查器测试改为类型化 Blob/Region 场景。
- Paddle 专属离线部署脚本、训练工作台及其压缩包；旧项目引用、宿主服务与不再使用的中心包版本。
- 合计从 Workflow Solution 移除11个旧工程，增加独立新版节点测试工程；当前43/43项目核对通过。

`WorkFlow.Rebuild` 是主线之外的历史对照，未删除。其他业务使用的 DP.Vision 文本/质量算法和标签适配未动。历史 ADR/Phase 记录不代表旧 API 仍可用。

## 新版能力

| 范围 | 独立实现 | Workflow契约 |
|---|---|---|
| 文件采集 | IImageFileReader / OpenCvImageFileReader，保留位深 | Vision.LoadFile |
| 文件夹 | 准备时冻结Ordinal清单、成功后推进、重跑归零 | Vision.LoadFolder |
| 相机 | DP.Vision.Halcon直接HFramegrabber采集并复制中立像素 | Vision.CaptureFrame |
| 连通域 | 闭区间阈值、4/8连通、面积、精确Region与质心 | Vision.AnalyzeBlobs |
| 颜色 | 明确编码RGB均值、Alpha不加权 | Vision.AnalyzeColor |
| 测量 | Canny真实边缘、正交线/代数圆拟合与RMS | Vision.MeasureEdges |
| 定位 | 固定旋转/尺度的平移匹配，分数不是概率 | Vision.LocateTemplate |
| 标定 | 中心化/尺度归一仿射及目标旋转中心 | Vision.SolveCalibration |
| 映射 | 显式弧度和标定坐标映射 | Vision.MapCoordinate |
| 几何 | 两点距离/方向，有限值及溢出检查 | Vision.MeasureDistance |

节点程序集仅一个 `WorkflowImageRuntimePluginModule`，直接注册使用 `RegisterImageNodes / RegisterImageNodeHandlers`。宿主固定装配中立服务，统一能力预检，不保留旧Profile/Provider/Importer选择体系。

图像统一 IImageSource/ImageFrame；运行帧仓拥有输出租约，默认512MiB/1024帧，超限失败而不是淘汰仍可绑定的历史输出。UI独立Retain，FrameId/模板FrameId验证后才发布事实；预览序号不替代内容身份。双平台原生FrameEditor共用RoiEditor，编辑副本确认一次提交/Undo，取消不改文档。

## 宿主与文档

- WinForms、独立WPF示例均使用新版服务和原生图像页，启动示例为Start→文件→Blob→颜色，不需要相机或模型。
- 新HALCON实现按请求打开/关闭设备；没有SDK时明确不可用，不返回模拟图像。详见[设备边界](../../DP.Vision/src/DP.Vision.Halcon/README.md)。
- 关闭先禁用Run，等待StopAsync退出，再释放帧/工作区。
- 不覆盖用户旧文档、不静默改NodeType、不新增版本迁移器。未知旧类型通过通用未知节点机制保留原始信息，运行绑定明确拒绝。
- 用法见[节点与示例](nodes/new-vision-file-pipeline.md)。

## 本轮验证

顺序执行，避免共享obj锁：

- Workflow完整测试：**644/644**，含Windows UI **320/320**。
- Workflow Debug/Release、独立WPF Debug/Release：**0警告、0错误**。
- Solution项目核对：**43/43**。
- DP.Vision Release verify：核心 **81**、算法 **46**、真实HALCON边界 **5**，均在net48/net8两框架通过；两个原生画布探针和四组Demo smoke通过。
- 显式无SDK构建：两框架各 **3** 测试通过，证明不提供伪造采集成功。
- 节点测试覆盖唯一Manifest模块、无旧工程/引用、旧NodeType不被静默转换且不能运行；SDK测试覆盖Gray8/Gray16/RGB真实像素、释放边界、取消、预算及不支持格式。

移除旧UI改变测试首用顺序后，确定性复现了“先PMv2，后96-DPI富文本得到13.5pt而非9pt”的默认字体缓存污染。UiTestThread现在同时在96-DPI初始化DeviceDpi和惰性默认Font；回归保留高DPI→富文本/时间控件/复合控件真实调用链，不放宽断言。

期间也出现过脚本worker三秒硬超时和少量模态UI测试超时；独立检查和最终完整测试通过，但未证明这些间歇现象的根因已消除。

## 保留的验证边界与后续建议

实际相机曝光/触发/现场吞吐、长连接采集管理和WPF物理输入尚未实机验收。像素边缘拟合不等于亚像素卡尺；平移匹配不等于旋转/尺度形状定位。

建议先补显式预处理、Region形态学、Blob特征筛选，再补亚像素卡尺、鲁棒拟合及带坐标变换的姿态定位；完整验收语义见[算子路线](nodes/vision-operator-roadmap.md)。这些是建议，未冒充已实现。取消的整图OCR、物理切字和无参考打印质量不恢复为Workflow目标。
