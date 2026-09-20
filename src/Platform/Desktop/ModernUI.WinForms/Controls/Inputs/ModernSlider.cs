using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>支持鼠标、键盘和提交事件的现代数值滑块。</summary>
[Description("ModernSlider 现代滑块")]
[DefaultEvent(nameof(ValueChanged))]
[DefaultBindingProperty(nameof(Value))]
[DisplayName("现代滑块")]
[ToolboxBitmap(typeof(ModernSlider), "Toolbox.Icons.Inputs.bmp")]
[ToolboxItem(true)]
public sealed class ModernSlider : ModernValidatedControl
{
    private decimal _minimum;
    private decimal _maximum = 100;
    private decimal _value;
    private decimal _step = 1;
    private decimal _largeChange = 10;
    private bool _dragging;
    private bool _showValue;
    private bool _readOnly;
    private Orientation _orientation;
    private bool _reverseDirection;
    private int _tickFrequency = 10;
    private TickStyle _tickStyle = TickStyle.None;
    private string _valueFormatString = string.Empty;

    public ModernSlider()
    {
        AccessibleRole = AccessibleRole.Slider;
        Size = new Size(220, 32);
        Cursor = Cursors.Hand;
    }

    [Category("Behavior"), DefaultValue(typeof(decimal), "0")]
    public decimal Minimum { get => _minimum; set { _minimum = value; if (_maximum < value) _maximum = value; Value = _value; Invalidate(); } }

    [Category("Behavior"), DefaultValue(typeof(decimal), "100")]
    public decimal Maximum { get => _maximum; set { _maximum = Math.Max(_minimum, value); Value = _value; Invalidate(); } }

