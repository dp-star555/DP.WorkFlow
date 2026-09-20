using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>支持确定、不确定和语义状态的现代进度条。</summary>
[Description("ModernProgressBar 现代进度条")]
[DisplayName("现代进度条")]
[ToolboxBitmap(typeof(ModernProgressBar), "Toolbox.Icons.Indicators.bmp")]
[ToolboxItem(true)]
public sealed class ModernProgressBar : ModernControl
{
    private const int FrameInterval = 30;
    private readonly System.Windows.Forms.Timer _timer;
    private double _minimum;
    private double _maximum = 100;
    private double _value;
    private bool _indeterminate;
    private float _phase;
    private ModernVisualStatus _status = ModernVisualStatus.Primary;
    private bool _showPercentage;
    private Orientation _orientation;
    private bool _reverseDirection;
    private string _displayText = string.Empty;
    private string _valueFormatString = string.Empty;

    public ModernProgressBar()
    {
        AccessibleRole = AccessibleRole.ProgressBar;
        TabStop = false;
        Size = new Size(240, 28);
        _timer = new System.Windows.Forms.Timer { Interval = FrameInterval };
        _timer.Tick += (_, _) =>
        {
            var inViewport = ModernAnimationVisibility.IsInVisibleViewport(this);
            ModernAnimationVisibility.UseInterval(_timer,
                inViewport ? FrameInterval : ModernAnimationVisibility.HiddenPollInterval);
            if (!inViewport) return;
            _phase = (_phase + .025f) % 1f;
            Invalidate();
        };
    }

    [Category("Behavior"), DefaultValue(0d)]
    public double Minimum { get => _minimum; set { _minimum = value; if (_maximum < value) _maximum = value; Value = _value; Invalidate(); } }

    [Category("Behavior"), DefaultValue(100d)]
    public double Maximum { get => _maximum; set { _maximum = Math.Max(_minimum, value); Value = _value; Invalidate(); } }

    [Category("Behavior"), DefaultValue(0d)]
    public double Value
    {
        get => _value;
        set { var next = ModernCompatibility.Clamp(value, Minimum, Maximum); if (_value.Equals(next)) return; _value = next; Invalidate(); AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1); }
    }

    [Category("Behavior"), DefaultValue(false)]
    public bool Indeterminate
    {
        get => _indeterminate;
        set { if (_indeterminate == value) return; _indeterminate = value; UpdateAnimation(); Invalidate(); }
    }

    [Category("Appearance"), DefaultValue(ModernVisualStatus.Primary)]
    public ModernVisualStatus Status { get => _status; set { if (_status == value) return; _status = value; Invalidate(); } }

    [Category("Appearance"), DefaultValue(Orientation.Horizontal)]
    [Description("进度条使用水平或垂直方向。")]
    public Orientation Orientation { get => _orientation; set { if (_orientation == value) return; _orientation = value; Invalidate(); } }

    [Category("Behavior"), DefaultValue(false)]
    [Description("是否反转进度填充方向。")]
    public bool ReverseDirection { get => _reverseDirection; set { if (_reverseDirection == value) return; _reverseDirection = value; Invalidate(); } }

    [Category("Appearance"), DefaultValue("")]
    [Description("非空时替代自动百分比显示的固定文本。")]
    public string DisplayText { get => _displayText; set { _displayText = value ?? string.Empty; Invalidate(); } }

    [Category("Appearance"), DefaultValue("")]
    [Description("DisplayText 为空时用于显示 Value 的可选格式字符串；空值显示百分比。")]
    public string ValueFormatString { get => _valueFormatString; set { _valueFormatString = value ?? string.Empty; Invalidate(); } }

    [Category("Appearance"), DefaultValue(false)]
    public bool ShowPercentage
    {
        get => _showPercentage;
        set
        {
            if (_showPercentage == value) return;
            _showPercentage = value;
            if (value && Height < ScaleLogical(28)) Height = ScaleLogical(28);
            Invalidate();
        }
    }

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
    {
        var visibleText = DisplayText.Length > 0
            ? DisplayText
            : ValueFormatString.Length > 0 ? Value.ToString(ValueFormatString) : $"{ProgressPercentage:0}%";
        var textHeight = ShowPercentage
            ? TextRenderer.MeasureText(visibleText, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height
            : 0;
        var color = ModernStatusColors.Resolve(Theme, Status);
        var ratio = Maximum <= Minimum ? 0 : (float)((Value - Minimum) / (Maximum - Minimum));
        var reverse = ReverseDirection ^ (Orientation == Orientation.Horizontal && RightToLeft == RightToLeft.Yes);
        if (Orientation == Orientation.Horizontal)
        {
            var trackAreaTop = ShowPercentage ? textHeight + ScaleLogical(3) : 0;
            var centerY = trackAreaTop + Math.Max(0, bounds.Height - trackAreaTop) / 2f;
            var trackHeight = Math.Min(Math.Max(0, bounds.Height - trackAreaTop), ScaleLogical(8));
            var track = new RectangleF(0, centerY - trackHeight / 2f, bounds.Width, trackHeight);
            canvas.Fill(Theme.BorderSecondary, track, track.Height / 2f);
            if (Indeterminate)
            {
                var width = Math.Max(ScaleLogical(32), track.Width * .28f);
                var phase = reverse ? 1 - _phase : _phase;
                var x = track.X + (track.Width + width) * phase - width;
                canvas.Fill(color, new RectangleF(x, track.Y, width, track.Height), track.Height / 2f);
            }
            else if (ratio > 0)
            {
                var width = track.Width * ratio;
                canvas.Fill(color, new RectangleF(reverse ? track.Right - width : track.X, track.Y, width, track.Height), track.Height / 2f);
            }
        }
        else
        {
            var trackTop = ShowPercentage ? textHeight + ScaleLogical(3) : 0;
            var centerX = bounds.Width / 2f;
            var trackWidth = Math.Min(bounds.Width, ScaleLogical(8));
            var track = new RectangleF(centerX - trackWidth / 2f, trackTop, trackWidth, Math.Max(0, bounds.Height - trackTop));
            canvas.Fill(Theme.BorderSecondary, track, track.Width / 2f);
            if (Indeterminate)
            {
                var height = Math.Max(ScaleLogical(32), track.Height * .28f);
                var phase = reverse ? 1 - _phase : _phase;
                var y = track.Y + (track.Height + height) * phase - height;
                canvas.Fill(color, new RectangleF(track.X, y, track.Width, height), track.Width / 2f);
            }
            else if (ratio > 0)
            {
                var height = track.Height * ratio;
                canvas.Fill(color, new RectangleF(track.X, reverse ? track.Y : track.Bottom - height, track.Width, height), track.Width / 2f);
            }
        }
        if (ShowPercentage)
            canvas.DrawText(visibleText, Font, Theme.Text,
                new Rectangle(bounds.X, bounds.Y, bounds.Width, textHeight), ContentAlignment.MiddleCenter);
    }

    [Browsable(false)]
    public double ProgressPercentage => Maximum <= Minimum ? 0 : (Value - Minimum) * 100 / (Maximum - Minimum);

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdateAnimation(); }
    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
    private void UpdateAnimation() => _timer.Enabled = Indeterminate && Visible && ModernUiSettings.EffectiveAnimationsEnabled;
}
