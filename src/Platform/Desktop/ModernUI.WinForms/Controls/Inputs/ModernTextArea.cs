using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace ModernUI.WinForms;

/// <summary>使用原生多行 TextBox 提供输入法、剪贴板、选择和 UIA 文本语义的现代文本区域。</summary>
[DefaultProperty(nameof(Text))]
[DefaultEvent(nameof(TextChanged))]
[Description("ModernTextArea 现代多行文本输入框")]
[DisplayName("现代多行文本框")]
[ToolboxBitmap(typeof(ModernTextArea), "Toolbox.Icons.Inputs.bmp")]
[ToolboxItem(true)]
public sealed class ModernTextArea : ModernInput
{
    private readonly ModernNativeScrollBarOverlay _verticalScrollBar;
    private readonly ModernNativeScrollBarOverlay _horizontalScrollBar;
    private readonly Panel _scrollCorner;
    private ScrollBars _scrollBars;
    private int _appliedHorizontalPosition;
    private Rectangle _appliedTextViewport;

    /// <summary>初始化多行文本区域的默认尺寸和编辑行为。</summary>
    public ModernTextArea()
    {
        Height = 96;
        MinimumSize = new Size(80, 60);
        Padding = new Padding(9, 8, 9, 8);
        InnerTextBox.AutoSize = false;
        InnerTextBox.Multiline = true;
        InnerTextBox.AcceptsReturn = true;
        InnerTextBox.AcceptsTab = false;
        InnerTextBox.WordWrap = true;
        InnerTextBox.ScrollBars = ScrollBars.None;
        _verticalScrollBar = new ModernNativeScrollBarOverlay(
            new ManagedControlScrollAdapter(InnerTextBox, GetNativeVerticalPosition,
                GetVerticalMaximumPosition, GetVerticalPageSize,
                position => ScrollNativeEditorTo(position, true)), vertical: true);
        _horizontalScrollBar = new ModernNativeScrollBarOverlay(
            new ManagedControlScrollAdapter(InnerTextBox, () => _appliedHorizontalPosition,
                GetHorizontalMaximumPosition, GetHorizontalPageSize,
                position => ScrollNativeEditorTo(position, false)), vertical: false);
        _scrollCorner = new Panel { TabStop = false, Visible = false };
        Controls.Add(_verticalScrollBar);
        Controls.Add(_horizontalScrollBar);
        Controls.Add(_scrollCorner);
        InnerTextBox.TextChanged += (_, _) =>
        {
            _appliedHorizontalPosition = 0;
            ScrollNativeEditorTo(0, false);
            ScrollNativeEditorTo(0, true);
            RecomputeScrollBars();
            QueueScrollBarRefresh();
        };
        InnerMouseWheelHandler = HandleInnerMouseWheel;
        InnerTextBox.KeyUp += (_, _) => QueueScrollBarRefresh();
        InnerTextBox.HandleCreated += (_, _) =>
        {
            _appliedTextViewport = Rectangle.Empty;
            BeginInvoke(UpdateTextFormattingRectangle);
            QueueScrollBarRefresh();
        };
        ApplyScrollBarTheme();
        PerformLayout();
    }

    /// <summary>获取或设置按 Enter 键时是否在默认按钮存在的窗体中插入换行。</summary>
    [Category("Behavior"), DefaultValue(true)]
    [Description("按 Enter 键时是否插入换行。")]
    public bool AcceptsReturn
    {
        get => InnerTextBox.AcceptsReturn;
        set => InnerTextBox.AcceptsReturn = value;
    }

    /// <summary>获取或设置按 Tab 键时是否插入制表符，而不是移动键盘焦点。</summary>
    [Category("Behavior"), DefaultValue(false)]
    [Description("按 Tab 键时是否插入制表符。")]
    public bool AcceptsTab
    {
        get => InnerTextBox.AcceptsTab;
        set => InnerTextBox.AcceptsTab = value;
    }

    /// <summary>获取或设置超出编辑区域宽度的文本是否自动换行。</summary>
    [Category("Behavior"), DefaultValue(true)]
    [Description("文本是否在编辑区域右边缘自动换行。")]
    public bool WordWrap
    {
        get => InnerTextBox.WordWrap;
        set
        {
            if (InnerTextBox.WordWrap == value) return;
            InnerTextBox.WordWrap = value;
            _appliedHorizontalPosition = 0;
            ScrollNativeEditorTo(0, false);
            _appliedTextViewport = Rectangle.Empty;
            LayoutScrollBars();
            UpdateTextFormattingRectangle();
            QueueScrollBarRefresh();
        }
    }

    /// <summary>获取或设置文本区域显示的现代胶囊滚动条。</summary>
    [Category("Appearance"), DefaultValue(ScrollBars.None)]
    [Description("文本区域显示的水平和垂直胶囊滚动条；滚动行为仍由原生编辑器提供。")]
    public ScrollBars ScrollBars
    {
        get => _scrollBars;
        set
        {
            if (_scrollBars == value) return;
            _scrollBars = value;
            // The native bars live in the editor's non-client area and can reappear between
            // WM_VSCROLL frames. Keep them disabled; this property configures allowed modern axes.
            InnerTextBox.ScrollBars = ScrollBars.None;
            RecomputeScrollBars();
            QueueScrollBarRefresh();
        }
    }

