using System.Collections;
using System.ComponentModel;

namespace ModernUI.WinForms;

public sealed partial class ModernSelectMultiple
{
    protected override void OnThemeChanged()
    {
        if (_list is null) return;
        _surface.ItemHeight = Math.Max(30, Theme.ControlHeight);
        _dropDown.ApplyTheme(Theme);
        Invalidate();
    }

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
    {
        var borderInset = ScaleLogical(1f);
        var rect = RectangleF.Inflate(bounds, -borderInset, -borderInset);
        canvas.Fill(Theme.Control, rect, ScaleLogical(Theme.Radius));
        var active = _dropDown.Visible;
        var focused = ShowFocusBorder && ShouldShowFocusCue;
        var fallback = active ? Theme.Primary
            : focused ? ModernFocusVisual.ResolveColor(Theme)
            : IsHovered ? Theme.PrimaryHover : Theme.Border;
        var border = ResolveValidationBorder(fallback);
        canvas.Draw(border, active || focused || ModernValidation.IsEmphasized(ValidationState) ? ModernFocusVisual.StrokeWidth(DpiScale) : ScaleLogical(1f),
            rect, ScaleLogical(Theme.Radius), centerStroke: true);
        var horizontalPadding = ScaleLogical(10);
        var iconSize = ScaleLogical(12);
        var rightToLeft = RightToLeft == RightToLeft.Yes;
        var iconBounds = new RectangleF(
            rightToLeft ? horizontalPadding : bounds.Width - horizontalPadding - iconSize,
            (bounds.Height - iconSize) / 2f, iconSize, iconSize);
        var contentLeft = rightToLeft ? (int)iconBounds.Right + ScaleLogical(8) : horizontalPadding;
        var contentRight = rightToLeft ? bounds.Width - horizontalPadding : (int)iconBounds.Left - ScaleLogical(8);
        var selected = SelectedItems;
        if (DisplayMode == MultipleSelectionDisplayMode.Tags && selected.Count > 0)
            DrawTags(canvas, selected, contentLeft, contentRight);
        else
        {
            var text = selected.Count == 0 ? EffectivePlaceholderText : string.Join(", ", selected.Select(GetItemText));
            var textColor = selected.Count == 0 ? Theme.TextSecondary : Theme.Text;
            var textLeft = contentLeft;
            var textRight = contentRight;
            if (selected.Count > 0 && _itemImages.Resolve(selected[0]) is { } selectedImage)
            {
                var itemIconSize = ScaleLogical(16);
                var imageLeft = rightToLeft ? textRight - itemIconSize : textLeft;
                canvas.DrawImage(selectedImage, new RectangleF(imageLeft, (bounds.Height - itemIconSize) / 2f,
                    itemIconSize, itemIconSize));
                if (rightToLeft) textRight = imageLeft - ScaleLogical(6);
                else textLeft += itemIconSize + ScaleLogical(6);
            }
            canvas.DrawText(text, Font, textColor,
                new Rectangle(textLeft, 0, Math.Max(0, textRight - textLeft), bounds.Height),
                rightToLeft ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft);
        }
        canvas.DrawIcon(active ? ModernIconKind.ChevronUp : ModernIconKind.ChevronDown,
            Theme.TextSecondary, iconBounds, ScaleLogical(1.5f));
    }

    private void DrawTags(GdiCanvas canvas, IReadOnlyList<object> selected, int left, int right)
    {
        var rightToLeft = RightToLeft == RightToLeft.Yes;
        var layouts = GetTagLayouts(selected, left, right);
        foreach (var tag in layouts)
        {
            canvas.Fill(Theme.PrimaryBackground, tag.Bounds, ScaleLogical(4));
            canvas.Draw(Geometry.Blend(Theme.Border, Theme.Primary, .25f), ScaleLogical(1), tag.Bounds, ScaleLogical(4));
            var textLeft = rightToLeft ? tag.CloseBounds.Right + ScaleLogical(3) : tag.Bounds.Left + ScaleLogical(6);
            var textRight = rightToLeft ? tag.Bounds.Right - ScaleLogical(6) : tag.CloseBounds.Left - ScaleLogical(3);
            if (_itemImages.Resolve(tag.Item) is { } image)
            {
                var imageSize = ScaleLogical(14);
                var imageLeft = rightToLeft ? textRight - imageSize : textLeft;
                canvas.DrawImage(image, new RectangleF(imageLeft,
                    tag.Bounds.Top + (tag.Bounds.Height - imageSize) / 2f, imageSize, imageSize));
                if (rightToLeft) textRight = imageLeft - ScaleLogical(4);
                else textLeft += imageSize + ScaleLogical(4);
            }
            canvas.DrawText(GetItemText(tag.Item!), Font, Theme.Text,
                new Rectangle(textLeft, tag.Bounds.Top, Math.Max(0, textRight - textLeft), tag.Bounds.Height),
                rightToLeft ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft);
            canvas.DrawIcon(ModernIconKind.Close, Theme.TextSecondary, tag.CloseBounds, ScaleLogical(1.2f));
        }
        var hidden = selected.Count - layouts.Count;
        if (hidden > 0)
        {
            var hiddenBounds = rightToLeft
                ? new Rectangle(left, 0, Math.Max(0,
                    (layouts.Count == 0 ? right : layouts[layouts.Count - 1].Bounds.Left - ScaleLogical(4)) - left), Height)
                : new Rectangle(layouts.Count == 0 ? left : layouts[layouts.Count - 1].Bounds.Right + ScaleLogical(4), 0,
                    Math.Max(0, right - (layouts.Count == 0 ? left : layouts[layouts.Count - 1].Bounds.Right + ScaleLogical(4))), Height);
            canvas.DrawText($"+{hidden}", Font, Theme.TextSecondary, hiddenBounds,
                rightToLeft ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft);
        }
    }

