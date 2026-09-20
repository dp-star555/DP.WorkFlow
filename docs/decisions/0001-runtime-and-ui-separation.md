# ADR-0001：运行内核与 UI 分离

- 状态：Accepted
- 日期：2026-01-01

## 决策

DP.WorkFlow Core、Runtime 和节点插件不得引用 WinForms 或 WPF。两个 UI 项目共享画布文档、图算法、编辑命令及运行快照，但分别实现自身的渲染和输入适配。

## 原因

旧版 Base 开启 WinForms，Motion 节点也直接引用 WinForms 编辑器，导致运行内核无法独立测试和复用。

## 结果

节点通过 UI 无关元数据描述可编辑属性；具体 PropertyGrid、DataTemplate 和控件行为由对应 UI 项目实现。
