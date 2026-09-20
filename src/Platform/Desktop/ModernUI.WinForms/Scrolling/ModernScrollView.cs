using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ModernUI.WinForms;

/// <summary>使用覆盖式细圆角滑块滚动单个内容控件，不依赖方形 Win32 滚动条。</summary>
[Description("ModernScrollView 现代滚动视图")]
[DisplayName("现代滚动视图")]
[ToolboxBitmap(typeof(ModernScrollView), "Toolbox.Icons.Navigation.bmp")]
[ToolboxItem(true)]
public sealed partial class ModernScrollView : Panel, IMessageFilter
{
    private Control? _content;
    private int _offset;
    private bool _layingOut;
    private bool _changingContent;
    private bool _dpiLayoutActive;
    private FinalLayoutTransaction? _contentLayoutTransaction;
    private bool _hovered;
    private bool _dragging;
    private int _dragStartY;
    private int _dragStartOffset;
    private int _dragPreviewOffset;
    private int _measuredContentWidth = -1;
    private int _preferredContentHeight;
    private bool _contentMeasureInvalid = true;
    private bool _messageFilterRegistered;
    private int _compositedSuspensionCount;

    public ModernScrollView()
    {
        _thumbColor = ModernUiSettings.DefaultTheme.ScrollThumb;
        _thumbHoverColor = ModernUiSettings.DefaultTheme.ScrollThumbHover;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoScroll = true;
        TabStop = true;
    }

    private FinalLayoutTransaction ContentLayoutTransaction =>
        _contentLayoutTransaction ??= new FinalLayoutTransaction(this, LayoutContent);

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Control? Content
    {
        get => _content;
        set
        {
            if (ReferenceEquals(_content, value)) return;
            _changingContent = true;
            try
            {
                if (_content is not null)
                {
                    UntrackContent(_content);
                    Controls.Remove(_content);
                }
                _content = value;
                _offset = 0;
                InvalidateContentMeasure();
                if (value is not null)
                {
                    TrackContent(value);
                    if (!Controls.Contains(value)) Controls.Add(value);
                }
            }
            finally { _changingContent = false; }
            PerformLayout();
            Invalidate();
        }
    }

    /// <summary>滚动条交互槽宽度，内容会为其预留空间。</summary>
    [Category("Layout"), DefaultValue(14)]
    [Description("为滚动条预留的交互区域宽度，单位为逻辑像素。")]
    public int ScrollBarGutter { get; set; } = 14;

    /// <summary>常态滑块宽度。</summary>
    [Category("Appearance"), DefaultValue(ModernScrollThumbRenderer.DefaultThickness)]
    [Description("滚动条胶囊滑块在普通状态下的宽度。")]
    public int ScrollBarWidth { get; set; } = ModernScrollThumbRenderer.DefaultThickness;

    /// <summary>悬停或拖动时滑块宽度。</summary>
    [Category("Appearance"), DefaultValue(ModernScrollThumbRenderer.HoverThickness)]
    [Description("滚动条胶囊滑块在悬停或拖动状态下的宽度。")]
    public int ScrollBarHoverWidth { get; set; } = ModernScrollThumbRenderer.HoverThickness;

    [Category("Behavior"), DefaultValue(ModernScrollThumbRenderer.MinimumLength)]
    [Description("滚动条滑块允许使用的最小长度。")]
    public int MinimumThumbLength { get; set; } = ModernScrollThumbRenderer.MinimumLength;

    [Category("Behavior"), DefaultValue(48)]
    [Description("每次鼠标滚轮操作移动内容的逻辑像素数。")]
    public int WheelStep { get; set; } = 48;

    private bool _useCompositedScrolling = true;
    private bool _compositedRenderingActive;

    [Category("Behavior"), DefaultValue(true)]
    [Description("拖动滑块时是否实时移动内容。")]
    public bool LiveScrollDuringThumbDrag { get; set; } = true;

    [Category("Behavior"), DefaultValue(true)]
    [Description("是否通过视口级 Win32 子窗口双缓冲合成原生控件滚动帧。")]
    public bool UseCompositedScrolling
    {
        get => _useCompositedScrolling;
        set
        {
            if (_useCompositedScrolling == value) return;
            _useCompositedScrolling = value;
            UpdateCompositedRenderingStyle();
        }
    }

    private Color _thumbColor;
    private Color _thumbHoverColor;

    [Category("Appearance")]
    [Description("滚动条滑块在普通状态下的颜色。")]
    public Color ThumbColor { get => _thumbColor; set { _thumbColor = value; InvalidateScrollBarGutter(); } }

    [Category("Appearance")]
    [Description("滚动条滑块在悬停或拖动状态下的颜色。")]
    public Color ThumbHoverColor { get => _thumbHoverColor; set { _thumbHoverColor = value; InvalidateScrollBarGutter(); } }

    /// <summary>把滚动条颜色同步为指定主题的语义 Token。</summary>
    public void ApplyTheme(ModernTheme theme)
    {
        ModernCompatibility.ThrowIfNull(theme, nameof(theme));
        ThumbColor = theme.ScrollThumb;
        ThumbHoverColor = theme.ScrollThumbHover;
        BackColor = theme.Background;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ScrollOffset
    {
        get => _offset;
        set => SetOffset(value);
    }

    private int MaximumOffset => Math.Max(0, Math.Max(_preferredContentHeight,
        _content?.Height ?? 0) - ClientSize.Height);
    private bool CanScroll => MaximumOffset > 0;

}
