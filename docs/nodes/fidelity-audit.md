# 节点重写保真审计

> 本文以旧版 `Base/` 和 `DP.WorkFlow.Motion/` 为唯一功能基线。新版允许替换静态依赖、UI 编辑器和执行架构，但不得自行改变节点业务语义、配置参数、结果结构或分支规则。

## 审计结论

此前“旧 NodeType 已全部完成”的结论无效。后续批量生成的部分节点只完成了 NodeType/Catalog 骨架，存在功能和参数失真，必须逐个按旧实现重写后才能标记 Completed。

## 已确认的主要失真

| 节点/类别 | 旧版真实语义 | 当前骨架问题 |
|---|---|---|
| AxisAction | 从 `IAxisStepRepository` 按 StepKey 取完整 AxisStep；支持 Step 来源、绑定、预览、WaitForCompleted、OverrideTimeoutMs | 被错误简化为单轴绝对位置运动 |
| AxisServo | DeviceId/AxisId 可绑定；Enabled、WaitForCompleted、TimeoutMs、PollIntervalMs、ResultVarKey、Interrupt | 缺少大部分参数和结果字段 |
| AxisStop | DeviceId/AxisId、等待完成、超时、轮询、结果和可恢复中断 | 错误增加 StopMode，遗漏原参数 |
| AxisWait | InPosOff/Homed/ServoOn/AlarmOff/PositionReached/PositionPassed 六种条件，目标位置来源和容差 | 被错误简化为 WaitIdle |
| IORead | DriveId、Index、Input/Output 类型，输出 VariantValue/IONodeResult | 被错误简化为布尔 PointKey |
| IOWrite | Off/On/Toggle/Pulse/DelayOn/DelayOff/SetByValue，等待、动作延时、绑定值、可恢复中断 | 被错误简化为布尔直接写入 |
| CylinderControl | CylinderName、Extend/Retract/AllOff、WaitForTargetState、TimeoutMs、Interrupt | 参数命名和等待语义不完整 |
| VacuumControl | VacuumOn/Off/OffWithBlowOff/BlowOffPulse、BlowOffDurationMs、WaitForVacuumOk | 破真空时序被遗漏 |
| CodeReaderWaitScan | TriggerBeforeWait、AutoOpenWhenClosed、Any/Exact/Contains/StartsWith/EndsWith、ExpectedCode、ResultVarKey | 被错误简化为一次等待 |
| ProductCreate | 当前批次、ProductId 自动生成、Recipe/Reason 来源、Station/Slot、ProductCreateNodeResult | 被错误替换为通用 Operation/Payload |
| ProductFlow/Station | 各节点有独立会话、工站、槽位、原因、等待和结果契约 | 通用 `WorkflowProcessNodeModel` 无法表达原语义 |
| WaferRobot | 各动作包含机器人、手臂、站位、槽位、等待、快照和产品事务参数 | 通用 Operation/Payload 丢失参数 |
| WarningHandlerBlock | 是专用告警子画布，宿主可切入执行 | 被错误简化为 HandlerKey 字符串 |
| SignalValue/Queue | 旧版支持数据值、运算、比较和结构化结果 | 字符串化实现不完整 |

## 当前整改状态

截至当前版本，上表中的 Axis、IO、气动、CodeReader、ProductFlow、Station、WaferRobot、WarningHandlerBlock、SignalValue 和 Queue 骨架失真均已完成首轮保真重写；Process 的通用 `Operation/Payload` 路径已删除。可恢复设备中断已经接入 ExternalHold、专用告警子流程及 Retry/Jump/Stop 恢复裁决，并保留 AlarmCode、故障节点标题、执行身份和安全点上下文。现阶段剩余重点是逐节点旧 JSON 版本迁移及更多设备故障模拟测试。

## 重写硬规则

1. 每次重写前完整读取对应旧节点、基类、结果类型、枚举和辅助类。
2. 建立“旧属性 → 新属性/WorkflowInput → JSON 迁移”逐项映射，禁止遗漏。
3. 保留 NodeType、默认值、分支条件、超时含义、结果字段和 Trace 关键数据。
4. 旧版 Source/Literal/BindingKey 可优化为 `WorkflowInput<T>`，但迁移器必须兼容旧字段。
5. 设备对象改为注入式接口；接口方法必须表达旧业务能力，不能用泛化字符串 Operation 代替强类型契约。
6. 可恢复中断必须接入新版 Hold/Recovery 契约，不得简单映射成 Failed/Timeout 端口。
7. 每个节点独占一个 `*Node.cs`；共享结果、枚举、服务接口放独立 Contracts 文件。
8. 完成模型、Handler、Catalog、JSON 迁移和行为测试后才标记 Completed。

## 修正顺序

1. Motion：IO → Axis → Cylinder/Vacuum → CodeReader。
2. Standard：SignalValue → Queue → 批量 Signal → ConvertValue 动态输出。
3. Process：异常恢复子画布 → ProductFlow/Station → WaferRobot。
4. 删除临时通用 Operation/Payload 骨架及其错误文档结论。
