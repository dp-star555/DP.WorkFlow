namespace DP.WorkFlow.UI.WinForms;

/// <summary>提供当前固定的 WinForms 控件配色和原生控件样式。</summary>
internal static class WorkflowWinFormsStyle
{
    /// <summary>保存 WinForms 工作流界面的颜色调色板。</summary>
    internal readonly record struct Palette(
        Color Window,
        Color Surface,
        Color Control,
        Color Border,
        Color Text,
        Color MutedText,
        Color Accent,
        Color Selection,
        Color SelectionText,
        Color Grid);

    /// <summary>调色板取自 ModernUI 深色主题，使原生宿主控件与 Modern 控件颜色一致。</summary>
    internal static Palette Get()
    {
        var theme = ModernUI.WinForms.ModernTheme.Dark;
        return new(theme.Background, theme.Container, theme.Control, theme.Border, theme.Text, theme.TextSecondary,
            theme.Primary, theme.Primary, Color.White, theme.BorderSecondary);
    }

    /// <summary>应用。</summary>
    internal static void Apply(Control root)
    {
        var palette = Get();
        ApplyControl(root, palette);
    }

    /// <summary>应用Control。</summary>
    private static void ApplyControl(Control control, Palette palette)
    {
        // 自绘现代控件拥有自己的完整主题树，外层宿主不能再递归改写其内部原生控件。
        // 特别是 ModernPropertyGrid 的圆角行背景依赖 Background/Container 精确一致。
        if (control is ModernPropertyGrid.WinForms.ModernPropertyGrid propertyGrid)
        {
            propertyGrid.Theme = ModernUI.WinForms.ModernTheme.Dark;
            return;
        }
        // 标签页外壳自绘，只设置主题；各页内容仍按普通宿主控件继续着色。
        if (control is ModernUI.WinForms.ModernTabControl tabs)
        {
            tabs.Theme = ModernUI.WinForms.ModernTheme.Dark;
            foreach (TabPage page in tabs.TabPages)
                foreach (Control child in page.Controls)
                    ApplyControl(child, palette);
            return;
        }
        // 分栏只是宿主容器：分隔条使用 Modern 主题，两侧内容继续按宿主规则着色。
        if (control is ModernUI.WinForms.ModernSplitter splitter)
        {
            splitter.Theme = ModernUI.WinForms.ModernTheme.Dark;
            foreach (var panel in new[] { splitter.Panel1, splitter.Panel2 })
            {
                panel.BackColor = palette.Window;
                foreach (Control child in panel.Controls)
                    ApplyControl(child, palette);
            }
            return;
        }
        // 其余 Modern 控件（树、列表、输入框、工具栏等）自带完整主题树。
        if (control.GetType().Namespace == typeof(ModernUI.WinForms.ModernTheme).Namespace)
        {
            ModernUI.WinForms.ModernUiSettings.ApplyTheme(control, ModernUI.WinForms.ModernTheme.Dark);
            return;
        }

        if (control is not WorkflowDesignerControl)
        {
            control.ForeColor = palette.Text;
            control.BackColor = control switch
            {
                TextBoxBase or ComboBox or ListBox or TreeView or DataGridView => palette.Control,
                TabPage or GroupBox => palette.Surface,
                _ => palette.Window
            };
        }

        switch (control)
        {
            case TreeView tree:
                tree.BackColor = palette.Surface;
                tree.ForeColor = palette.Text;
                tree.LineColor = palette.Border;
                ApplyTreeNodes(tree.Nodes, palette);
                break;
            case DataGridView grid:
                ApplyGrid(grid, palette);
                break;
            case TextBoxBase textBox:
                textBox.BackColor = palette.Control;
                textBox.ForeColor = palette.Text;
                break;
            case ComboBox combo:
                combo.BackColor = palette.Control;
                combo.ForeColor = palette.Text;
                break;
            case Button button:
                button.BackColor = palette.Control;
                button.ForeColor = palette.Text;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = palette.Border;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(
                    palette.Control.R == 255 ? 229 : 62,
                    palette.Control.R == 255 ? 241 : 62,
                    palette.Control.R == 255 ? 251 : 66);
                break;
            case ToolStrip strip:
                strip.BackColor = palette.Surface;
                strip.ForeColor = palette.Text;
                foreach (ToolStripItem item in strip.Items)
                    item.ForeColor = palette.Text;
                break;
        }

        foreach (Control child in control.Controls)
            ApplyControl(child, palette);
    }

    /// <summary>应用Tree Nodes。</summary>
    private static void ApplyTreeNodes(TreeNodeCollection nodes, Palette palette)
    {
        foreach (TreeNode node in nodes)
        {
            node.ForeColor = node.Tag is null ? palette.Accent : palette.Text;
            ApplyTreeNodes(node.Nodes, palette);
        }
    }

    /// <summary>应用Grid。</summary>
    private static void ApplyGrid(DataGridView grid, Palette palette)
    {
        grid.BackgroundColor = palette.Surface;
        grid.GridColor = palette.Grid;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.DefaultCellStyle.BackColor = palette.Control;
        grid.DefaultCellStyle.ForeColor = palette.Text;
        grid.DefaultCellStyle.SelectionBackColor = palette.Selection;
        grid.DefaultCellStyle.SelectionForeColor = palette.SelectionText;
        grid.DefaultCellStyle.NullValue = string.Empty;
        grid.AlternatingRowsDefaultCellStyle.BackColor = palette.Surface;
        grid.AlternatingRowsDefaultCellStyle.ForeColor = palette.Text;
        grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = palette.Selection;
        grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = palette.SelectionText;
        grid.ColumnHeadersDefaultCellStyle.BackColor = palette.Surface;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.Text;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.Surface;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.Text;
        grid.EnableHeadersVisualStyles = false;
        grid.RowsDefaultCellStyle.BackColor = palette.Control;
        grid.RowsDefaultCellStyle.ForeColor = palette.Text;
        grid.RowsDefaultCellStyle.SelectionBackColor = palette.Selection;
        grid.RowsDefaultCellStyle.SelectionForeColor = palette.SelectionText;
    }
}
