using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// Workflow 页面用的表格：只向无障碍接口（UI 自动化）公开表格本身，不公开每一行每一格。
/// </summary>
/// <remarks>
/// 运行中的表格每个节拍都在变。输入法、读屏、划词翻译等外部工具会通过 UI 自动化逐格读取表格，
/// 这些查询都在界面线程上回答；行多、刷新快时界面线程被拖住，表现为卡顿甚至无响应。
/// 表格内容可以从详情面板或“复制”取。
/// </remarks>
public class WorkflowTable : ModernDataGridView
{
    /// <inheritdoc />
    protected override AccessibleObject CreateAccessibilityInstance() => new ControlAccessibleObject(this);
}
