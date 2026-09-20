# 节点实现盘点与优先级

> 盘点范围：新版 `DP.WorkFlow/src` 与旧版 `Base/DP.WorkFlow/UIModel/Nodes`、`DP.WorkFlow.Motion/Nodes`。
>
> 优先级含义：P0 = 生产闭环前必须完成；P1 = 首批设备流程必须完成；P2 = 按设备集成计划实现；P3 = 兼容性或体验增强。

## 1. 已完成保真验证的运行节点（13 个基础内核节点）

| 分类 | NodeType | 状态 |
|---|---|---|
| 边界 | `Start`、`End` | 模型、Handler、端口、测试已完成 |
| 通用执行 | `Action` | 动作注册表、绑定和测试已完成 |
| 判断比较 | `Decision`、`ValueCompare`、`StringCompare` | True/False 端口及结构化输出已完成 |
| 等待 | `WaitFunction`、`Delay` | 取消、Timeout 端口和测试已完成 |
| 控制流 | `Loop`、`Jump` | 显式端口、循环上限和测试已完成 |
| 并行 | `ParallelAll`、`WaitAllInputsCompleted` | 编译期汇聚、Token/Scope 和测试已完成 |
| 复合 | `Block` | 子画布、输入输出映射、监管运行和测试已完成 |
| 信号 | `SignalSet`、`SignalWait` | 注入式线程安全信号服务、无轮询等待和测试已完成 |
| 运控 IO | `IORead`、`IOWrite`、`IOWait`、`IOMultiCheck`、`IOMultiWait` | 独立 Motion 工程、服务接口和测试已完成 |
| 轴与气动 | `AxisAction/Servo/Stop/Wait`、`CylinderControl/Wait`、`VacuumControl/Wait` | 注入式设备服务和显式超时端口已完成 |
| 扫码 | `CodeReaderOpen/Close/Trigger/WaitScan` | 注入式扫码服务和结构化扫码结果已完成 |
| 流程恢复 | `SafePoint`、`WarnStopStation`、`WarningPrompt`、`OperatorChoice`、`OperatorStepConfirm` | 独立 Process 工程、恢复/操作员服务接口和测试已完成 |
| 转换与队列 | `ConvertValue`、`QueueAdd/Remove/Read/Wait` | 固定区域转换、线程安全非轮询队列和测试已完成 |

## 2. 已有新版骨架但必须重新进行保真重写的旧节点

抽象基类（例如 `AxisNodeBaseModel`、`InterruptibleIONodeBaseModel`）不计入节点数量，但需要用新版服务接口重新设计，不能直接复制旧实现。

### 2.1 通用与数据节点（ConvertValue 已完成）

| 优先级 | 节点 | 建议 |
|---|---|---|
| 已完成 | `ConvertValue` | 已提供常用目标类型和固定区域转换；动态 Schema 仍可继续增强 |
| P3 | `Label` | 不建议作为运行节点迁移，改为纯设计器 Annotation/Note |

### 2.2 信号与队列节点（11 个，已完成）

| 优先级 | 节点 |
|---|---|
| 已完成 | `SignalSet`、`SignalWait` |
| 已完成 | `SignalValueSet`、`SignalValueWait` |
| 已完成 | `WaitSignals` |
| 已完成 | `QueueAdd`、`QueueRemove`、`QueueRead`、`QueueWait` |
| 已完成 | `SignalStateBatchInitialize`、`SignalValueBatchInitialize` |

说明：批量初始化节点依赖 List/Dictionary 通用属性编辑器；队列与信号应先定义 UI 无关、线程安全、可注入的 Runtime Service，禁止使用旧版静态全局容器。

### 2.3 异常处理与恢复节点（9 个，已完成）

| 建议顺序 | 节点 |
|---|---|
| 已完成 | `SafePoint`、`WarnStopStation`、`WarningPrompt`、`OperatorChoice`、`OperatorStepConfirm` |
| 已完成 | `RetryCurrentNode`、`ReturnToSafePoint` |
| 已完成 | `WarningHandlerBlock`、`WarningHandlerStart` |

这些节点不能只实现 Handler；必须先补齐“故障挂起、操作员决策、从安全点恢复、重试身份、工站停止请求”的 Runtime 契约、快照字段和恢复测试。它们是设备动作节点进入生产使用前的门槛。

### 2.4 IO 节点（5 个，已完成）

| 优先级 | 节点 |
|---|---|
| 已完成 | `IORead`、`IOWrite`、`IOWait`、`IOMultiCheck`、`IOMultiWait` |

先定义 `IWorkflowIoService`、点位稳定 ID、仿真实现、超时/取消和批量读取一致性，再实现节点。禁止节点直接依赖具体板卡或 UI。

### 2.5 轴与气动节点（8 个，已完成）