    private List<TagLayout> GetTagLayouts(IReadOnlyList<object> selected, int left, int right)
    {
        var result = new List<TagLayout>();
        var rightToLeft = RightToLeft == RightToLeft.Yes;
        var x = rightToLeft ? right : left;
        var height = Math.Max(20, Height - ScaleLogical(10));
        var top = (Height - height) / 2;
        foreach (var item in selected.Take(MaxVisibleTags))
        {
            var iconWidth = _itemImages.Resolve(item) is null ? 0 : ScaleLogical(18);
            var textWidth = TextRenderer.MeasureText(GetItemText(item), Font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            var width = Math.Min(ScaleLogical(180), textWidth + iconWidth + ScaleLogical(46));
            if (rightToLeft ? x - width < left : x + width > right) break;
            var bounds = new Rectangle(rightToLeft ? x - width : x, top, width, height);
            var close = new Rectangle(
                rightToLeft ? bounds.Left + ScaleLogical(6) : bounds.Right - ScaleLogical(18),
                bounds.Top + (bounds.Height - ScaleLogical(12)) / 2, ScaleLogical(12), ScaleLogical(12));
            result.Add(new TagLayout(item, bounds, close));
            x = rightToLeft ? bounds.Left - ScaleLogical(4) : bounds.Right + ScaleLogical(4);
        }
        return result;
    }

    private readonly record struct TagLayout(object? Item, Rectangle Bounds, Rectangle CloseBounds);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (ReadOnly) { ToggleDropDown(); return; }
        if (DisplayMode == MultipleSelectionDisplayMode.Tags)
        {
            var tags = GetTagLayouts(SelectedItems, ScaleLogical(10), Width - ScaleLogical(32));
            var removed = tags.FirstOrDefault(tag => tag.CloseBounds.Contains(e.Location));
            if (removed.Item is not null)
            {
                var index = _list.Items.IndexOf(removed.Item);
                if (index >= 0) SetItemCheckedCore(index, false, IsHandleCreated);
                return;
            }
        }
        ToggleDropDown();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!ReadOnly && keyData == Keys.Back && DisplayMode == MultipleSelectionDisplayMode.Tags && !_dropDown.Visible && SelectedItems.Count > 0)
        {
            var last = SelectedItems[SelectedItems.Count - 1];
            var index = _list.Items.IndexOf(last);
            if (index >= 0) SetItemCheckedCore(index, false, true);
            return true;
        }
        if (keyData is (Keys.Enter or Keys.Space or Keys.Down) && !_dropDown.Visible)
        {
            _openedFromKeyboard = true;
            ShowDropDown();
            return true;
        }
        if (_dropDown.Visible)
        {
            if (_openedFromKeyboard)
            {
                // 弹层出现在系统光标下方时可能立即收到合成的 MouseMove；
                // 键盘导航必须从确定的初始项开始，而不是受全局光标位置影响。
                _surface.ResetHoveredIndex();
                _openedFromKeyboard = false;
            }
            if (_surface.HandleKey(keyData)) return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ShowDropDown()
    {
        if (!Enabled || _dropDown.Visible || _list.Items.Count == 0) return;
        Focus();
        var visibleRows = ModernCompatibility.Clamp(_list.Items.Count, 1, Math.Max(1, MaxDropDownItems));
        _surface.ItemHeight = Math.Max(ScaleLogical(30), ScaleLogical(Theme.ControlHeight));
        _surface.ItemCount = _list.Items.Count;
        _surface.VisibleRows = visibleRows;
        _surface.InitialSelectedIndex = _list.Items.Cast<object>()
            .Select((item, index) => (item, index))
            .Where(entry => _checkedItems.Contains(entry.item))
            .Select(entry => entry.index)
            .DefaultIfEmpty(-1)
            .First();
        _dropDown.Show(this,
            new Size(Math.Max(Width, ScaleLogical(160)), visibleRows * _surface.ItemHeight + ScaleLogical(8)),
            ScaleLogical(2), Math.Max(0, DropDownAnimationDuration));
        if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
        Invalidate();
    }

    private void ToggleItem(int index) =>
        SetItemCheckedCore(index, !_checkedItems.Contains(_list.Items[index]), true);

    private void SetItemCheckedCore(int index, bool value, bool raiseEvent)
    {
        var item = _list.Items[index];
        var changed = value ? _checkedItems.Add(item) : _checkedItems.Remove(item);
        if (!changed) return;
        AnimateItemCheck(index, value);
        if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        Invalidate();
        if (raiseEvent) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DrawItem(Graphics graphics, int index, Rectangle bounds)
    {
        var hovered = index == _hoveredIndex;
        var isChecked = _checkedItems.Contains(_list.Items[index]);
        var progress = _checkProgress.GetValueOrDefault(index, isChecked ? 1f : 0f);
        using (var surface = new SolidBrush(Theme.Elevated)) graphics.FillRectangle(surface, bounds);
        // 与单选下拉保持一致：已选项使用 PrimaryBackground，悬停已选项再轻微加深。
        var highlightColor = isChecked
            ? hovered ? Geometry.Blend(Theme.PrimaryBackground, Theme.Primary, .08f) : Theme.PrimaryBackground
            : hovered ? Theme.ControlHover : Color.Empty;
        if (highlightColor != Color.Empty)
        {
            var highlight = RectangleF.Inflate(bounds, -ScaleLogical(4), -ScaleLogical(2));
            using var path = Geometry.CreateRoundedRectangle(highlight, Math.Max(3, ScaleLogical(Theme.Radius) - 2));
            using var background = new SolidBrush(highlightColor);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.FillPath(background, path);
        }

        var rightToLeft = RightToLeft == RightToLeft.Yes;
        var boxSize = ScaleLogical(18);
        var box = new RectangleF(
            rightToLeft ? bounds.Right - ScaleLogical(10) - boxSize : bounds.X + ScaleLogical(10),
            bounds.Y + (bounds.Height - boxSize) / 2f, boxSize, boxSize);
        using (var canvas = new GdiCanvas(graphics))
            ModernCheckboxRenderer.Draw(canvas, box, Theme, progress, false, hovered, Enabled, DpiScale);
        var item = _list.Items[index];
        var textLeft = rightToLeft ? bounds.Left + ScaleLogical(12) : (int)box.Right + ScaleLogical(9);
        var textRight = rightToLeft ? (int)box.Left - ScaleLogical(9) : bounds.Right - ScaleLogical(12);
        if (_itemImages.Resolve(item) is { } image)
        {
            var iconSize = ScaleLogical(18);
            var imageLeft = rightToLeft ? textRight - iconSize : textLeft;
            graphics.DrawImage(image, new Rectangle(imageLeft,
                bounds.Top + (bounds.Height - iconSize) / 2, iconSize, iconSize));
            if (rightToLeft) textRight = imageLeft - ScaleLogical(8);
            else textLeft += iconSize + ScaleLogical(8);
        }
        var textBounds = new Rectangle(textLeft, bounds.Y, Math.Max(0, textRight - textLeft), bounds.Height);
        TextRenderer.DrawText(graphics, GetItemText(item), Font,
            textBounds, Theme.Text,
            (rightToLeft ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left) |
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private void SetHoveredIndex(int index)
    {
        if (_hoveredIndex == index) return;
        var previous = _hoveredIndex;
        _hoveredIndex = index;
        if (previous >= 0 && previous < _list.Items.Count) _surface.InvalidateItem(previous);
        if (index >= 0 && index < _list.Items.Count) _surface.InvalidateItem(index);
    }

    private void AnimateItemCheck(int index, bool isChecked)
    {
        _checkAnimations.Remove(index, out var previous);
        previous?.Dispose();
        var target = isChecked ? 1f : 0f;
        var from = _checkProgress.GetValueOrDefault(index, isChecked ? 0f : 1f);
        // 在状态集合改变后、首个动画 Tick 之前固定起始帧，避免先绘制终态再跳回起点造成闪烁。
        _checkProgress[index] = from;
        _checkAnimations[index] = ModernAnimation.Start(_surface, from, target,
            Math.Max(0, CheckBoxAnimationDuration),
            value =>
            {
                _checkProgress[index] = value;
                if (index < _list.Items.Count) _surface.InvalidateItem(index);
            },
            () =>
            {
                _checkProgress.Remove(index);
                _checkAnimations.Remove(index);
            });
    }
}
