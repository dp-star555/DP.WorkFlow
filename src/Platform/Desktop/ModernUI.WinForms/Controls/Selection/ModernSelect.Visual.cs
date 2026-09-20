using System.Collections;
using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ModernUI.WinForms;

public partial class ModernSelect
{
    protected override void OnThemeChanged()
    {
        if (_surface is null || _dropDown is null) return;
        _surface.ItemHeight = Math.Max(30, Theme.ControlHeight);
        _dropDown.ApplyTheme(Theme);
        Invalidate();
    }

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
    {
        var active = _dropDown.Visible;
        if (EmbeddedCellAppearance)
        {
            canvas.Fill(EmbeddedBackColor, bounds, 0);
        }
        else
        {
            var borderInset = ScaleLogical(1f);
            var rect = RectangleF.Inflate(bounds, -borderInset, -borderInset);
            canvas.Fill(Theme.Control, rect, ScaleLogical(Theme.Radius));
            var focused = ShowFocusBorder && ShouldShowFocusCue;
            var fallback = active ? Theme.Primary
                : focused ? ModernFocusVisual.ResolveColor(Theme)
                : IsHovered ? Theme.PrimaryHover : Theme.Border;
            var border = ResolveValidationBorder(fallback);
            canvas.Draw(border, active || focused || ModernValidation.IsEmphasized(ValidationState) ? ModernFocusVisual.StrokeWidth(DpiScale) : ScaleLogical(1f),
                rect, ScaleLogical(Theme.Radius), centerStroke: true);
        }

        // DataGridView already positions an editing control inside the cell's horizontal padding.
        // Embedded mode must not apply that padding a second time.
        var horizontalPadding = ScaleLogical(EmbeddedCellAppearance ? 0 : 10);
        var iconSize = ScaleLogical(12);
        var iconPadding = ScaleLogical(EmbeddedCellAppearance ? 0 : 10);
        var rightToLeft = RightToLeft == RightToLeft.Yes;
        var iconBounds = new RectangleF(
            rightToLeft ? iconPadding : bounds.Width - iconPadding - iconSize,
            (bounds.Height - iconSize) / 2f, iconSize, iconSize);
        var text = GetItemText(SelectedItem);
        var textColor = EmbeddedCellAppearance
            ? EmbeddedForeColor
            : string.IsNullOrEmpty(text) ? Theme.TextSecondary : Theme.Text;
        var textLeft = rightToLeft ? (int)iconBounds.Right + ScaleLogical(8) : horizontalPadding;
        var textRight = rightToLeft ? bounds.Width - horizontalPadding : (int)iconBounds.Left - ScaleLogical(8);
        if (_itemImages.Resolve(SelectedItem) is { } selectedImage)
        {
            var itemIconSize = ScaleLogical(16);
            var imageLeft = rightToLeft ? textRight - itemIconSize : textLeft;
            canvas.DrawImage(selectedImage, new RectangleF(imageLeft, (bounds.Height - itemIconSize) / 2f,
                itemIconSize, itemIconSize));
            if (rightToLeft) textRight = imageLeft - ScaleLogical(6);
            else textLeft += itemIconSize + ScaleLogical(6);
        }
        canvas.DrawText(string.IsNullOrEmpty(text) ? EffectivePlaceholderText : text, Font, textColor,
            new Rectangle(textLeft, 0, Math.Max(0, textRight - textLeft), bounds.Height),
            rightToLeft ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft);
        canvas.DrawIcon(active ? ModernIconKind.ChevronUp : ModernIconKind.ChevronDown,
            EmbeddedCellAppearance ? EmbeddedForeColor : Theme.TextSecondary, iconBounds, ScaleLogical(1.5f));
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (_dropDown.Visible) _dropDown.Close();
        else ShowDropDown();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is (Keys.Enter or Keys.Space or Keys.Down) && !_dropDown.Visible)
        {
            ShowDropDown();
            return true;
        }
        if (_dropDown.Visible && _surface.HandleKey(keyData)) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ShowDropDown()
    {
        if (!Enabled || _dropDown.Visible || Items.Count == 0) return;
        Focus();
        var rows = ModernCompatibility.Clamp(Items.Count, 1, Math.Max(1, MaxDropDownItems));
        _surface.ItemHeight = Math.Max(ScaleLogical(30), ScaleLogical(Theme.ControlHeight));
        _surface.ItemCount = Items.Count;
        _surface.VisibleRows = rows;
        _surface.InitialSelectedIndex = SelectedIndex;
        _dropDown.Show(this, new Size(Math.Max(Width, ScaleLogical(160)), rows * _surface.ItemHeight + ScaleLogical(8)), ScaleLogical(2),
            Math.Max(0, DropDownAnimationDuration));
        if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
        Invalidate();
    }

    private void DrawItem(Graphics graphics, int index, Rectangle bounds)
    {
        var selected = index == SelectedIndex;
        var hovered = index == _hoveredIndex;
        using (var surface = new SolidBrush(Theme.Elevated)) graphics.FillRectangle(surface, bounds);

        // 参考 AntdUI：选中项始终 PrimaryBg；非选中项 Hover 使用中性 FillTertiary。
        var highlightColor = selected ? Theme.PrimaryBackground : hovered ? Theme.ControlHover : Color.Empty;
        if (highlightColor != Color.Empty)
        {
            var highlight = RectangleF.Inflate(bounds, -ScaleLogical(4), -ScaleLogical(2));
            using var path = Geometry.CreateRoundedRectangle(highlight, Math.Max(3, ScaleLogical(Theme.Radius) - 2));
            using var background = new SolidBrush(highlightColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.FillPath(background, path);
        }

        var item = _model.Items[index];
        var rightToLeft = RightToLeft == RightToLeft.Yes;
        var textLeft = bounds.Left + ScaleLogical(12);
        var textRight = bounds.Right - ScaleLogical(12);
        if (_itemImages.Resolve(item) is { } image)
        {
            var iconSize = ScaleLogical(18);
            var imageLeft = rightToLeft ? textRight - iconSize : textLeft;
            graphics.DrawImage(image, new Rectangle(imageLeft,
                bounds.Top + (bounds.Height - iconSize) / 2, iconSize, iconSize));
            if (rightToLeft) textRight = imageLeft - ScaleLogical(8);
            else textLeft += iconSize + ScaleLogical(8);
        }
        var textBounds = new Rectangle(textLeft, bounds.Top, Math.Max(0, textRight - textLeft), bounds.Height);
        TextRenderer.DrawText(graphics, GetItemText(item), Font,
            textBounds, selected ? Theme.Primary : Theme.Text,
            (rightToLeft ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left) |
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
