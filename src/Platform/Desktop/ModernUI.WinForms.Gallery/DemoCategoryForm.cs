using System.ComponentModel;
using ModernPropertyGrid.WinForms;
using ModernUI.WinForms;

namespace ModernUI.WinForms.Gallery;

/// <summary>展示单一使用场景中的完整控件状态和可操作功能。</summary>
internal sealed partial class DemoCategoryForm : GalleryDpiForm
{
    private readonly GalleryDemoDescriptor _descriptor;
    private readonly VerticalSectionPanel _content = new();
    private readonly ModernScrollView _viewport = new() { Dock = DockStyle.Fill };
    private readonly ModernStatusBar _statusBar = new();
    private readonly ToolStripStatusLabel _activityStatus = new("就绪");
    private readonly ToolStripStatusLabel _environmentStatus = new();
    private readonly List<IDisposable> _ownedResources = [];
    private bool _activityReady;
    private ModernTheme _theme;

    public DemoCategoryForm(GalleryDemoDescriptor descriptor, ModernTheme theme)
    {
        _descriptor = descriptor;
        _theme = theme;
        AutoScaleMode = AutoScaleMode.None;
        Text = $"{descriptor.Title} · ModernUI Demo";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(960, 680);
        MinimumSize = new Size(760, 560);
        GalleryApplication.ConfigureLogicalSize(this, new Size(960, 680), new Size(760, 560));
        Font = new Font("Microsoft YaHei UI", 9F);

        var root = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.Controls.Add(CreateHeader(), 0, 0);

        _content.Padding = new Padding(22, 8, 22, 22);
        _viewport.Content = _content;
        root.Controls.Add(_viewport, 0, 1);
        _statusBar.Dock = DockStyle.Fill;
        _statusBar.Items.Add(_activityStatus);
        _statusBar.AddSpring();
        _statusBar.Items.Add(_environmentStatus);
        root.Controls.Add(_statusBar, 0, 2);
        Controls.Add(root);

        BuildDemo();
        ApplyTheme(theme);
        Shown += (_, _) =>
        {
            _activityReady = true;
            _activityStatus.Text = "就绪 · 请操作控件查看公开事件和值";
            UpdateEnvironmentStatus();
        };
        DpiChanged += (_, _) => BeginInvoke(UpdateEnvironmentStatus);
    }

    private Control CreateHeader() =>
        new DemoHeaderPanel(_descriptor.Title, _descriptor.Description) { Dock = DockStyle.Fill };

    internal void SetInitialScrollOffset(int offset)
    {
        if (offset <= 0) return;
        Shown += (_, _) => BeginInvoke((Action)(() => _viewport.ScrollOffset = offset));
    }

    private void BuildDemo()
    {
        switch (_descriptor.Category)
        {
            case GalleryDemoCategory.Actions: BuildActions(); break;
            case GalleryDemoCategory.Inputs: BuildInputs(); break;
            case GalleryDemoCategory.Selection: BuildSelection(); break;
            case GalleryDemoCategory.DataDisplay: BuildDataDisplay(); break;
            case GalleryDemoCategory.NavigationAndLayout: BuildNavigation(); break;
            case GalleryDemoCategory.FeedbackAndOverlays: BuildFeedback(); break;
            case GalleryDemoCategory.DateAndTime: BuildDateTime(); break;
            case GalleryDemoCategory.PropertyGrid: BuildPropertyGrid(); break;
            case GalleryDemoCategory.BusinessScenario: BuildBusinessScenario(); break;
        }
    }

    private ModernButton DemoButton(string text, Action action)
    {
        var button = new ModernButton { Text = text, Width = 108 };
        button.Click += (_, _) => action();
        return button;
    }

    private T Own<T>(T resource) where T : IDisposable
    {
        _ownedResources.Add(resource);
        return resource;
    }

    private void Report(string message)
    {
        if (!_activityReady) return;
        _activityStatus.Text = $"事件 · {message}";
        _activityStatus.ToolTipText = message;
    }

    private void UpdateEnvironmentStatus() =>
        _environmentStatus.Text = $"{_descriptor.Category} · {DeviceDpi} DPI";

