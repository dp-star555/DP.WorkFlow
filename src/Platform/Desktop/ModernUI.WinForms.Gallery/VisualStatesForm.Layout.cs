using System.Globalization;
using ModernUI.Localization;
using ModernUI.WinForms;

namespace ModernUI.WinForms.Gallery;

internal sealed partial class VisualStatesForm
{
    private Control CreateBand(string titleKey, int titleWidth, params Control[] samples)
    {
        var band = new BufferedTableLayoutPanel
        {
            Width = 1110,
            Height = Math.Max(98, samples.Length == 0 ? 98 : samples.Max(sample => sample.Height + 8)),
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 8)
        };
        band.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, titleWidth));
        band.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var heading = new Label
        {
            Text = string.Empty,
            Dock = DockStyle.Fill,
            Font = new Font(SystemFonts.MessageBoxFont ?? Control.DefaultFont, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        BindGalleryText(titleKey, text => heading.Text = text);
        var states = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };
        states.Controls.AddRange(samples);
        band.Controls.Add(heading, 0, 0);
        band.Controls.Add(states, 1, 0);
        return band;
    }

    private Control Sample(string captionKey, Control control, int width = 156)
    {
        var sample = new BufferedTableLayoutPanel
        {
            Width = width,
            Height = Math.Max(90, control.Height + 28),
            RowCount = 2,
            ColumnCount = 1,
            Margin = new Padding(0, 0, 8, 0)
        };
        sample.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        sample.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var label = new Label
        {
            Text = string.Empty,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Tag = "secondary"
        };
        BindGalleryText("Sample." + captionKey, text => label.Text = text);
        control.Anchor = AnchorStyles.Left;
        sample.Controls.Add(label, 0, 0);
        sample.Controls.Add(control, 0, 1);
        return sample;
    }
}
