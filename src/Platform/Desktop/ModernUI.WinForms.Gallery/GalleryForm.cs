using ModernUI.WinForms;

namespace ModernUI.WinForms.Gallery;

/// <summary>Gallery 的分类入口；具体控件演示在独立分类窗口中展示。</summary>
internal sealed partial class GalleryForm : GalleryDpiForm
{
    private readonly Label _themeLabel = new();
    private readonly FlowLayoutPanel _categoryGrid = new();
    private readonly List<DemoCategoryForm> _openDemos = [];
    private ModernSwitch? _themeSwitch;
    private ModernTheme _theme;

    public GalleryForm() : this(ModernTheme.Light) { }

    internal GalleryForm(ModernTheme initialTheme)
    {
        _theme = initialTheme ?? throw new ArgumentNullException(nameof(initialTheme));
        InitializeComponent();
        Text = "ModernUI WinForms · 控件案例";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 720);
        MinimumSize = new Size(980, 620);
        GalleryApplication.ConfigureLogicalSize(this, new Size(1180, 720), new Size(980, 620));
        Font = new Font("Microsoft YaHei UI", 9F);

        var root = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(CreateIntroduction(), 0, 1);
        root.Controls.Add(CreateCategoryGrid(), 0, 2);
        Controls.Add(root);
        root.SendToBack();
        ApplyCurrentTheme();
    }

    private Control CreateHeader()
    {
        var header = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(24, 12, 20, 10) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        header.Controls.Add(new Label
        {
            Text = "ModernUI WinForms 控件案例",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font.FontFamily, 13F, FontStyle.Bold)
        }, 0, 0);
        _themeLabel.Dock = DockStyle.Fill;
        _themeLabel.TextAlign = ContentAlignment.MiddleRight;
        header.Controls.Add(_themeLabel, 1, 0);

        _themeSwitch = new ModernSwitch
        {
            Anchor = AnchorStyles.None,
            AccessibleName = "切换深色主题",
            Checked = _theme.IsDark
        };
        _themeSwitch.CheckedChanged += (_, _) => RevealTheme(
            _themeSwitch.Checked ? ModernTheme.Dark : ModernTheme.Light);
        header.Controls.Add(_themeSwitch, 2, 0);
        return header;
    }

    private Control CreateIntroduction()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 4, 28, 8) };
        panel.Controls.Add(new Label
        {
            Text = "按使用场景浏览控件。点击分类后打开独立 Demo，可操作该分类的状态、事件与组合用法。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font.FontFamily, 10F)
        });
        return panel;
    }

    private Control CreateCategoryGrid()
    {
        _categoryGrid.Dock = DockStyle.Fill;
        _categoryGrid.AutoScroll = true;
        _categoryGrid.WrapContents = true;
        _categoryGrid.Padding = new Padding(22, 8, 12, 22);
        _categoryGrid.Resize += (_, _) => ResizeCategoryCards();
        foreach (var descriptor in GalleryDemoCatalog.All) _categoryGrid.Controls.Add(CreateCategoryCard(descriptor));
        return _categoryGrid;
    }

    private Control CreateCategoryCard(GalleryDemoDescriptor descriptor) =>
        new GalleryCategoryCard(descriptor, Font, () => OpenDemo(descriptor));

    private void ResizeCategoryCards()
    {
        if (_categoryGrid.ClientSize.Width <= 0) return;
        var dpi = _categoryGrid.DeviceDpi > 0 ? _categoryGrid.DeviceDpi : 96;
        var scale = dpi / 96d;
        int S(int logical) => (int)Math.Round(logical * scale);
        var logicalWidth = _categoryGrid.ClientSize.Width / scale;
        var columns = logicalWidth >= 1080 ? 3 : logicalWidth >= 700 ? 2 : 1;
        var available = _categoryGrid.ClientSize.Width - _categoryGrid.Padding.Horizontal - S(20);
        var width = Math.Max(S(300), available / columns - S(18));
        foreach (Control card in _categoryGrid.Controls) card.Width = width;
    }

    internal void OpenDemo(GalleryDemoDescriptor descriptor)
    {
        var demo = new DemoCategoryForm(descriptor, _theme);
        PositionDemoOnOwnerMonitor(demo);
        demo.FormClosed += (_, _) => _openDemos.Remove(demo);
        _openDemos.Add(demo);
        // The demos are tracked explicitly, so native Win32 ownership is unnecessary. Assigning a
        // PMv2 owner created on another DPI can make WinForms reparent native input HWNDs while the
        // child Form is still being shown (ERROR_INVALID_WINDOW_HANDLE / 1400 at 96 DPI).
        demo.Show();
    }

    private void PositionDemoOnOwnerMonitor(Form demo)
    {
        // CenterParent determines the native position during WM_SHOWWINDOW. On a mixed-DPI desktop,
        // that can move the Form while WinForms is recursively creating its native child HWNDs.
        // Choose the final monitor position before any handle exists so every child gets a stable parent.
        var scale = DeviceDpi > 0 ? DeviceDpi / 96d : 1d;
        var expectedWidth = (int)Math.Round(demo.Width * scale);
        var expectedHeight = (int)Math.Round(demo.Height * scale);
        var workingArea = Screen.FromControl(this).WorkingArea;
        var x = Left + (Width - expectedWidth) / 2;
        var y = Top + (Height - expectedHeight) / 2;
        x = Math.Max(workingArea.Left, Math.Min(x, workingArea.Right - expectedWidth));
        y = Math.Max(workingArea.Top, Math.Min(y, workingArea.Bottom - expectedHeight));
        demo.StartPosition = FormStartPosition.Manual;
        demo.Location = new Point(x, y);
    }

    internal void ScheduleThemeFrameProbe()
    {
        Shown += (_, _) =>
        {
            var timer = new System.Windows.Forms.Timer { Interval = 2200 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();
                Text = "THEME_FRAME_PROBE_RUNNING";
                RevealTheme(_theme.IsDark ? ModernTheme.Light : ModernTheme.Dark);
                var completion = new System.Windows.Forms.Timer { Interval = 500 };
                completion.Tick += (_, _) =>
                {
                    completion.Stop();
                    completion.Dispose();
                    Text = $"THEME_FRAME_PROBE_OK theme={(_theme.IsDark ? "dark" : "light")}";
                };
                completion.Start();
            };
            timer.Start();
        };
    }

    private void RevealTheme(ModernTheme next)
    {
        if (_themeSwitch is null || ReferenceEquals(_theme, next)) return;
        var origin = PointToClient(_themeSwitch.PointToScreen(
            new Point(_themeSwitch.Width / 2, _themeSwitch.Height / 2)));
        ModernThemeTransition.Reveal(this, origin, () =>
        {
            _theme = next;
            ApplyCurrentTheme();
            foreach (var demo in _openDemos.ToArray())
            {
                if (demo.IsDisposed) _openDemos.Remove(demo);
                else demo.ApplyTheme(_theme);
            }
        });
    }

    private void ApplyCurrentTheme()
    {
        _themeLabel.Text = _theme.IsDark ? "深色模式" : "浅色模式";
        ModernUiSettings.DefaultTheme = _theme;
        ModernUiSettings.ApplyTheme(this, _theme);
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
}
