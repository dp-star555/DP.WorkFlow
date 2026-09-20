using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace ModernUI.WinForms;

public sealed partial class ModernComboBox
{
    private void DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _comboBox.Items.Count) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected ? Theme.PrimaryBackground : Theme.Elevated);
        e.Graphics.FillRectangle(background, e.Bounds);
        var item = _comboBox.Items[e.Index];
        var text = GetItemText(item);
        var textLeft = e.Bounds.Left + ScaleLogical(8);
        if (_itemImages.Resolve(item) is { } image)
        {
            var iconSize = ScaleLogical(18);
            e.Graphics.DrawImage(image, new Rectangle(textLeft, e.Bounds.Top + (e.Bounds.Height - iconSize) / 2, iconSize, iconSize));
            textLeft += iconSize + ScaleLogical(8);
        }
        var textBounds = new Rectangle(textLeft, e.Bounds.Top,
            Math.Max(0, e.Bounds.Right - textLeft - ScaleLogical(8)), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, text, Font, textBounds,
            selected ? Theme.Primary : Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void BoundListChanged(object? sender, ListChangedEventArgs e) => SynchronizeSourceItems();

    private void UpdateItemImages()
    {
        if (_comboBox is null || _selectedImageSurface is null) return;
        var image = _itemImages.Resolve(SelectedItem);
        _selectedImageSurface.Image = image;
        _disabledSurface.Image = image;
        LayoutEditor();
        _comboBox.Invalidate();
    }

    private void SynchronizeSourceItems()
    {
        var selected = _comboBox.SelectedItem;
        _comboBox.BeginUpdate();
        try
        {
            _comboBox.Items.Clear();
            if (_sourceList is not null)
                foreach (var item in _sourceList) _comboBox.Items.Add(item);
            _comboBox.SelectedItem = selected is null
                ? null
                : _comboBox.Items.Cast<object>().FirstOrDefault(item => ReferenceEquals(item, selected));
        }
        finally
        {
            _comboBox.EndUpdate();
        }
    }

    private string GetItemText(object? item)
    {
        if (item is null) return string.Empty;
        return Convert.ToString(DataMemberResolver.Resolve(item, DisplayMember)) ?? string.Empty;
    }
}
