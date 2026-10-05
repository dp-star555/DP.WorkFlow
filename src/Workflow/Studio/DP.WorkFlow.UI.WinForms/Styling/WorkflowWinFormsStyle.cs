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

    internal static Palette Get() => new(
        Color.FromArgb(30, 30, 30),
        Color.FromArgb(37, 37, 38),
        Color.FromArgb(51, 51, 55),
        Color.FromArgb(63, 63, 70),
        Color.FromArgb(241, 241, 241),
        Color.FromArgb(200, 200, 200),
        Color.FromArgb(55, 148, 255),
        Color.FromArgb(0, 122, 204),
        Color.White,
        Color.FromArgb(67, 67, 70));

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
        if (control is ModernUI.WinForms.ModernControl or ModernUI.WinForms.ModernMenuStrip or
            ModernUI.WinForms.ModernToolStrip or ModernUI.WinForms.ModernGroupBox or
            ModernUI.WinForms.ModernListBox or ModernUI.WinForms.ModernStatusBar or
            ModernUI.WinForms.ModernDataGridView)
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
