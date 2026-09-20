using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>以现代标签头承载原生 TabPage，并保留 Designer、键盘选择和 PageTabList UIA provider。</summary>
[DefaultProperty(nameof(TabPages))]
[DefaultEvent(nameof(SelectedIndexChanged))]
[Description("ModernTabControl 现代页签控件")]
[DisplayName("现代页签")]
[ToolboxBitmap(typeof(ModernTabControl), "Toolbox.Icons.Navigation.bmp")]
[ToolboxItem(true)]
public sealed partial class ModernTabControl : UserControl
{
    private readonly HeaderStrip _header;
    private readonly Panel _viewport = new();
    private readonly HiddenHeaderTabControl _nativeTabs = new();
    private ModernTheme _theme = ModernUiSettings.DefaultTheme;
    private int _headerHorizontalPadding = 16;
    private int _headerVerticalPadding = 7;
    private TabAlignment _alignment = TabAlignment.Top;
    private bool _initializing;
    private int _lastSelectedIndex = -1;
    private int _selectionAnimationDuration = 260;
    private ImageList? _imageList;
    private readonly HashSet<Control> _inheritedPageFonts = [];
    private int _lastDeviceDpi = 96;
    private FinalLayoutTransaction? _dpiLayoutTransaction;

    private FinalLayoutTransaction DpiLayoutTransaction =>
        _dpiLayoutTransaction ??= new FinalLayoutTransaction(this, ApplyFinalDpiLayout);