    /// <summary>获取当前现代滚动条的横向和纵向位置。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Point ScrollPosition => new(_horizontalScrollBar.Position, _verticalScrollBar.Position);

    /// <summary>获取扣除横纵滑块后用于排版文本的客户区。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Rectangle TextViewportBounds
    {
        get
        {
            var vertical = _verticalScrollBar.Visible;
            var horizontal = _horizontalScrollBar.Visible;
            return new Rectangle(0, 0,
                Math.Max(1, InnerTextBox.ClientSize.Width -
                    (vertical ? SystemInformation.VerticalScrollBarWidth : 0)),
                Math.Max(1, InnerTextBox.ClientSize.Height -
                    (horizontal ? SystemInformation.HorizontalScrollBarHeight : 0)));
        }
    }

    /// <summary>获取或设置文本区域中的各行文本。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [AllowNull]
    public string[] Lines
    {
        get => InnerTextBox.Lines;
        set => InnerTextBox.Lines = value ?? [];
    }

    /// <summary>滚动文本区域，使插入点进入可见范围。</summary>
    public void ScrollToCaret()
    {
        InnerTextBox.ScrollToCaret();
        QueueScrollBarRefresh();
    }

    protected override void OnThemeChanged()
    {
        base.OnThemeChanged();
        ApplyScrollBarTheme();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        LayoutScrollBars();
        UpdateTextFormattingRectangle();
    }

    private void ApplyScrollBarTheme()
    {
        if (_verticalScrollBar is null || _horizontalScrollBar is null) return;
        _verticalScrollBar.Theme = Theme;
        _horizontalScrollBar.Theme = Theme;
        _scrollCorner.BackColor = Theme.Control;
    }

    private void LayoutScrollBars()
    {
        if (_verticalScrollBar is null || _horizontalScrollBar is null) return;
        RecomputeScrollBarVisibility();
        var vertical = _verticalScrollBar.Visible;
        var horizontal = _horizontalScrollBar.Visible;
        var verticalWidth = SystemInformation.VerticalScrollBarWidth;
        var horizontalHeight = SystemInformation.HorizontalScrollBarHeight;
        _scrollCorner.Visible = vertical && horizontal;
        if (vertical)
        {
            _verticalScrollBar.Bounds = new Rectangle(InnerTextBox.Right - verticalWidth, InnerTextBox.Top,
                verticalWidth, Math.Max(0, InnerTextBox.Height - (horizontal ? horizontalHeight : 0)));
            _verticalScrollBar.BringToFront();
        }
        if (horizontal)
        {
            _horizontalScrollBar.Bounds = new Rectangle(InnerTextBox.Left, InnerTextBox.Bottom - horizontalHeight,
                Math.Max(0, InnerTextBox.Width - (vertical ? verticalWidth : 0)), horizontalHeight);
            _horizontalScrollBar.BringToFront();
        }
        if (vertical && horizontal)
        {
            _scrollCorner.Bounds = new Rectangle(InnerTextBox.Right - verticalWidth,
                InnerTextBox.Bottom - horizontalHeight, verticalWidth, horizontalHeight);
            _scrollCorner.BringToFront();
        }
    }

    private void UpdateTextFormattingRectangle()
    {
        if (!InnerTextBox.IsHandleCreated) return;
        var viewport = TextViewportBounds;
        if (_appliedTextViewport == viewport) return;
        _appliedTextViewport = viewport;
        var rectangle = new NativeRect
        {
            Left = viewport.Left,
            Top = viewport.Top,
            Right = viewport.Right,
            Bottom = viewport.Bottom
        };
        SendMessage(InnerTextBox.Handle, EditSetFormattingRectangle, IntPtr.Zero, ref rectangle);
        InnerTextBox.Invalidate();
    }

    internal bool CanConsumeWheel(int delta)
    {
        var horizontalRequested = (ModifierKeys & Keys.Shift) == Keys.Shift;
        if (horizontalRequested) return _horizontalScrollBar.CanScroll(delta);
        return HasVerticalOverflow
            ? _verticalScrollBar.CanScroll(delta)
            : _horizontalScrollBar.CanScroll(delta);
    }

    private bool HandleInnerMouseWheel(int delta)
    {
        var horizontalRequested = (ModifierKeys & Keys.Shift) == Keys.Shift;
        var handled = horizontalRequested || !HasVerticalOverflow
            ? _horizontalScrollBar.TryScrollWheel(delta)
            : _verticalScrollBar.TryScrollWheel(delta);
        if (handled) QueueScrollBarRefresh();
        return handled;
    }

    private bool HasVerticalOverflow => GetVerticalMaximumPosition() > 0;

