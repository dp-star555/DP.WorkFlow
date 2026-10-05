using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DP.WorkFlow.UI.Wpf;

/// <summary>为代码创建的 WPF 工作流窗口提供统一深色调色板和基础控件尺寸。</summary>
internal static class WorkflowWpfStyle
{
    private static readonly SolidColorBrush WindowBrush = CreateBrush(30, 30, 30);
    private static readonly SolidColorBrush SurfaceBrush = CreateBrush(37, 37, 38);
    private static readonly SolidColorBrush ControlBrush = CreateBrush(51, 51, 55);
    private static readonly SolidColorBrush HoverBrush = CreateBrush(62, 62, 66);
    private static readonly SolidColorBrush BorderBrush = CreateBrush(63, 63, 70);
    private static readonly SolidColorBrush TextBrush = CreateBrush(241, 241, 241);
    private static readonly SolidColorBrush MutedTextBrush = CreateBrush(190, 190, 190);
    private static readonly SolidColorBrush AccentBrush = CreateBrush(0, 122, 204);

    /// <summary>把窗口及其原生控件统一为工作流设计器使用的深色主题。</summary>
    internal static void Apply(Window window)
    {
        window.Background = WindowBrush;
        window.Foreground = TextBrush;
        window.FontFamily = new FontFamily("Microsoft YaHei UI");
        window.FontSize = 13;
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;

        // WPF 默认模板会读取这些系统颜色；覆盖后可避免按钮、选择项短暂显示浅色。
        window.Resources[SystemColors.ControlBrushKey] = ControlBrush;
        window.Resources[SystemColors.ControlTextBrushKey] = TextBrush;
        window.Resources[SystemColors.WindowBrushKey] = SurfaceBrush;
        window.Resources[SystemColors.WindowTextBrushKey] = TextBrush;
        window.Resources[SystemColors.HighlightBrushKey] = AccentBrush;
        window.Resources[SystemColors.HighlightTextBrushKey] = TextBrush;

        window.Resources[typeof(Button)] = CreateButtonStyle();
        window.Resources[typeof(TextBox)] = CreateInputStyle(typeof(TextBox));
        window.Resources[typeof(ComboBox)] = CreateInputStyle(typeof(ComboBox));
        window.Resources[typeof(ListBox)] = CreateListStyle();
        window.Resources[typeof(DataGrid)] = CreateDataGridStyle();
        window.Resources[typeof(DataGridColumnHeader)] = CreateColumnHeaderStyle();
        window.Resources[typeof(GroupBox)] = CreateGroupStyle();
        window.Resources[typeof(TabControl)] = CreateTabControlStyle();
        window.Resources[typeof(TabItem)] = CreateTabItemStyle();
    }

    private static Style CreateTabControlStyle()
    {
        var style = new Style(typeof(TabControl));
        style.Setters.Add(new Setter(Control.BackgroundProperty, WindowBrush));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, BorderBrush));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 1, 0, 0)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0, 4, 0, 0)));
        return style;
    }

    /// <summary>圆角标签头：未选中透明，悬停浅灰，选中使用强调色下划线与表面色背景。</summary>
    private static Style CreateTabItemStyle()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Chrome");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6, 6, 0, 0));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 2));
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.PaddingProperty, new Thickness(16, 6, 16, 6));
        border.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 2, 0));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(TabItem)) { VisualTree = border };
        template.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true,
            Setters = { new Setter(Border.BackgroundProperty, HoverBrush, "Chrome") } });
        template.Triggers.Add(new Trigger { Property = TabItem.IsSelectedProperty, Value = true,
            Setters =
            {
                new Setter(Border.BackgroundProperty, SurfaceBrush, "Chrome"),
                new Setter(Border.BorderBrushProperty, AccentBrush, "Chrome")
            } });
        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private static Style CreateButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, ControlBrush));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, BorderBrush));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 4, 12, 4)));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 72d));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 30d));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        style.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true,
            Setters = { new Setter(Control.BackgroundProperty, HoverBrush) } });
        style.Triggers.Add(new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true,
            Setters = { new Setter(Control.BackgroundProperty, AccentBrush) } });
        style.Triggers.Add(new Trigger { Property = UIElement.IsEnabledProperty, Value = false,
            Setters = { new Setter(Control.ForegroundProperty, MutedTextBrush), new Setter(UIElement.OpacityProperty, 0.65d) } });
        return style;
    }

    private static Style CreateInputStyle(Type targetType)
    {
        var style = new Style(targetType);
        style.Setters.Add(new Setter(Control.BackgroundProperty, ControlBrush));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, BorderBrush));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 3, 5, 3)));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 28d));
        return style;
    }

    private static Style CreateListStyle()
    {
        var style = new Style(typeof(ListBox));
        style.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceBrush));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, BorderBrush));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(3)));
        return style;
    }

    private static Style CreateDataGridStyle()
    {
        var style = new Style(typeof(DataGrid));
        style.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceBrush));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, BorderBrush));
        style.Setters.Add(new Setter(DataGrid.RowBackgroundProperty, ControlBrush));
        style.Setters.Add(new Setter(DataGrid.AlternatingRowBackgroundProperty, SurfaceBrush));
        style.Setters.Add(new Setter(DataGrid.GridLinesVisibilityProperty, DataGridGridLinesVisibility.Horizontal));
        style.Setters.Add(new Setter(DataGrid.HorizontalGridLinesBrushProperty, BorderBrush));
        style.Setters.Add(new Setter(DataGrid.RowHeaderWidthProperty, 0d));
        return style;
    }

    private static Style CreateColumnHeaderStyle()
    {
        var style = new Style(typeof(DataGridColumnHeader));
        style.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceBrush));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, BorderBrush));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
        return style;
    }

    private static Style CreateGroupStyle()
    {
        var style = new Style(typeof(GroupBox));
        style.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceBrush));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, BorderBrush));
        return style;
    }

    private static SolidColorBrush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
