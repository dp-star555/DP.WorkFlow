using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace ModernUI.WinForms;

public sealed partial class ModernComboBox
{
    protected override void OnThemeChanged()
    {
        if (_comboBox is null) return;
        _comboBox.BackColor = Theme.Control;
        _comboBox.ForeColor = Theme.Text;
        _comboBox.ItemHeight = Math.Max(ScaleLogical(28), Font.Height + ScaleLogical(10));
        _arrow.Theme = Theme;
        _disabledSurface.Theme = Theme;
        _selectedImageSurface.Theme = Theme;
        _dropDown.ApplyTheme(Theme);
        NativeControlTheme.ApplyComboBox(_comboBox, Theme.IsDark);
        InvalidateChrome();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (_comboBox is null) return;
        _comboBox.Font = Font;
        _comboBox.ItemHeight = Math.Max(ScaleLogical(28), Font.Height + ScaleLogical(10));
        LayoutTransaction.Request();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        LayoutTransaction.Request();
    }

    protected override void OnPaddingChanged(EventArgs e)
    {
        base.OnPaddingChanged(e);
        LayoutTransaction.Request();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        _dpiMetricsInvalid = true;
        LayoutTransaction.Request();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        _disabledSurface.Visible = !Enabled;
        if (_disabledSurface.Visible)
        {
            _disabledSurface.Text = Text;
            _disabledSurface.BringToFront();
        }
        LayoutTransaction.Request();
    }

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
    {
        var inset = ScaleLogical(1f);
        var rectangle = RectangleF.Inflate(bounds, -inset, -inset);
        canvas.Fill(Theme.Control, rectangle, ScaleLogical(Theme.Radius));
        var active = ContainsFocus || _dropDown.Visible;
        canvas.Draw(ResolveBorderColor(), active || ModernValidation.IsEmphasized(ValidationState) ? ScaleLogical(1.5f) : ScaleLogical(1f),
            rectangle, ScaleLogical(Theme.Radius), centerStroke: true);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        _comboBox.Focus();
    }

    private void ApplyFinalLayout()
    {
        if (_dpiMetricsInvalid)
        {
            _dpiMetricsInvalid = false;
            OnThemeChanged();
        }
        LayoutEditor();
    }

    private void LayoutEditor()
    {
        if (_comboBox is null) return;
        var x = Padding.Left;
        var width = Math.Max(0, Width - Padding.Horizontal);
        var selectedImage = _itemImages.Resolve(SelectedItem);
        var imageSlotWidth = selectedImage is null ? 0 : ScaleLogical(24);
        var preferredHeight = _comboBox.PreferredHeight;
        // Center the native selected-text line from measured bounds. PreferredHeight includes the
        // ComboBox non-text area; half of (PreferredHeight - ItemHeight) is its native top inset.
        // This avoids DPI/font-specific visual offsets and keeps the actual text line centered.
        var textLineHeight = TextRenderer.MeasureText("Ag", Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height;
        var targetTextTop = (ClientRectangle.Height - textLineHeight) / 2;
        var nativeTextTopInset = Math.Max(0, (preferredHeight - _comboBox.ItemHeight) / 2);
        var editorTop = Math.Max(Padding.Top, targetTextTop - nativeTextTopInset);
        _comboBox.Bounds = new Rectangle(x + imageSlotWidth, editorTop,
            Math.Max(0, width - imageSlotWidth), preferredHeight);
        _selectedImageSurface.Bounds = new Rectangle(x + ScaleLogical(5), Padding.Top,
            Math.Max(0, imageSlotWidth - ScaleLogical(7)), Math.Max(0, Height - Padding.Vertical));
        _selectedImageSurface.Image = selectedImage;
        _selectedImageSurface.Visible = Enabled && selectedImage is not null;
        var arrowWidth = Math.Min(ScaleLogical(24), Math.Max(0, width));
        // The arrow is modern-drawn, so center its surface against the outer client rectangle rather
        // than inheriting the native editor's optical text correction.
        _arrow.Bounds = new Rectangle(x + Math.Max(0, width - arrowWidth), Padding.Top,
            arrowWidth, Math.Max(0, Height - Padding.Vertical));
        _disabledSurface.Bounds = new Rectangle(x, Padding.Top, width, Math.Max(0, Height - Padding.Vertical));
        // Chrome surfaces cover the native editor's square top/bottom edges. At high DPI the
        // rounded-corner band can become taller than the centered text margins and paint over the
        // selected glyphs. Clamp both bands to the measured free space around the text line.
        var textBottom = targetTextTop + textLineHeight;
        var freeTextMargin = Math.Max(1, Math.Min(targetTextTop, Height - textBottom));
        var chromeHeight = Math.Min(ScaleLogical(Theme.Radius + 3), freeTextMargin);
        _topChrome.Bounds = new Rectangle(0, 0, Width, chromeHeight);
        _bottomChrome.Bounds = new Rectangle(0, Height - chromeHeight, Width, chromeHeight);
        UpdateEditorRegion();
        if (_disabledSurface.Visible) _disabledSurface.BringToFront();
        else _arrow.BringToFront();
        _topChrome.BringToFront();
        _bottomChrome.BringToFront();
        CommitDropDownWidth();
    }

    private void CommitDropDownWidth()
    {
        if (_dropDown.Visible) CloseManagedDropDown();
    }

    protected override void OnHoverChanged()
    {
        base.OnHoverChanged();
        _topChrome.Invalidate();
        _bottomChrome.Invalidate();
    }

    private Color ResolveBorderColor()
    {
        var active = ContainsFocus || _dropDown.Visible;
        var fallback = active ? Theme.Primary : IsHovered ? Theme.PrimaryHover : Theme.Border;
        return ResolveValidationBorder(fallback);
    }

    protected override void InvalidateValidationVisual() => InvalidateChrome();

    private void InvalidateChrome()
    {
        Invalidate();
        _topChrome?.Invalidate();
        _bottomChrome?.Invalidate();
    }

    private static float CenteredChevronTop(float surfaceHeight, float iconSize, bool expanded)
    {
        // Keep the Path's actual Y extents centered, not merely its square icon bounds.
        var pathTop = expanded ? .34f : .36f;
        var pathBottom = expanded ? .64f : .66f;
        var pathCenter = (pathTop + pathBottom) / 2f;
        return (surfaceHeight - iconSize) / 2f + (.5f - pathCenter) * iconSize;
    }

    private void UpdateEditorRegion()
    {
        if (_comboBox.Width <= 0 || _comboBox.Height <= 0) return;
        var inset = Math.Max(1, ScaleLogical(2));
        var rectangle = new Rectangle(inset, inset,
            Math.Max(1, _comboBox.Width - inset * 2), Math.Max(1, _comboBox.Height - inset * 2));
        var previous = _comboBox.Region;
        _comboBox.Region = new Region(rectangle);
        previous?.Dispose();
    }
}
