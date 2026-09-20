# ADR-0009：共享设计会话与双原生 UI 控件

- 状态：Accepted
- 日期：2026-01-01

## 决策

WinForms 与 WPF 共享 `WorkflowDesignerSession`、画布模型、编辑命令、端口规则、Undo/Redo、视口和运行快照，不共享具体控件。WinForms 使用 GDI+ Control，WPF 使用 FrameworkElement/DrawingContext。

UI 工程可以引用 Core 和 Runtime；Core、Runtime 和节点程序集禁止反向引用任何 UI 工程。

## 原因

强行通过 WindowsFormsHost、ElementHost 或跨框架控件包装共享画布，会造成 DPI、输入、焦点、主题和部署问题。只共享行为模型可以保证两套 UI 语义一致，同时允许使用各自框架的原生渲染和扩展能力。

## 结果

所有文档修改都通过同一个设计会话进入，端口基数和 Undo/Redo 行为由共享测试固定。两套 UI 可以独立演进属性面板和视觉主题。