| 分类 | 节点 |
|---|---|
| 轴 | `AxisAction`、`AxisServo`、`AxisStop`、`AxisWait` |
| 气缸 | `CylinderControl`、`CylinderWait` |
| 真空 | `VacuumControl`、`VacuumWait` |

建议顺序：`AxisStop` → `AxisServo` → `AxisWait` → `AxisAction`，然后实现气缸和真空。动作节点必须统一支持取消、超时、停止、设备报警和仿真服务。

### 2.6 扫码节点（4 个，已完成）

`CodeReaderOpen`、`CodeReaderClose`、`CodeReaderTrigger`、`CodeReaderWaitScan`。

先定义 `ICodeReaderService` 和扫码结果 Schema；`WaitScan` 需要 Success/Timeout/Failed 等明确端口，不复用旧版隐式分支。

### 2.7 产品流与工站节点（10 个，已完成）

| 分类 | 节点 |
|---|---|
| 产品移动 | `ProductMoveStart`、`ProductMoveSuccess`、`ProductMoveFailed` |
| 产品 | `ProductCreate` |
| 工站判断 | `StationCanReceive`、`StationCanSend`、`StationSlotHasProduct` |
| 工站动作/等待 | `StationFinished`、`StationWaitCanReceive`、`StationWaitCanSend` |

依赖产品、工站、槽位和移动会话的稳定领域接口。应先实现只读判断节点，再实现会改变产品归属的动作节点；移动开始/成功/失败必须具备事务身份和幂等规则。

### 2.8 晶圆机器人节点（8 个，已完成）

`WaferRobotInitialize`、`WaferRobotHome`、`WaferRobotReadSnapshot`、`WaferRobotStop`、`WaferRobotWaitIdle`、`WaferRobotMove`、`WaferRobotPick`、`WaferRobotPlace`。

建议顺序：ReadSnapshot → Stop → Initialize → Home → WaitIdle → Move → Pick → Place。Pick/Place 必须在产品流事务契约稳定后实现，避免机器人状态与产品槽位状态不一致。

## 3. 推荐实施批次

### Milestone N1：基础数据闭环（P0，进行中）

1. [ ] 动态 Action/插件输出 Schema；
2. [ ] `ConvertValue`；
3. [x] 布尔信号服务及 `SignalSet/SignalWait`；
4. [ ] `SignalValueSet/SignalValueWait`；
5. [ ] Dictionary/List/复杂对象属性编辑器；
6. [ ] 对应 JSON 迁移和绑定分析测试。

### Milestone N2：安全与异常恢复（P0）

1. Runtime 故障挂起/恢复契约；
2. SafePoint、WarningPrompt；
3. 操作员选择/确认；
4. Retry、ReturnToSafePoint、StopStation；
5. WarningHandler Block/Start；
6. 根流程、并行分支和 Block 内故障恢复测试。

### Milestone N3：通用设备控制（P0-P1）

1. IO Service + IORead/Write/Wait；
2. IO 多点判断/等待；
3. Axis Service + Stop/Servo/Wait/Action；
4. Cylinder/Vacuum；
5. 每类服务都提供 Fake/Simulator 和取消、超时、报警测试。

### Milestone N4：设备与领域插件（P2）

1. CodeReader；
2. 产品流和工站；
3. WaferRobot；
4. 旧 JSON 节点配置迁移。

### Milestone N5：兼容与设计体验（P3）

1. Label 改为 Annotation；
2. 旧 NodeType 别名和配置版本迁移；
3. 插件缺失/版本不兼容诊断；
4. 节点图标、帮助、示例流程和端到端测试。

## 4. 每个节点的完成标准

节点只有同时满足以下条件才标记为 Completed：

1. 模型不依赖 WinForms/WPF；
2. NodeType、NodeVersion、输入输出端口稳定；
3. Handler 使用注入服务，不直接访问静态设备对象；
4. 支持取消、超时和异常映射；
5. 输出类型进入 Descriptor/动态 Schema，可用于设计期绑定；
6. WinForms/WPF 参数编辑可用；
7. Schema 4 Document JSON 往返及旧配置迁移完成；
8. 顺序、分支、并行和 Block 场景测试完成；
9. 运行快照、Trace 和错误消息可诊断；
10. 新工程保持 0 warning / 0 error。

## 5. 当前最高优先级结论

旧版 NodeType 虽已有模型和 Catalog 骨架，但保真审计确认多个后续节点的参数与执行语义不符合旧实现，不能视为 Completed。具体问题和重写规则见 [节点重写保真审计](fidelity-audit.md)。

下一阶段不再扩充 NodeType，重点转为生产级收口：

`复杂集合属性编辑器 → 动态输出 Schema → 逐节点旧 JSON 配置迁移 → 设备 Simulator/报警测试 → 异常恢复端到端测试 → Product/Robot 事务一致性测试`。

“已覆盖 NodeType”不等于全部达到生产验收标准；只有满足上节十项完成标准后，节点才从 Implemented 提升为 Completed。
