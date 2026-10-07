namespace DP.WorkFlow.UI.WinForms;

/// <summary>供扩展页面使用的工作台配色入口：把嵌入的第三方/原生控件统一成工作台深色主题。</summary>
public static class WorkflowWinFormsTheme
{
    /// <summary>
    /// 为原生控件着色（按钮自绘圆角、列表表头/标签页头深色、下拉框/复选框平面化、Windows 10 1809+ 深色滚动条）；
    /// ModernUI 控件只设置主题。<paramref name="followAddedControls"/> 为真时，之后动态加入的子控件也会着色。
    /// </summary>
    /// <param name="root">根控件。</param>
    /// <param name="followAddedControls">是否跟随之后动态加入的子控件。</param>
    public static void ApplyDark(Control root, bool followAddedControls = false)
    {
        ArgumentNullException.ThrowIfNull(root);
        WorkflowWinFormsStyle.Apply(root, followAddedControls);
    }
}
