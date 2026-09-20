# ADR-0007：Block 显式数据契约与确定性比较

- 状态：Accepted
- 日期：2026-01-01

## 决策

Block 使用显式 InputMappings 和 OutputMappings 在父子作用域间传值。输入支持固定值、父变量和父节点绑定；输出支持子变量和子节点绑定。映射目标重复、缺失来源或 null 值均明确失败，不再隐式共享可变 Context。

ValueCompare 和 StringCompare 使用 `WorkflowInput<T>`。数值、日期转换固定使用 InvariantCulture；字符串使用 Ordinal/OrdinalIgnoreCase。浮点容差同时定义相等与大小边界，Like 使用带超时的 NonBacktracking 正则。

## 原因

隐式父子变量共享会让子流程产生不可见副作用；依赖机器区域设置的数值和字符串比较会造成设备间结果不一致。旧版容差还可能让 Equal 与 GreaterThan 同时成立。

## 结果

Block 的输入输出成为可持久化、可审查的数据契约。比较节点在不同区域设置下保持相同行为，并通过 CompareNodeResult 暴露统一输出。