    private int GetVerticalPageSize()
    {
        var height = InnerTextBox.ClientSize.Height -
            (_horizontalScrollBar.Visible ? SystemInformation.HorizontalScrollBarHeight : 0);
        return Math.Max(1, height / Math.Max(1, Font.Height));
    }

    private int GetNativeVerticalPosition() => InnerTextBox.IsHandleCreated
        ? Math.Max(0, SendMessage(InnerTextBox.Handle, EditGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32())
        : 0;

    private int GetVerticalMaximumPosition()
    {
        var visualLines = InnerTextBox.IsHandleCreated
            ? Math.Max(1, SendMessage(InnerTextBox.Handle, EditGetLineCount, IntPtr.Zero, IntPtr.Zero).ToInt32())
            : GetVisualLineCount(Math.Max(1, TextViewportBounds.Width));
        return Math.Max(0, visualLines - GetVerticalPageSize());
    }

    private int GetHorizontalPageSize()
    {
        var characterWidth = Math.Max(1, TextRenderer.MeasureText("M", Font,
            Size.Empty, TextFormatFlags.NoPadding).Width);
        var width = InnerTextBox.ClientSize.Width -
            (_verticalScrollBar.Visible ? SystemInformation.VerticalScrollBarWidth : 0);
        return Math.Max(1, width / characterWidth);
    }

    private int GetHorizontalMaximumPosition()
    {
        var longestLine = Lines.Length == 0 ? 0 : Lines.Max(line => line?.Length ?? 0);
        return Math.Max(0, longestLine - GetHorizontalPageSize());
    }

    private void ScrollNativeEditorTo(int position, bool vertical)
    {
        if (!InnerTextBox.IsHandleCreated) return;
        var current = vertical
            ? SendMessage(InnerTextBox.Handle, EditGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32()
            : _appliedHorizontalPosition;
        var delta = position - current;
        if (delta == 0) return;
        SendMessage(InnerTextBox.Handle, EditLineScroll,
            vertical ? IntPtr.Zero : (IntPtr)delta,
            vertical ? (IntPtr)delta : IntPtr.Zero);
        if (!vertical) _appliedHorizontalPosition = position;
    }

    private void RecomputeScrollBars()
    {
        LayoutScrollBars();
        _appliedTextViewport = Rectangle.Empty;
        UpdateTextFormattingRectangle();
    }

    private void RecomputeScrollBarVisibility()
    {
        var verticalAllowed = ScrollBars is ScrollBars.Vertical or ScrollBars.Both;
        var horizontalAllowed = !WordWrap && ScrollBars is ScrollBars.Horizontal or ScrollBars.Both;
        var vertical = false;
        var horizontal = false;
        var verticalWidth = SystemInformation.VerticalScrollBarWidth;
        var horizontalHeight = SystemInformation.HorizontalScrollBarHeight;
        for (var pass = 0; pass < 3; pass++)
        {
            var width = Math.Max(1, InnerTextBox.ClientSize.Width - (vertical ? verticalWidth : 0));
            var height = Math.Max(1, InnerTextBox.ClientSize.Height - (horizontal ? horizontalHeight : 0));
            horizontal = horizontalAllowed && GetLongestLinePixelWidth() > width;
            vertical = verticalAllowed && GetVisualLineCount(width) * Math.Max(1, Font.Height) > height;
        }
        _verticalScrollBar.Visible = vertical;
        _horizontalScrollBar.Visible = horizontal;
    }

    private int GetLongestLinePixelWidth()
    {
        if (Lines.Length == 0) return 0;
        return Lines.Max(line => TextRenderer.MeasureText(line ?? string.Empty, Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width);
    }

    private int GetVisualLineCount(int width)
    {
        if (!WordWrap) return Math.Max(1, Lines.Length);
        var count = 0;
        foreach (var line in Lines.DefaultIfEmpty(string.Empty))
        {
            var measured = TextRenderer.MeasureText(string.IsNullOrEmpty(line) ? " " : line, Font,
                new Size(Math.Max(1, width), int.MaxValue), TextFormatFlags.NoPadding |
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            count += Math.Max(1, (int)Math.Ceiling(measured.Height / (double)Math.Max(1, Font.Height)));
        }
        return Math.Max(1, count);
    }

    private void QueueScrollBarRefresh()
    {
        if (!IsHandleCreated || IsDisposed) return;
        BeginInvoke(() =>
        {
            if (IsDisposed) return;
            RecomputeScrollBars();
            _verticalScrollBar.RefreshFromTarget();
            _horizontalScrollBar.RefreshFromTarget();
        });
    }

    private const int EditGetLineCount = 0x00BA;
    // EM_SETRECT repaints after changing the formatting rectangle. EM_SETRECTNP (0xB4)
    // left the old wrapped final line visible until the next wheel/scroll message.
    private const int EditSetFormattingRectangle = 0x00B3;
    private const int EditLineScroll = 0x00B6;
    private const int EditGetFirstVisibleLine = 0x00CE;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, IntPtr data);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, ref NativeRect data);
}
