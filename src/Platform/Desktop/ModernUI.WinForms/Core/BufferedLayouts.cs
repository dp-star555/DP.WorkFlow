using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>减少复杂桌面布局缩放时背景擦除和子控件频闪的 TableLayoutPanel。</summary>
[ToolboxItem(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public class BufferedTableLayoutPanel : TableLayoutPanel
{
    public BufferedTableLayoutPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();
    }
}

/// <summary>减少滚动和动态行布局频闪的 FlowLayoutPanel。</summary>
[ToolboxItem(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class BufferedFlowLayoutPanel : FlowLayoutPanel
{
    public BufferedFlowLayoutPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();
    }
}

/// <summary>减少 OwnerDraw 列表逐帧更新时背景擦除造成的频闪。</summary>
[ToolboxItem(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class BufferedListBox : ListBox
{
    public BufferedListBox()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();
    }
}

/// <summary>减少卡片和工作区缩放频闪的普通容器。</summary>
[ToolboxItem(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class BufferedPanel : Panel
{
    public BufferedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();
    }
}
