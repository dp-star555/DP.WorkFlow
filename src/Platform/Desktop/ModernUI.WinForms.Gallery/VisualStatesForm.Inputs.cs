using System.Globalization;
using ModernUI.Localization;
using ModernUI.WinForms;

namespace ModernUI.WinForms.Gallery;

internal sealed partial class VisualStatesForm
{
    private ModernSegmentedControl CreateSegmented()
    {
        var segmented = new ModernSegmentedControl { Width = 390 };
        var select = new ModernSegmentedItem(string.Empty, "select");
        var rectangle = new ModernSegmentedItem(string.Empty, "rectangle");
        var ellipse = new ModernSegmentedItem(string.Empty, "ellipse");
        var polygon = new ModernSegmentedItem(string.Empty, "polygon") { Enabled = false };
        BindGalleryText("SegmentSelect", text => select.Text = text);
        BindGalleryText("SegmentRectangle", text => rectangle.Text = text);
        BindGalleryText("SegmentEllipse", text => ellipse.Text = text);
        BindGalleryText("SegmentPolygon", text => polygon.Text = text);
        segmented.Items.Add(select);
        segmented.Items.Add(rectangle);
        segmented.Items.Add(ellipse);
        segmented.Items.Add(polygon);
        segmented.SelectedIndex = 1;
        return segmented;
    }

    private static ModernComboBox CreateComboBox(string text, bool hasError = false, bool enabled = true)
    {
        var comboBox = new ModernComboBox
        {
            Width = 218,
            Text = text,
            HasError = hasError,
            Enabled = enabled,
            DisplayMember = nameof(VisualChoice.Name),
            ImageKeyMember = nameof(VisualChoice.IconKey)
        };
        comboBox.ImageList = CreateChoiceImages(comboBox);
        comboBox.Items.AddRange([
            new VisualChoice(1, "System", "info"),
            new VisualChoice(2, "System.Collections.Generic", "key"),
            new VisualChoice(3, "System.Linq", "search"),
            new VisualChoice(4, "System.Text", "settings")
        ]);
        comboBox.SelectedIndex = comboBox.Items.Cast<VisualChoice>().ToList().FindIndex(item => item.Name == text);
        comboBox.Text = text;
        comboBox.Select(text.Length, 0);
        return comboBox;
    }

    private static ModernInputNumber CreateNumber(decimal value, decimal minimum, decimal maximum, bool enabled = true) =>
        new()
        {
            Width = 188,
            Minimum = minimum,
            Maximum = maximum,
            Value = value,
            Enabled = enabled
        };

    private ModernSelect CreateSelect(int selectedIndex, bool enabled = true)
    {
        var select = new ModernSelect
        {
            Width = 170,
            Enabled = enabled,
            DisplayMember = nameof(VisualChoice.Name),
            ValueMember = nameof(VisualChoice.Id),
            ImageKeyMember = nameof(VisualChoice.IconKey)
        };
        BindGalleryText("SelectContinuous", _ =>
        {
            var selectedValue = select.SelectedValue;
            select.DataSource = new[]
            {
                new VisualChoice(1, G("SelectContinuous"), "play"),
                new VisualChoice(2, G("SelectExternal"), "key"),
                new VisualChoice(3, G("SelectSingleFrame"), "info")
            };
            if (selectedValue is not null) select.SelectedValue = selectedValue;
        });
        select.ImageList = CreateChoiceImages(select);
        select.SelectedIndex = selectedIndex;
        return select;
    }

    private ModernSelectMultiple CreateMultiSelect(bool selected)
    {
        var select = new ModernSelectMultiple
        {
            Width = 170,
            DisplayMember = nameof(VisualChoice.Name),
            ValueMember = nameof(VisualChoice.Id),
            ImageKeyMember = nameof(VisualChoice.IconKey)
        };
        BindGalleryText("MultiRawImage", _ =>
        {
            var selectedValues = select.SelectedValues.ToArray();
            select.DataSource = new[]
            {
                new VisualChoice(1, G("MultiRawImage"), "info"),
                new VisualChoice(2, G("MultiResult"), "search"),
                new VisualChoice(3, G("MultiLog"), "settings")
            };
            if (selectedValues.Length > 0) select.SetSelectedValues(selectedValues);
        });
        select.ImageList = CreateChoiceImages(select);
        if (selected) select.SetSelectedValues([1, 2]);
        return select;
    }

    private static ImageList CreateChoiceImages(Control owner)
    {
        var images = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
        foreach (var (key, icon) in new[]
        {
            ("info", ModernIconKind.Info),
            ("play", ModernIconKind.Play),
            ("key", ModernIconKind.Key),
            ("search", ModernIconKind.Search),
            ("settings", ModernIconKind.Settings)
        })
            images.Images.Add(key, ModernIcons.CreateBitmap(icon, Color.FromArgb(22, 119, 255)));
        owner.Disposed += (_, _) => images.Dispose();
        return images;
    }
}
