using System.ComponentModel;

namespace ModernUI.WinForms;

// 单元格文本被省略号截断时，悬停该单元格以气泡显示完整内容。不用原生 ShowItemToolTips：
// 原生信息提示弹出时只重绘行的一部分，自绘列表的其余单元格会被擦掉。
public sealed partial class ModernListView
{
    private const int CellToolTipDelay = 500;
    private ModernToolTip? _cellToolTip;
    private System.Windows.Forms.Timer? _cellToolTipTimer;
    private (int Item, int Column) _cellToolTipCell = (-1, -1);
    private string _cellToolTipText = string.Empty;
    private Rectangle _cellToolTipBounds;
    private bool _showTruncatedCellToolTips = true;

    /// <summary>获取或设置单元格文本被截断时，是否在悬停该单元格时以气泡显示完整内容（Details 视图）。</summary>
    [Category("Behavior")]
    [DefaultValue(true)]
    [Description("单元格文本被省略号截断时，悬停该单元格以气泡显示完整内容。")]
    public bool ShowTruncatedCellToolTips
    {
        get => _showTruncatedCellToolTips;
        set
        {
            if (_showTruncatedCellToolTips == value) return;
            _showTruncatedCellToolTips = value;
            if (!value) ResetCellToolTip();
        }
    }

    private void UpdateCellToolTip(Point location)
    {
        var cell = (-1, -1);
        ListViewItem? item = null;
        ListViewItem.ListViewSubItem? subItem = null;
        if (_showTruncatedCellToolTips && !ShowItemToolTips && View == View.Details && Enabled)
        {
            var hit = HitTest(location);
            item = hit.Item; subItem = hit.SubItem;
            if (item is not null && subItem is not null) cell = (item.Index, item.SubItems.IndexOf(subItem));
        }
        if (cell == _cellToolTipCell) return;
        HideCellToolTip();
        _cellToolTipCell = cell;
        if (item is null || subItem is null || !TryGetTruncatedCell(item, subItem, cell.Item2, out var bounds)) return;
        _cellToolTipText = subItem.Text;
        _cellToolTipBounds = bounds;
        if (_cellToolTipTimer is null)
        {
            _cellToolTipTimer = new System.Windows.Forms.Timer { Interval = CellToolTipDelay };
            _cellToolTipTimer.Tick += (_, _) => ShowPendingCellToolTip();
        }
        _cellToolTipTimer.Start();
    }

    // 与 OnDrawSubItem 相同的文本区域：左右留白，首列再扣除复选框和图标。
    private bool TryGetTruncatedCell(ListViewItem item, ListViewItem.ListViewSubItem subItem, int column, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (column < 0 || column >= Columns.Count || string.IsNullOrEmpty(subItem.Text)) return false;
        var cell = subItem.Bounds;
        // 首个子项的 Bounds 是整行，列宽取首列宽度。
        if (column == 0) cell = new Rectangle(cell.Left, cell.Top, Columns[0].Width, cell.Height);
        var available = cell.Width - 2 * ScaleLogical(HorizontalPadding);
        if (column == 0)
        {
            if (CheckBoxes) available -= ScaleLogical(18) + ScaleLogical(7);
            if (SmallImageList is not null && !ReferenceEquals(SmallImageList, _rowHeightImages)
                && item.ImageIndex >= 0 && item.ImageIndex < SmallImageList.Images.Count)
                available -= SmallImageList.ImageSize.Width + ScaleLogical(6);
        }
        var width = TextRenderer.MeasureText(subItem.Text, Font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
        if (width <= Math.Max(0, available)) return false;
        bounds = Rectangle.Intersect(cell, ClientRectangle);
        return !bounds.IsEmpty;
    }

    private void ShowPendingCellToolTip()
    {
        _cellToolTipTimer?.Stop();
        if (IsDisposed || !IsHandleCreated || !Visible || _cellToolTipCell.Item < 0 || _cellToolTipText.Length == 0) return;
        _cellToolTip ??= new ModernToolTip { MaximumWidth = 480, AutoPopDelay = 15000 };
        _cellToolTip.BubbleBackColor = Theme.OverlaySurface;
        _cellToolTip.BubbleForeColor = Theme.ToolTipText;
        _cellToolTip.Show(this, _cellToolTipBounds, _cellToolTipText);
    }

    private void HideCellToolTip()
    {
        _cellToolTipTimer?.Stop();
        _cellToolTip?.Hide();
        _cellToolTipText = string.Empty;
    }

    // 滚动、点击、离开或内容变化后重新判断当前单元格。
    private void ResetCellToolTip()
    {
        HideCellToolTip();
        _cellToolTipCell = (-1, -1);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        ResetCellToolTip();
        base.OnMouseDown(e);
    }

    private void DisposeCellToolTip()
    {
        _cellToolTipTimer?.Dispose();
        _cellToolTipTimer = null;
        _cellToolTip?.Dispose();
        _cellToolTip = null;
    }
}
