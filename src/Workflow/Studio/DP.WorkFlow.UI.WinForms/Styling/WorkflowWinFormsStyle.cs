using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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

    // 已挂接过自绘事件的原生控件，避免重复订阅；Following 记录已跟随动态子控件的容器。
    private static readonly ConditionalWeakTable<Control, object> Hooked = new();
    private static readonly ConditionalWeakTable<Control, object> Following = new();

    /// <summary>应用。</summary>
    /// <param name="root">根控件。</param>
    /// <param name="followAddedControls">是否对之后动态加入的子控件也着色；用于运行中重建界面的第三方控件。</param>
    internal static void Apply(Control root, bool followAddedControls = false)
    {
        var palette = Get();
        ApplyControl(root, palette, followAddedControls);
    }

    /// <summary>应用Control。</summary>
    private static void ApplyControl(Control control, Palette palette, bool follow = false)
    {
        if (follow && !Following.TryGetValue(control, out _))
        {
            Following.Add(control, new object());
            control.ControlAdded += (_, e) => { if (e.Control is { } child) ApplyControl(child, Get(), true); };
        }
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
                    ApplyControl(child, palette, follow);
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
                    ApplyControl(child, palette, follow);
            }
            return;
        }
        // 其余 Modern 控件（树、列表、输入框、工具栏等）自带完整主题树。
        if (control.GetType().Namespace == typeof(ModernUI.WinForms.ModernTheme).Namespace)
        {
            ModernUI.WinForms.ModernUiSettings.ApplyTheme(control, ModernUI.WinForms.ModernTheme.Dark);
            return;
        }

        // 原生属性表（SDK的ROI规则编辑等）：内部子控件由属性表自己按这些颜色绘制，不再递归改写。
        if (control is PropertyGrid nativeGrid)
        {
            nativeGrid.BackColor = palette.Window;
            nativeGrid.ForeColor = palette.Text;
            nativeGrid.ViewBackColor = palette.Control;
            nativeGrid.ViewForeColor = palette.Text;
            nativeGrid.ViewBorderColor = palette.Border;
            nativeGrid.LineColor = palette.Surface;
            nativeGrid.CategoryForeColor = palette.Text;
            nativeGrid.CategorySplitterColor = palette.Border;
            nativeGrid.HelpBackColor = palette.Surface;
            nativeGrid.HelpForeColor = palette.Text;
            nativeGrid.HelpBorderColor = palette.Border;
            nativeGrid.CommandsBackColor = palette.Surface;
            nativeGrid.CommandsForeColor = palette.Text;
            nativeGrid.DisabledItemForeColor = palette.MutedText;
            nativeGrid.SelectedItemWithFocusBackColor = Color.FromArgb(0, 120, 215);
            nativeGrid.SelectedItemWithFocusForeColor = Color.White;
            return;
        }

        bool first = !Hooked.TryGetValue(control, out _);
        if (first) Hooked.Add(control, new object());

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
                combo.FlatStyle = FlatStyle.Flat;
                if (first) DarkWindowTheme(combo, "DarkMode_CFD");
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
                // 原生按钮按 ModernButton 的样子自绘：圆角、悬停/按下高亮、禁用灰字。
                if (first) button.Paint += (_, e) => PaintButton(button, e.Graphics);
                break;
            case ListView list:
                list.BackColor = palette.Control;
                list.ForeColor = palette.Text;
                list.BorderStyle = BorderStyle.FixedSingle;
                if (first)
                {
                    DarkWindowTheme(list, "DarkMode_Explorer", headerTheme: "DarkMode_ItemsView");
                    // 详细视图表头自绘为深色；行和子项仍用系统绘制。
                    if (!list.OwnerDraw)
                    {
                        list.OwnerDraw = true;
                        list.DrawColumnHeader += (_, e) => PaintHeader(e);
                        list.DrawItem += (_, e) => e.DrawDefault = true;
                        list.DrawSubItem += (_, e) => e.DrawDefault = true;
                    }
                }
                break;
            case ListBox listBox:
                listBox.BorderStyle = BorderStyle.FixedSingle;
                if (first) DarkWindowTheme(listBox, "DarkMode_Explorer");
                break;
            case TabControl tabControl:
                // 原生标签页头自绘为深色；ModernTabControl 已在前面单独处理。
                if (first && tabControl.DrawMode == TabDrawMode.Normal)
                {
                    tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
                    tabControl.DrawItem += (_, e) => PaintTab(tabControl, e);
                }
                break;
            case CheckBox checkBox:
                checkBox.FlatStyle = FlatStyle.Flat;
                checkBox.FlatAppearance.BorderColor = palette.Border;
                checkBox.FlatAppearance.CheckedBackColor = palette.Control;
                break;
            case RadioButton radio:
                radio.FlatStyle = FlatStyle.Flat;
                radio.FlatAppearance.BorderColor = palette.Border;
                break;
            case UpDownBase upDown:
                upDown.BackColor = palette.Control;
                upDown.ForeColor = palette.Text;
                upDown.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ScrollableControl { AutoScroll: true } scrollable when first:
                DarkWindowTheme(scrollable, "DarkMode_Explorer");
                break;
            case ToolStrip strip:
                strip.BackColor = palette.Surface;
                strip.ForeColor = palette.Text;
                foreach (ToolStripItem item in strip.Items)
                    item.ForeColor = palette.Text;
                break;
        }

        foreach (Control child in control.Controls)
            ApplyControl(child, palette, follow);
    }

    private static void PaintButton(Button button, Graphics graphics)
    {
        var theme = ModernUI.WinForms.ModernTheme.Dark;
        var bounds = new Rectangle(0, 0, button.Width - 1, button.Height - 1);
        var hot = button.Enabled && button.ClientRectangle.Contains(button.PointToClient(Cursor.Position));
        var pressed = hot && (Control.MouseButtons & MouseButtons.Left) != 0;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(button.Parent?.BackColor ?? theme.Background);
        using var path = RoundedRectangle(bounds, Math.Min(6, bounds.Height / 2));
        using var fill = new SolidBrush(pressed ? theme.Border : hot ? theme.ControlHover : theme.Control);
        using var border = new Pen(button.Focused ? theme.Primary : theme.Border);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);
        TextRenderer.DrawText(graphics, button.Text, button.Font, bounds, button.Enabled ? theme.Text : theme.TextSecondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }

    private static void PaintHeader(DrawListViewColumnHeaderEventArgs e)
    {
        var theme = ModernUI.WinForms.ModernTheme.Dark;
        using var fill = new SolidBrush(theme.Container);
        using var line = new Pen(theme.Border);
        e.Graphics.FillRectangle(fill, e.Bounds);
        e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        e.Graphics.DrawLine(line, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 5);
        var text = Rectangle.Inflate(e.Bounds, -6, 0);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text, e.Font, text, theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }

    private static void PaintTab(TabControl tabs, DrawItemEventArgs e)
    {
        var theme = ModernUI.WinForms.ModernTheme.Dark;
        var selected = e.Index == tabs.SelectedIndex;
        using var fill = new SolidBrush(selected ? theme.Control : theme.Container);
        e.Graphics.FillRectangle(fill, e.Bounds);
        if (selected)
        {
            using var accent = new SolidBrush(theme.Primary);
            e.Graphics.FillRectangle(accent, e.Bounds.Left, e.Bounds.Bottom - 2, e.Bounds.Width, 2);
        }
        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds, selected ? theme.Text : theme.TextSecondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0) { path.AddRectangle(bounds); return path; }
        int d = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // Windows 10 1809+ 的系统深色主题：用于原生滚动条、下拉列表和列表表头背景；旧系统或调用失败时保持原样。
    private static void DarkWindowTheme(Control control, string theme, string? headerTheme = null)
    {
        void Apply()
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
            try
            {
                _ = SetWindowTheme(control.Handle, theme, null);
                if (headerTheme is not null && SendMessage(control.Handle, 0x101F /* LVM_GETHEADER */, IntPtr.Zero, IntPtr.Zero) is var header && header != IntPtr.Zero)
                    _ = SetWindowTheme(header, headerTheme, null);
            }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { }
        }
        if (control.IsHandleCreated) Apply();
        control.HandleCreated += (_, _) => Apply();
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

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