    [Category("Behavior"), DefaultValue(typeof(decimal), "0"), Bindable(true)]
    public decimal Value
    {
        get => _value;
        set { var next = ModernCompatibility.Clamp(value, Minimum, Maximum); if (_value == next) return; _value = next; ValueChanged?.Invoke(this, EventArgs.Empty); AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1); Invalidate(); }
    }

    [Category("Behavior"), DefaultValue(typeof(decimal), "1")]
    public decimal Step { get => _step; set => _step = Math.Max(.0000001m, value); }

    [Category("Behavior"), DefaultValue(typeof(decimal), "10")]
    public decimal LargeChange { get => _largeChange; set => _largeChange = Math.Max(Step, value); }

    [Category("Behavior"), DefaultValue(false)]
    public bool ReadOnly { get => _readOnly; set { if (_readOnly == value) return; _readOnly = value; Cursor = value ? Cursors.Default : Cursors.Hand; Invalidate(); } }

    [Category("Appearance"), DefaultValue(Orientation.Horizontal)]
    public Orientation Orientation { get => _orientation; set { if (_orientation == value) return; _orientation = value; Invalidate(); } }

    [Category("Behavior"), DefaultValue(false)]
    public bool ReverseDirection { get => _reverseDirection; set { if (_reverseDirection == value) return; _reverseDirection = value; Invalidate(); } }

    [Category("Appearance"), DefaultValue(10)]
    public int TickFrequency { get => _tickFrequency; set { _tickFrequency = Math.Max(1, value); Invalidate(); } }

    [Category("Appearance"), DefaultValue(TickStyle.None)]
    public TickStyle TickStyle { get => _tickStyle; set { if (_tickStyle == value) return; _tickStyle = value; Invalidate(); } }

    [Category("Appearance"), DefaultValue("")]
    public string ValueFormatString { get => _valueFormatString; set { _valueFormatString = value ?? string.Empty; Invalidate(); } }

    [Category("Appearance"), DefaultValue(false)]
    public bool ShowValue
    {
        get => _showValue;
        set
        {
            if (_showValue == value) return;
            _showValue = value;
            if (value && Height < ScaleLogical(32)) Height = ScaleLogical(32);
            Invalidate();
        }
    }

    public event EventHandler? ValueChanged;
    public event EventHandler? ValueCommitted;

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
    {
        var padding = ScaleLogical(10);
        var valueText = Value.ToString(ValueFormatString.Length == 0 ? null : ValueFormatString, Culture);
        var textHeight = ShowValue
            ? TextRenderer.MeasureText(valueText, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height
            : 0;
        var ratio = DisplayRatio;
        var accent = ModernValidation.ResolveBorder(Theme, ValidationState, Theme.Primary);
        var diameter = ScaleLogical(IsPressed || _dragging ? 16 : 14);
        if (Orientation == Orientation.Horizontal)
        {
            var trackAreaTop = ShowValue ? textHeight + ScaleLogical(3) : 0;
            var centerY = trackAreaTop + Math.Max(0, bounds.Height - trackAreaTop) / 2f;
            var track = new RectangleF(padding, centerY - ScaleLogical(2), Math.Max(0, bounds.Width - padding * 2), ScaleLogical(4));
            DrawTicks(canvas, track);
            canvas.Fill(Theme.BorderSecondary, track, track.Height / 2f);
            if (ratio > 0) canvas.Fill(accent, new RectangleF(track.X, track.Y, track.Width * ratio, track.Height), track.Height / 2f);
            var x = track.X + track.Width * ratio;
            canvas.FillEllipse(Theme.Container, new RectangleF(x - diameter / 2f, centerY - diameter / 2f, diameter, diameter));
            canvas.Draw(accent, ScaleLogical(2), new RectangleF(x - diameter / 2f, centerY - diameter / 2f, diameter, diameter), diameter / 2f);
        }
        else
        {
            var textTop = ShowValue ? textHeight + ScaleLogical(3) : 0;
            var centerX = bounds.Width / 2f;
            var track = new RectangleF(centerX - ScaleLogical(2), padding + textTop, ScaleLogical(4),
                Math.Max(0, bounds.Height - padding * 2 - textTop));
            DrawTicks(canvas, track);
            canvas.Fill(Theme.BorderSecondary, track, track.Width / 2f);
            if (ratio > 0)
            {
                var fillHeight = track.Height * ratio;
                canvas.Fill(accent, new RectangleF(track.X, track.Bottom - fillHeight, track.Width, fillHeight), track.Width / 2f);
            }
            var y = track.Bottom - track.Height * ratio;
            canvas.FillEllipse(Theme.Container, new RectangleF(centerX - diameter / 2f, y - diameter / 2f, diameter, diameter));
            canvas.Draw(accent, ScaleLogical(2), new RectangleF(centerX - diameter / 2f, y - diameter / 2f, diameter, diameter), diameter / 2f);
        }
        if (ShowValue)
            canvas.DrawText(valueText, Font, Theme.Text,
                new Rectangle(bounds.X, bounds.Y, bounds.Width, textHeight), ContentAlignment.MiddleCenter);
    }

    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left && !ReadOnly) { _dragging = true; SetFromPoint(e.Location); } }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (_dragging) SetFromPoint(e.Location); }
    protected override void OnMouseUp(MouseEventArgs e) { var commit = _dragging; _dragging = false; base.OnMouseUp(e); if (commit) ValueCommitted?.Invoke(this, EventArgs.Empty); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (ReadOnly) return;
        var old = Value;
        var decrease = ReverseDirection ? Step : -Step;
        var increase = ReverseDirection ? -Step : Step;
        if (Orientation == Orientation.Horizontal && RightToLeft == RightToLeft.Yes)
            (decrease, increase) = (increase, decrease);
        Value = e.KeyCode switch
        {
            Keys.Left or Keys.Down => Value + decrease,
            Keys.Right or Keys.Up => Value + increase,
            Keys.PageDown => Value - LargeChange,
            Keys.PageUp => Value + LargeChange,
            Keys.Home => Minimum,
            Keys.End => Maximum,
            _ => Value
        };
        if (Value != old) { e.Handled = true; ValueCommitted?.Invoke(this, EventArgs.Empty); }
    }

    private float DisplayRatio
    {
        get
        {
            var ratio = Maximum <= Minimum ? 0 : (float)((Value - Minimum) / (Maximum - Minimum));
            var reverse = ReverseDirection ^ (Orientation == Orientation.Horizontal && RightToLeft == RightToLeft.Yes);
            return reverse ? 1 - ratio : ratio;
        }
    }

    private void SetFromPoint(Point point)
    {
        var padding = ScaleLogical(10);
        decimal ratio;
        if (Orientation == Orientation.Horizontal)
        {
            var width = Math.Max(1, ClientSize.Width - padding * 2);
            ratio = ModernCompatibility.Clamp((point.X - padding) / (decimal)width, 0, 1);
            if (RightToLeft == RightToLeft.Yes) ratio = 1 - ratio;
        }
        else
        {
            var height = Math.Max(1, ClientSize.Height - padding * 2);
            ratio = 1 - ModernCompatibility.Clamp((point.Y - padding) / (decimal)height, 0, 1);
        }
        if (ReverseDirection) ratio = 1 - ratio;
        var raw = Minimum + (Maximum - Minimum) * ratio;
        var steps = Math.Round((raw - Minimum) / Step, MidpointRounding.AwayFromZero);
        Value = ModernCompatibility.Clamp(Minimum + steps * Step, Minimum, Maximum);
    }

    private void DrawTicks(GdiCanvas canvas, RectangleF track)
    {
        if (TickStyle == TickStyle.None || TickFrequency <= 0 || Maximum <= Minimum) return;
        var tickCount = (int)Math.Min(200, Math.Floor((Maximum - Minimum) / TickFrequency));
        var tickLength = ScaleLogical(3);
        for (var index = 0; index <= tickCount; index++)
        {
            var ratio = tickCount == 0 ? 0 : index / (float)tickCount;
            if (Orientation == Orientation.Horizontal)
            {
                var x = track.Left + track.Width * ratio;
                canvas.DrawLines(Theme.Border, ScaleLogical(1),
                    [new PointF(x, track.Top - tickLength), new PointF(x, track.Bottom + tickLength)]);
            }
            else
            {
                var y = track.Bottom - track.Height * ratio;
                canvas.DrawLines(Theme.Border, ScaleLogical(1),
                    [new PointF(track.Left - tickLength, y), new PointF(track.Right + tickLength, y)]);
            }
        }
    }
}
