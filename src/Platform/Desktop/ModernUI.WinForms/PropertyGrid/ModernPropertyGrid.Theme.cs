using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using ModernUI.Localization;
using ModernUI.WinForms;

namespace ModernPropertyGrid.WinForms;

public sealed partial class ModernPropertyGrid
{
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        InvalidateRowWidthCaches();
        DpiLayoutTransaction.Request();
    }

    protected override void OnDpiChangedBeforeParent(EventArgs e)
    {
        InvalidateRowWidthCaches();
        base.OnDpiChangedBeforeParent(e);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        InvalidateRowWidthCaches();
        DpiLayoutTransaction.Request();
    }

    private void ApplyFinalDpiLayout()
    {
        ApplyDpiMetrics();
        InvalidateRowWidthCaches();
        ResizeRows();
        Invalidate();
    }

    private void ApplyDpiMetrics()
    {
        _layout.RowStyles[0].Height = ShowSearchBar ? ScaleLogical(48) : 0;
        _searchBar.Padding = new Padding(ScaleLogical(8), ScaleLogical(8), ScaleLogical(6), ScaleLogical(7));
        _searchBar.ColumnStyles[1].Width = ScaleLogical(72);
        _searchBar.ColumnStyles[2].Width = ScaleLogical(36);
        _clearSearch.Margin = new Padding(ScaleLogical(6), 0, 0, 0);
        _content.Padding = new Padding(ScaleLogical(8));
        _detailsSplitter.SplitterWidth = ScaleLogical(7);
        _detailsSplitter.Panel1MinSize = ScaleLogical(64);
        _detailsSplitter.Panel2MinSize = ScaleLogical(44);
        _detailsSplitter.InitialPanel2Size = 82;
        _details.MinimumSize = new Size(0, ScaleLogical(44));
        _details.Padding = new Padding(ScaleLogical(10), ScaleLogical(8), ScaleLogical(10), ScaleLogical(8));

        foreach (Control control in _content.Controls)
        {
            if (control.Tag is CategoryBodyState state)
            {
                state.CancelAnimation();
                foreach (Control child in control.Controls)
                {
                    if (child is ModernButton groupHeader)
                    {
                        groupHeader.Height = ScaleLogical(32);
                        groupHeader.Margin = new Padding(0, ScaleLogical(2), 0, ScaleLogical(2));
                    }
                    if (child is not PropertyRowPanel row) continue;
                    var rowHeight = ScaleLogical(_theme.PropertyRowHeight);
                    row.MinimumSize = new Size(0, rowHeight);
                    row.Height = rowHeight;
                    row.Margin = new Padding(0, 0, 0, ScaleLogical(2));
                    row.Padding = new Padding(ScaleLogical(8), ScaleLogical(5), ScaleLogical(6), ScaleLogical(5));
                }
                state.ExpandedHeight = state.MeasureExpandedHeight?.Invoke() ?? control.Controls.Cast<Control>()
                    .Sum(child => child.Height + child.Margin.Vertical);
                control.Height = _collapsedCategories.Contains(state.Category) ? 0 : state.ExpandedHeight;
            }
            else if (control is ModernButton categoryHeader)
            {
                categoryHeader.Height = ScaleLogical(32);
                categoryHeader.Margin = new Padding(0, ScaleLogical(6), 0, ScaleLogical(2));
            }
        }
    }

    private void InvalidateRowWidthCaches()
    {
        _lastRowsWidth = -1;
        foreach (Control control in _content.Controls)
        {
            if (control.Tag is CategoryBodyState state) state.LastWidth = -1;
        }
    }

    private void RefreshVisualTheme()
    {
        foreach (var control in Descendants(_content))
        {
            switch (control)
            {
                case PropertyRowPanel row:
                    row.OutsideColor = _theme.Background;
                    row.NormalColor = _theme.Container;
                    row.HoverColor = _theme.ControlHover;
                    row.SelectedColor = _theme.PrimaryBackground;
                    row.Invalidate();
                    break;
                case ModernControl modern:
                    modern.Theme = _theme;
                    break;
                case Label label:
                    label.ForeColor = _theme.TextSecondary;
                    break;
                case FlowLayoutPanel flow:
                    flow.BackColor = _theme.Background;
                    break;
            }
        }
        _content.Invalidate(true);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private void ApplyTheme()
    {
        BackColor = _theme.Background;
        ForeColor = _theme.Text;
        _layout.BackColor = _theme.Background;
        _searchBar.BackColor = _theme.Background;
        _content.BackColor = _theme.Background;
        _scrollViewport.BackColor = _theme.Background;
        _scrollViewport.ThumbColor = _theme.ScrollThumb;
        _scrollViewport.ThumbHoverColor = _theme.ScrollThumbHover;
        _detailsSplitter.Theme = _theme;
        _detailsSplitter.Panel1.BackColor = _theme.Background;
        _detailsSplitter.Panel2.BackColor = _theme.Container;
        _details.BackColor = _theme.Container;
        _details.ForeColor = _theme.TextSecondary;
        _summary.ForeColor = _theme.TextSecondary;
        _search.Theme = _theme;
        _clearSearch.Theme = _theme;
    }

    private int ScaleLogical(int value) => (int)Math.Round(value * DeviceDpi / 96f);

    private void ResizeRows()
    {
        // ModernScrollView 已预留固定的覆盖式滚动条槽位，行宽不再随滚动状态变化。
        var width = Math.Max(ScaleLogical(80), _content.Width - _content.Padding.Horizontal);
        if (_lastRowsWidth == width) return;
        _lastRowsWidth = width;
        foreach (Control control in _content.Controls)
        {
            if (control.Width != width) control.Width = width;
            if (control.Tag is CategoryBodyState) ResizeCategoryRows(control);
        }
    }
}