    private static Control Column(params Control[] controls) => new DemoControlColumn(controls);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var resource in _ownedResources.AsEnumerable().Reverse()) resource.Dispose();
            _ownedResources.Clear();
        }
        base.Dispose(disposing);
    }

    private void AddSection(string title, string description, Control content)
    {
        var section = new DemoSectionPanel(title, description)
        {
            Width = 860,
            Height = Math.Max(150, content.Height + 92),
            Margin = new Padding(0, 0, 0, 16),
            Padding = new Padding(18, 72, 18, 16)
        };
        section.Controls.Add(content);
        content.Location = new Point(18, 72);
        if (content.Dock == DockStyle.None && content.Width > section.ClientSize.Width - 36)
            content.Width = section.ClientSize.Width - 36;
        _content.Controls.Add(section);
        if (_viewport.IsHandleCreated) _viewport.PerformLayout();
    }

    private static Control Row(params Control[] controls) => new DemoControlRow(controls);

    private static Control FormRows(params (string Label, Control Control)[] rows) => new DemoFormRows(rows);

    public void ApplyTheme(ModernTheme theme)
    {
        _theme = theme;
        ModernUiSettings.ApplyTheme(this, theme);
        ApplyLabelColors(this);
        Invalidate(true);
    }

    private void ApplyLabelColors(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Label label)
            {
                label.BackColor = Color.Transparent;
                label.ForeColor = Equals(label.Tag, "secondary") ? _theme.TextSecondary : _theme.Text;
            }
            ApplyLabelColors(child);
        }
    }

    /// <summary>Draws the static page heading without a nested panel and label HWND.</summary>
    private sealed class DemoHeaderPanel : ModernControl
    {
        private readonly string _title;
        private readonly string _description;
        private Font? _headerFont;

        public DemoHeaderPanel(string title, string description)
        {
            _title = title;
            _description = description;
            AccessibleRole = AccessibleRole.Grouping;
            AccessibleName = title;
            AccessibleDescription = description;
            TabStop = false;
            RecreateHeaderFont();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            RecreateHeaderFont();
        }

        protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
        {
            canvas.Fill(Theme.Background, bounds, 0);
            var left = ScaleLogical(28);
            var width = Math.Max(0, Width - left - ScaleLogical(24));
            canvas.DrawText(_title, _headerFont ?? Font, Theme.Text,
                new Rectangle(left, ScaleLogical(10), width, ScaleLogical(26)), ContentAlignment.MiddleLeft);
            canvas.DrawText(_description, _headerFont ?? Font, Theme.Text,
                new Rectangle(left, ScaleLogical(34), width, ScaleLogical(28)), ContentAlignment.MiddleLeft);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _headerFont?.Dispose();
            base.Dispose(disposing);
        }

        private void RecreateHeaderFont()
        {
            _headerFont?.Dispose();
            _headerFont = new Font(Font.FontFamily, 11F, FontStyle.Bold);
        }
    }

    /// <summary>Draws static section chrome and text without creating two extra label HWNDs.</summary>
    private sealed class DemoSectionPanel : ModernControl
    {
        private readonly string _title;
        private readonly string _description;
        private Font? _titleFont;

        public DemoSectionPanel(string title, string description)
        {
            _title = title;
            _description = description;
            AccessibleRole = AccessibleRole.Grouping;
            AccessibleName = title;
            AccessibleDescription = description;
            TabStop = false;
            RecreateTitleFont();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            RecreateTitleFont();
        }

        protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
        {
            var inset = ScaleLogical(1f);
            var surface = RectangleF.Inflate(bounds, -inset, -inset);
            canvas.Fill(Theme.Container, surface, ScaleLogical(8));
            canvas.Draw(Theme.BorderSecondary, ScaleLogical(1f), surface, ScaleLogical(8));
            canvas.DrawText(_title, _titleFont ?? Font, Theme.Text,
                new Rectangle(ScaleLogical(18), ScaleLogical(12),
                    Math.Max(0, Width - ScaleLogical(36)), ScaleLogical(24)), ContentAlignment.MiddleLeft);
            canvas.DrawText(_description, Font, Theme.TextSecondary,
                new Rectangle(ScaleLogical(18), ScaleLogical(38),
                    Math.Max(0, Width - ScaleLogical(36)), ScaleLogical(24)), ContentAlignment.MiddleLeft);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _titleFont?.Dispose();
            base.Dispose(disposing);
        }

        private void RecreateTitleFont()
        {
            _titleFont?.Dispose();
            _titleFont = new Font(Font.FontFamily, 10F, FontStyle.Bold);
        }
    }

    /// <summary>Positions interactive controls horizontally without FlowLayoutPanel auto-size passes.</summary>
    private sealed class DemoControlRow : ModernControl
    {
        public DemoControlRow(IEnumerable<Control> controls)
        {
            TabStop = false;
            var width = 0;
            var height = 0;
            foreach (var control in controls)
            {
                control.Margin = new Padding(0, 4, 12, 4);
                Controls.Add(control);
                width += control.Width + control.Margin.Horizontal;
                height = Math.Max(height, control.Height + control.Margin.Vertical);
            }
            Size = new Size(Math.Max(0, width), Math.Max(0, height));
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            var left = 0;
            foreach (Control control in Controls)
            {
                control.Location = new Point(left + control.Margin.Left, control.Margin.Top);
                left = control.Right + control.Margin.Right;
            }
        }

        protected override void RenderContent(GdiCanvas canvas, Rectangle bounds) { }
    }

    /// <summary>Positions interactive scenarios vertically without recursive preferred-size measurement.</summary>
    private sealed class DemoControlColumn : ModernControl
    {
        public DemoControlColumn(IEnumerable<Control> controls)
        {
            TabStop = false;
            var width = 0;
            var height = 0;
            foreach (var control in controls)
            {
                control.Margin = new Padding(0, 0, 0, 10);
                Controls.Add(control);
                width = Math.Max(width, control.Width + control.Margin.Horizontal);
                height += control.Height + control.Margin.Vertical;
            }
            Size = new Size(width, Math.Max(0, height));
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            var top = 0;
            foreach (Control control in Controls)
            {
                control.Location = new Point(control.Margin.Left, top + control.Margin.Top);
                top = control.Bottom + control.Margin.Bottom;
            }
        }

        protected override void RenderContent(GdiCanvas canvas, Rectangle bounds) { }
    }

    /// <summary>Draws form labels and keeps only native/interactive editors in the HWND tree.</summary>
    private sealed class DemoFormRows : ModernControl
    {
        private readonly (string Label, Control Control)[] _rows;

        public DemoFormRows((string Label, Control Control)[] rows)
        {
            _rows = rows;
            TabStop = false;
            var editorWidth = 0;
            var height = 0;
            foreach (var row in rows)
            {
                row.Control.Margin = new Padding(0, 4, 0, 4);
                if (string.IsNullOrWhiteSpace(row.Control.AccessibleName)) row.Control.AccessibleName = row.Label;
                Controls.Add(row.Control);
                editorWidth = Math.Max(editorWidth, row.Control.Width);
                height += Math.Max(34, row.Control.Height + row.Control.Margin.Vertical);
            }
            Size = new Size(100 + editorWidth, height);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            var editorLeft = ScaleLogical(100);
            var top = 0;
            foreach (var row in _rows)
            {
                var rowHeight = Math.Max(ScaleLogical(34), row.Control.Height + row.Control.Margin.Vertical);
                row.Control.Location = new Point(editorLeft + row.Control.Margin.Left,
                    top + row.Control.Margin.Top);
                top += rowHeight;
            }
        }

        protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
        {
            var top = 0;
            foreach (var row in _rows)
            {
                var rowHeight = Math.Max(ScaleLogical(34), row.Control.Height + row.Control.Margin.Vertical);
                canvas.DrawText(row.Label, Font, Theme.Text,
                    new Rectangle(0, top, ScaleLogical(92), rowHeight), ContentAlignment.MiddleLeft);
                top += rowHeight;
            }
        }
    }

    /// <summary>Owns section width and vertical placement without recursively measuring section contents.</summary>
    private sealed class VerticalSectionPanel : Panel
    {
        protected override void OnLayout(LayoutEventArgs levent)
        {
            var sectionWidth = ResolveSectionWidth();
            var top = Padding.Top;
            foreach (Control section in Controls)
            {
                top += section.Margin.Top;
                var bounds = new Rectangle(Padding.Left + section.Margin.Left, top,
                    Math.Max(0, sectionWidth - section.Margin.Horizontal), section.Height);
                if (section.Bounds != bounds) section.Bounds = bounds;
                top = bounds.Bottom + section.Margin.Bottom;
            }
            base.OnLayout(levent);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var height = Padding.Top + Padding.Bottom;
            foreach (Control section in Controls)
                height += section.Margin.Vertical + section.Height;
            return new Size(Math.Max(0, proposedSize.Width), height);
        }

        private int ResolveSectionWidth()
        {
            var dpi = DeviceDpi > 0 ? DeviceDpi : 96;
            var minimumWidth = (int)Math.Round(680d * dpi / 96d);
            var trailingGutter = (int)Math.Round(16d * dpi / 96d);
            return Math.Max(minimumWidth, ClientSize.Width - Padding.Horizontal - trailingGutter);
        }
    }
}