    /// <summary>初始化现代标签头及负责页面、键盘和无障碍语义的原生 TabControl。</summary>
    public ModernTabControl()
    {
        _initializing = true;
        AutoScaleMode = AutoScaleMode.Inherit;
        TabStop = true;
        Size = new Size(320, 200);
        _header = new HeaderStrip(this) { TabStop = false };
        _viewport.Controls.Add(_nativeTabs);
        base.Controls.Add(_viewport);
        base.Controls.Add(_header);
        _nativeTabs.SelectedIndexChanged += (_, _) =>
        {
            var previousIndex = _lastSelectedIndex;
            _lastSelectedIndex = _nativeTabs.SelectedIndex;
            _header.AnimateIndicator(previousIndex, _lastSelectedIndex);
            _header.Invalidate();
            if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.Selection, SelectedIndex);
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        };
        _nativeTabs.ControlAdded += (_, eventArgs) =>
        {
            if (eventArgs.Control is TabPage page)
            {
                TrackInheritedPageFonts(page);
                ApplyPageTheme(page);
            }
            _header.Invalidate();
            PerformLayout();
        };
        _nativeTabs.ControlRemoved += (_, eventArgs) =>
        {
            if (eventArgs.Control is TabPage page) UntrackInheritedPageFonts(page);
            _header.Invalidate();
            PerformLayout();
        };
        _nativeTabs.Invalidated += (_, _) => _header.Invalidate();
        _initializing = false;
        ApplyTheme();
    }

    /// <summary>获取原生 TabPage 集合。</summary>
    [Category("Data")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public TabControl.TabPageCollection TabPages => _nativeTabs.TabPages;

    /// <summary>获取页签数量。</summary>
    [Browsable(false)]
    public int TabCount => _nativeTabs.TabCount;

    /// <summary>获取或设置由 TabPage.ImageIndex 或 ImageKey 引用的页签头图像列表。</summary>
    [Category("Appearance"), DefaultValue(null)]
    public ImageList? ImageList
    {
        get => _imageList;
        set
        {
            if (ReferenceEquals(_imageList, value)) return;
            if (_imageList is not null) _imageList.RecreateHandle -= OnImageListChanged;
            _imageList = value;
            _nativeTabs.ImageList = value;
            if (_imageList is not null) _imageList.RecreateHandle += OnImageListChanged;
            PerformLayout();
            _header.Invalidate();
        }
    }

    /// <summary>获取或设置当前选中页索引。</summary>
    [Category("Behavior"), DefaultValue(-1)]
    public int SelectedIndex { get => _nativeTabs.SelectedIndex; set => _nativeTabs.SelectedIndex = value; }

    /// <summary>获取或设置当前选中的页面。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TabPage? SelectedTab { get => _nativeTabs.SelectedTab; set => _nativeTabs.SelectedTab = value; }

    /// <summary>获取或设置标签头位于页面顶部或底部。</summary>
    [Category("Appearance"), DefaultValue(TabAlignment.Top)]
    public TabAlignment Alignment
    {
        get => _alignment;
        set
        {
            if (value is TabAlignment.Left or TabAlignment.Right)
                throw new NotSupportedException("ModernTabControl currently supports Top and Bottom alignment.");
            if (_alignment == value) return;
            _alignment = value;
            PerformLayout();
            _header.Invalidate();
        }
    }

    /// <summary>获取或设置页签及其页面使用的主题。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ModernTheme Theme
    {
        get => _theme;
        set { _theme = value ?? throw new ArgumentNullException(nameof(value)); ApplyTheme(); }
    }

    /// <summary>获取或设置标签文字左右两侧的逻辑像素留白。</summary>
    [Category("Layout"), DefaultValue(16)]
    public int HeaderHorizontalPadding
    {
        get => _headerHorizontalPadding;
        set { _headerHorizontalPadding = Math.Max(4, value); PerformLayout(); _header.Invalidate(); }
    }

    /// <summary>获取或设置标签文字上下两侧的逻辑像素留白。</summary>
    [Category("Layout"), DefaultValue(7)]
    public int HeaderVerticalPadding
    {
        get => _headerVerticalPadding;
        set { _headerVerticalPadding = Math.Max(2, value); PerformLayout(); _header.Invalidate(); }
    }

    /// <summary>获取或设置选中指示条滑动动画时长；设置为 0 可关闭动画。</summary>
    [Category("Behavior")]
    [DefaultValue(260)]
    public int SelectionAnimationDuration
    {
        get => _selectionAnimationDuration;
        set => _selectionAnimationDuration = Math.Max(0, value);
    }

    /// <summary>当前选中页面发生变化时发生。</summary>
    public event EventHandler? SelectedIndexChanged;

    /// <summary>按索引选择页面。</summary>
    /// <param name="index">要选择的页面索引。</param>
    public void SelectTab(int index) => _nativeTabs.SelectTab(index);

    /// <summary>选择指定页面。</summary>
    /// <param name="page">要选择的页面。</param>
    public void SelectTab(TabPage page) => _nativeTabs.SelectTab(page);

    protected override Control.ControlCollection CreateControlsInstance() => new TabControlCollectionAdapter(this);

    protected override AccessibleObject CreateAccessibilityInstance()
    {
        _nativeTabs.AccessibleName = AccessibleName;
        _nativeTabs.AccessibleDescription = AccessibleDescription;
        return _nativeTabs.AccessibilityObject;
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (_initializing || e.Control is not TabPage page) return;
        base.Controls.Remove(page);
        _nativeTabs.TabPages.Add(page);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (_header is null) return;
        _header.Font = Font;
        _nativeTabs.Font = Font;
        PerformLayout();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _lastDeviceDpi = Math.Max(1, DeviceDpi);
        DpiLayoutTransaction.Request();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        var newDpi = Math.Max(1, DeviceDpi);
        if (newDpi != _lastDeviceDpi)
        {
            var factor = newDpi / (float)Math.Max(1, _lastDeviceDpi);
            foreach (TabPage page in _nativeTabs.TabPages)
                if (!ReferenceEquals(page, _nativeTabs.SelectedTab)) page.Scale(new SizeF(factor, factor));
            _lastDeviceDpi = newDpi;
        }
        DpiLayoutTransaction.Request();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_header is null || _viewport is null || _nativeTabs is null) return;
        var headerHeight = Font.Height + ScaleLogical(HeaderVerticalPadding) * 2;
        if (Alignment == TabAlignment.Bottom)
        {
            _viewport.Bounds = new Rectangle(0, 0, Width, Math.Max(0, Height - headerHeight));
            _header.Bounds = new Rectangle(0, _viewport.Bottom, Width, headerHeight);
        }
        else
        {
            _header.Bounds = new Rectangle(0, 0, Width, headerHeight);
            _viewport.Bounds = new Rectangle(0, headerHeight, Width, Math.Max(0, Height - headerHeight));
        }

        // The native control still owns pages and UIA. Its one-pixel hidden tab strip and pane frame
        // are placed outside the clipping viewport so only the selected TabPage remains visible.
        var clip = ScaleLogical(5);
        _nativeTabs.Bounds = new Rectangle(-clip, -clip - 1,
            _viewport.Width + clip * 2, _viewport.Height + clip * 2 + 1);
    }

    private void ApplyFinalDpiLayout()
    {
        _header.Font = Font;
        _nativeTabs.Font = Font;
        foreach (TabPage page in _nativeTabs.TabPages) SynchronizeInheritedPageFonts(page);
        PerformLayout();
        Invalidate();
    }

    private void TrackInheritedPageFonts(Control root)
    {
        var fontProperty = TypeDescriptor.GetProperties(root)[nameof(Font)];
        if (fontProperty is not null && !fontProperty.ShouldSerializeValue(root))
            _inheritedPageFonts.Add(root);
        root.ControlAdded += TrackedPageControlAdded;
        foreach (Control child in root.Controls) TrackInheritedPageFonts(child);
    }

    private void UntrackInheritedPageFonts(Control root)
    {
        _inheritedPageFonts.Remove(root);
        root.ControlAdded -= TrackedPageControlAdded;
        foreach (Control child in root.Controls) UntrackInheritedPageFonts(child);
    }

    private void TrackedPageControlAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control is not null) TrackInheritedPageFonts(e.Control);
    }

    private void SynchronizeInheritedPageFonts(Control root)
    {
        if (_inheritedPageFonts.Contains(root) && root.Parent is not null &&
            !root.Font.Equals(root.Parent.Font) && IsStaleInheritedDpiFont(root.Font, root.Parent.Font))
            root.Font = root.Parent.Font;
        foreach (Control child in root.Controls) SynchronizeInheritedPageFonts(child);
    }

    private static bool IsStaleInheritedDpiFont(Font child, Font parent) =>
        string.Equals(child.FontFamily.Name, parent.FontFamily.Name, StringComparison.OrdinalIgnoreCase) &&
        child.Style == parent.Style && child.SizeInPoints < parent.SizeInPoints * .9f;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.Control) == Keys.Control && (keyData & Keys.KeyCode) == Keys.Tab && TabCount > 1)
        {
            var delta = (keyData & Keys.Shift) == Keys.Shift ? -1 : 1;
            SelectedIndex = (SelectedIndex + delta + TabCount) % TabCount;
            return true;
        }
        if (Focused && keyData is Keys.Left or Keys.Up) return MoveSelection(-1);
        if (Focused && keyData is Keys.Right or Keys.Down) return MoveSelection(1);
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private bool MoveSelection(int delta)
    {
        if (TabCount == 0) return false;
        SelectedIndex = (Math.Max(0, SelectedIndex) + delta + TabCount) % TabCount;
        return true;
    }

    private int ScaleLogical(int logicalPixels) => ModernDpi.ScaleToInt(logicalPixels, DeviceDpi);

    private void ApplyTheme()
    {
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        _viewport.BackColor = Theme.Background;
        _header.Theme = Theme;
        _nativeTabs.BackColor = Theme.Background;
        _nativeTabs.ForeColor = Theme.Text;
        NativeControlTheme.ApplyExplorer(_nativeTabs, Theme.IsDark);
        foreach (TabPage page in TabPages) ApplyPageTheme(page);
        Invalidate(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _imageList is not null) _imageList.RecreateHandle -= OnImageListChanged;
        base.Dispose(disposing);
    }

    private void OnImageListChanged(object? sender, EventArgs e)
    {
        PerformLayout();
        _header.Invalidate();
    }

    private Image? GetPageImage(TabPage page)
    {
        var images = ImageList?.Images;
        if (images is null) return null;
        if (!string.IsNullOrEmpty(page.ImageKey) && images.ContainsKey(page.ImageKey)) return images[page.ImageKey];
        return page.ImageIndex >= 0 && page.ImageIndex < images.Count ? images[page.ImageIndex] : null;
    }

    private void ApplyPageTheme(TabPage page)
    {
        page.UseVisualStyleBackColor = false;
        page.BackColor = Theme.Background;
        page.ForeColor = Theme.Text;
        foreach (Control child in page.Controls) ModernUiSettings.ApplyTheme(child, Theme);
    }

    private Rectangle GetHeaderItemBounds(int index)
    {
        if ((uint)index >= (uint)TabCount) return Rectangle.Empty;
        var x = 0;
        for (var current = 0; current < TabCount; current++)
        {
            var page = TabPages[current];
            var textWidth = TextRenderer.MeasureText(page.Text, Font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            var imageWidth = GetPageImage(page) is null ? 0 : ScaleLogical(16) + ScaleLogical(6);
            var width = Math.Max(ScaleLogical(56), textWidth + imageWidth + ScaleLogical(HeaderHorizontalPadding) * 2);
            if (current == index) return new Rectangle(x, 0, Math.Min(width, Math.Max(0, _header.Width - x)), _header.Height);
            x += width;
            if (x >= _header.Width) break;
        }
        return Rectangle.Empty;
    }

    private int HeaderIndexFromPoint(Point point)
    {
        for (var index = 0; index < TabCount; index++)
            if (GetHeaderItemBounds(index).Contains(point)) return index;
        return -1;
    }
}
