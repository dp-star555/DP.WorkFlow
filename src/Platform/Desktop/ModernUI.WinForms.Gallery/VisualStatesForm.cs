using System.Globalization;
using ModernUI.Localization;
using ModernUI.WinForms;

namespace ModernUI.WinForms.Gallery;

/// <summary>Provides a deterministic control-state surface for visual regression checks.</summary>
internal sealed partial class VisualStatesForm : Form
{
    private readonly Label _titleLabel = new();
    private readonly Label _themeLabel = new();
    private readonly ModernSwitch _themeSwitch = new();
    private readonly ModernButton _localeButton = new() { Width = 88, Text = "English", ButtonType = ModernButtonType.Text };
    private readonly ILocalizationManager _localizationManager;
    private readonly List<Action> _galleryTextBindings = [];
    private readonly int _initialScrollOffset;
    private FlowLayoutPanel? _stateMatrix;
    private ModernScrollView? _stateViewport;
    private ModernTheme _theme;

    public VisualStatesForm(ModernTheme initialTheme, int initialScrollOffset = 0, bool english = false)
    {
        _theme = initialTheme;
        AutoScaleMode = AutoScaleMode.None;
        _localizationManager = GalleryLocalization.CreateManager(CultureInfo.GetCultureInfo(english ? "en-US" : "zh-CN"));
        _initialScrollOffset = Math.Max(0, initialScrollOffset);
        Text = "ModernUI Visual States";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 640);
        MinimumSize = new Size(980, 640);
        GalleryApplication.ConfigureLogicalSize(this, new Size(1180, 640), new Size(980, 640));
        Font = new Font("Microsoft YaHei UI", 9F);

        var root = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(CreateStateMatrix(), 0, 1);
        Controls.Add(root);

        Shown += (_, _) =>
        {
            UpdateDpiCaption();
            if (_stateViewport is not null)
                BeginInvoke(() => _stateViewport.ScrollOffset = (int)Math.Round(_initialScrollOffset * DeviceDpi / 96d));
        };
        DpiChanged += (_, eventArgs) =>
        {
            var logicalScrollOffset = _stateViewport is null || eventArgs.DeviceDpiOld <= 0
                ? _initialScrollOffset
                : _stateViewport.ScrollOffset * 96d / eventArgs.DeviceDpiOld;
            UpdateDpiCaption();
            if (_stateViewport is not null)
                BeginInvoke(() => _stateViewport.ScrollOffset = (int)Math.Round(logicalScrollOffset * DeviceDpi / 96d));
        };
        ModernUiSettings.ApplyLocalization(this, _localizationManager.Context);
        _localeButton.Text = G(english ? "SwitchToChinese" : "SwitchToEnglish");
        BindGalleryText("ThemeSwitchAccessible", text => _themeSwitch.AccessibleName = text);
        _localeButton.Click += async (_, _) =>
        {
            var nextEnglish = _localizationManager.Context.Current.Culture.Name != "en-US";
            await _localizationManager.ChangeLocaleAsync(CultureInfo.GetCultureInfo(nextEnglish ? "en-US" : "zh-CN"));
            ApplyGalleryTexts();
            _localeButton.Text = G(nextEnglish ? "SwitchToChinese" : "SwitchToEnglish");
            UpdateDpiCaption();
            ApplyCurrentTheme();
        };
        _themeSwitch.Checked = _theme.IsDark;
        _themeSwitch.CheckedChanged += (_, _) =>
        {
            _theme = _themeSwitch.Checked ? ModernTheme.Dark : ModernTheme.Light;
            ApplyCurrentTheme();
        };
        ApplyCurrentTheme();
    }

    private Control CreateHeader()
    {
        var header = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            Padding = new Padding(24, 12, 24, 10)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));

        _titleLabel.Text = string.Empty;
        _titleLabel.Dock = DockStyle.Fill;
        _titleLabel.Font = new Font(Font.FontFamily, 12F, FontStyle.Bold);
        _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        _themeLabel.Dock = DockStyle.Fill;
        _themeLabel.TextAlign = ContentAlignment.MiddleRight;
        _themeLabel.Tag = "secondary";
        _themeSwitch.Anchor = AnchorStyles.None;

        header.Controls.Add(_titleLabel, 0, 0);
        header.Controls.Add(_localeButton, 1, 0);
        header.Controls.Add(_themeLabel, 2, 0);
        header.Controls.Add(_themeSwitch, 3, 0);
        return header;
    }

    private sealed record VisualChoice(int Id, string Name, string IconKey = "info");

}
