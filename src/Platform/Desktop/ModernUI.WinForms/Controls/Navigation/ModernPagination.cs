using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>用于服务端或显式分页数据源的现代分页控件。</summary>
[Description("ModernPagination 现代分页控件")]
[DefaultEvent(nameof(PageChanged))]
[DisplayName("现代分页器")]
[ToolboxBitmap(typeof(ModernPagination), "Toolbox.Icons.Navigation.bmp")]
[ToolboxItem(true)]
public sealed class ModernPagination : ModernControl
{
    private readonly ModernButton _first = new() { Text = "", Icon = ModernIconKind.DoubleChevronLeft };
    private readonly ModernButton _previous = new() { Text = "", Icon = ModernIconKind.ChevronLeft };
    private readonly ModernButton _next = new() { Text = "", Icon = ModernIconKind.ChevronRight };
    private readonly ModernButton _last = new() { Text = "", Icon = ModernIconKind.DoubleChevronRight };
    private readonly Label _caption = new() { TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true };
    private int _pageIndex = 1;
    private int _pageSize = 20;
    private long _totalCount;

    public ModernPagination()
    {
        AccessibleRole = AccessibleRole.Grouping;
        TabStop = false;
        Size = new Size(360, 34);
        Controls.AddRange([_first, _previous, _caption, _next, _last]);
        _first.Click += (_, _) => MoveFirst();
        _previous.Click += (_, _) => MovePrevious();
        _next.Click += (_, _) => MoveNext();
        _last.Click += (_, _) => MoveLast();
        UpdateLocalizedText();
        UpdateState();
    }

    [Category("Data"), DefaultValue(1)]
    public int PageIndex { get => _pageIndex; set => SetPage(value); }

    [Category("Data"), DefaultValue(20)]
    public int PageSize
    {
        get => _pageSize;
        set { var next = Math.Max(1, value); if (_pageSize == next) return; _pageSize = next; SetPage(_pageIndex); UpdateState(); }
    }

    [Category("Data"), DefaultValue(typeof(long), "0")]
    public long TotalCount
    {
        get => _totalCount;
        set { var next = Math.Max(0, value); if (_totalCount == next) return; _totalCount = next; SetPage(_pageIndex); UpdateState(); }
    }

    [Browsable(false)]
    public int PageCount => Math.Max(1, (int)Math.Min(int.MaxValue, (TotalCount + PageSize - 1) / PageSize));

    [Category("Appearance"), DefaultValue(true)]
    public bool ShowTotal { get; set; } = true;

    public event EventHandler? PageChanged;

    public void MoveFirst() => SetPage(1);
    public void MovePrevious() => SetPage(PageIndex - 1);
    public void MoveNext() => SetPage(PageIndex + 1);
    public void MoveLast() => SetPage(PageCount);

    protected override void OnThemeChanged()
    {
        foreach (var button in new[] { _first, _previous, _next, _last }) button.Theme = Theme;
        _caption.BackColor = Color.Transparent;
        _caption.ForeColor = Theme.TextSecondary;
    }

    protected override void OnLocalizationChanged()
    {
        base.OnLocalizationChanged();
        UpdateLocalizedText();
        UpdateState();
        PerformLayout();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var buttonWidth = ScaleLogical(42);
        var gap = ScaleLogical(4);
        var captionWidth = Math.Max(ScaleLogical(100), ClientSize.Width - buttonWidth * 4 - gap * 4);
        var x = 0;
        var controls = RightToLeft == RightToLeft.Yes
            ? new Control[] { _last, _next, _caption, _previous, _first }
            : new Control[] { _first, _previous, _caption, _next, _last };
        foreach (var control in controls)
        {
            var width = ReferenceEquals(control, _caption) ? captionWidth : buttonWidth;
            control.Bounds = new Rectangle(x, 0, width, ClientSize.Height);
            x += width + gap;
        }
    }

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds) { }

    private void SetPage(int value)
    {
        var next = ModernCompatibility.Clamp(value, 1, PageCount);
        if (_pageIndex == next) { UpdateState(); return; }
        _pageIndex = next;
        UpdateState();
        PageChanged?.Invoke(this, EventArgs.Empty);
        AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
    }

    private void UpdateLocalizedText()
    {
        if (_first is null) return;
        AccessibleName = FrameworkText(ModernUiTextKeys.Pagination);
        var rtl = RightToLeft == RightToLeft.Yes;
        _first.Icon = rtl ? ModernIconKind.DoubleChevronRight : ModernIconKind.DoubleChevronLeft;
        _previous.Icon = rtl ? ModernIconKind.ChevronRight : ModernIconKind.ChevronLeft;
        _next.Icon = rtl ? ModernIconKind.ChevronLeft : ModernIconKind.ChevronRight;
        _last.Icon = rtl ? ModernIconKind.DoubleChevronLeft : ModernIconKind.DoubleChevronRight;
        _first.AccessibleName = FrameworkText(ModernUiTextKeys.FirstPage);
        _previous.AccessibleName = FrameworkText(ModernUiTextKeys.PreviousPage);
        _next.AccessibleName = FrameworkText(ModernUiTextKeys.NextPage);
        _last.AccessibleName = FrameworkText(ModernUiTextKeys.LastPage);
    }

    private void UpdateState()
    {
        _first.Enabled = _previous.Enabled = PageIndex > 1;
        _next.Enabled = _last.Enabled = PageIndex < PageCount;
        var arguments = new Dictionary<string, object?>
        {
            ["page"] = PageIndex,
            ["pages"] = PageCount,
            ["total"] = TotalCount
        };
        _caption.Text = FrameworkText(ShowTotal
            ? ModernUiTextKeys.PaginationSummaryWithTotal
            : ModernUiTextKeys.PaginationSummary, arguments);
        AccessibleDescription = _caption.Text;
    }
}
