using ModernUI.Localization;

namespace ModernUI.WinForms;

/// <summary>定义现代 WinForms 控件使用的语义颜色、尺寸和动画参数。</summary>
public sealed record ModernTheme
{
    /// <summary>Ant Design 风格浅色主题。</summary>
    public static ModernTheme Light { get; } = new();

    /// <summary>使用 Windows 系统色的高对比度主题。</summary>
    public static ModernTheme HighContrast { get; } = new()
    {
        IsDark = SystemColors.Window.GetBrightness() < .5f,
        Background = SystemColors.Window,
        Container = SystemColors.Window,
        Elevated = SystemColors.Window,
        Control = SystemColors.Window,
        ControlHover = SystemColors.Highlight,
        Border = SystemColors.WindowText,
        BorderSecondary = SystemColors.GrayText,
        Text = SystemColors.WindowText,
        TextSecondary = SystemColors.WindowText,
        TextDisabled = SystemColors.GrayText,
        Primary = SystemColors.Highlight,
        PrimaryHover = SystemColors.HotTrack,
        PrimaryActive = SystemColors.Highlight,
        PrimaryBackground = SystemColors.Highlight,
        Error = SystemColors.Highlight,
        Success = SystemColors.Highlight,
        Warning = SystemColors.Highlight,
        OverlaySurface = SystemColors.Info,
        OverlayBorder = SystemColors.InfoText,
        ToolTipText = SystemColors.InfoText,
        ScrollThumb = SystemColors.ScrollBar,
        ScrollThumbHover = SystemColors.Highlight,
        ScrollThumbDragging = SystemColors.Highlight,
        PrimarySurface = SystemColors.Window,
        SuccessSurface = SystemColors.Window,
        WarningSurface = SystemColors.Window,
        ErrorSurface = SystemColors.Window,
        FocusRing = SystemColors.Highlight
    };

    /// <summary>Ant Design 风格深色主题。</summary>
    public static ModernTheme Dark { get; } = new()
    {
        IsDark = true,
        Background = Color.FromArgb(20, 20, 20),
        Container = Color.FromArgb(31, 31, 31),
        Elevated = Color.FromArgb(40, 40, 40),
        Control = Color.FromArgb(36, 36, 36),
        ControlHover = Color.FromArgb(48, 48, 48),
        Border = Color.FromArgb(66, 66, 66),
        BorderSecondary = Color.FromArgb(48, 48, 48),
        Text = Color.FromArgb(217, 217, 217),
        TextSecondary = Color.FromArgb(166, 166, 166),
        TextDisabled = Color.FromArgb(89, 89, 89),
        Primary = Color.FromArgb(22, 104, 220),
        PrimaryHover = Color.FromArgb(60, 137, 232),
        PrimaryActive = Color.FromArgb(21, 84, 173),
        PrimaryBackground = Color.FromArgb(17, 26, 44),
        PrimarySurface = Color.FromArgb(17, 26, 44),
        Error = Color.FromArgb(220, 68, 70),
        Success = Color.FromArgb(73, 170, 25),
        Warning = Color.FromArgb(216, 150, 20),
        OverlaySurface = Color.FromArgb(40, 40, 40),
        OverlayBorder = Color.FromArgb(72, 255, 255, 255),
        ToolTipText = Color.White,
        ScrollThumb = Color.FromArgb(110, 166, 166, 166),
        ScrollThumbHover = Color.FromArgb(141, 166, 166, 166),
        ScrollThumbDragging = Color.FromArgb(172, 166, 166, 166),
        SuccessSurface = Color.FromArgb(22, 35, 18),
        WarningSurface = Color.FromArgb(43, 33, 17),
        ErrorSurface = Color.FromArgb(42, 18, 21)
    };

    public bool IsDark { get; init; }
    public Color Background { get; init; } = Color.FromArgb(245, 247, 250);
    public Color Container { get; init; } = Color.White;
    public Color Elevated { get; init; } = Color.White;
    public Color Control { get; init; } = Color.White;
    public Color ControlHover { get; init; } = Color.FromArgb(250, 250, 250);
    public Color Border { get; init; } = Color.FromArgb(217, 217, 217);
    public Color BorderSecondary { get; init; } = Color.FromArgb(240, 240, 240);
    public Color Text { get; init; } = Color.FromArgb(31, 31, 31);
    public Color TextSecondary { get; init; } = Color.FromArgb(89, 89, 89);
    public Color TextDisabled { get; init; } = Color.FromArgb(191, 191, 191);
    public Color Primary { get; init; } = Color.FromArgb(22, 119, 255);
    public Color PrimaryHover { get; init; } = Color.FromArgb(64, 150, 255);
    public Color PrimaryActive { get; init; } = Color.FromArgb(9, 88, 217);
    public Color PrimaryBackground { get; init; } = Color.FromArgb(230, 244, 255);
    public Color Error { get; init; } = Color.FromArgb(255, 77, 79);
    public Color Success { get; init; } = Color.FromArgb(82, 196, 26);
    public Color Warning { get; init; } = Color.FromArgb(250, 173, 20);
    public Color OverlaySurface { get; init; } = Color.FromArgb(38, 38, 38);
    public Color OverlayBorder { get; init; } = Color.FromArgb(72, 255, 255, 255);
    public Color ToolTipText { get; init; } = Color.White;
    public Color ScrollThumb { get; init; } = Color.FromArgb(110, 89, 89, 89);
    public Color ScrollThumbHover { get; init; } = Color.FromArgb(141, 89, 89, 89);
    public Color ScrollThumbDragging { get; init; } = Color.FromArgb(172, 89, 89, 89);
    public Color PrimarySurface { get; init; } = Color.FromArgb(230, 244, 255);
    public Color SuccessSurface { get; init; } = Color.FromArgb(246, 255, 237);
    public Color WarningSurface { get; init; } = Color.FromArgb(255, 251, 230);
    public Color ErrorSurface { get; init; } = Color.FromArgb(255, 242, 240);
    public Color FocusRing { get; init; } = Color.Empty;
    public int ControlHeight { get; init; } = 34;
    public int ControlHeightLarge { get; init; } = 40;
    public int PropertyRowHeight { get; init; } = 44;
    public int Radius { get; init; } = 6;
    public int Spacing { get; init; } = 8;
    public int AnimationDuration { get; init; } = 160;
}

/// <summary>提供框架级默认配置。</summary>
public static class ModernUiSettings
{
    /// <summary>获取或设置新控件默认使用的主题。</summary>
    public static ModernTheme DefaultTheme { get; set; } = ModernTheme.Light;
    /// <summary>获取或设置是否启用微交互动画。</summary>
    public static bool AnimationsEnabled { get; set; } = true;
    /// <summary>获取或设置是否遵循 Windows 客户区动画偏好；关闭时用于无动画和远程桌面环境。</summary>
    public static bool RespectSystemAnimationPreference { get; set; } = true;
    /// <summary>获取或设置系统启用高对比度时是否自动使用系统色主题。</summary>
    public static bool RespectSystemHighContrast { get; set; } = true;
    /// <summary>获取框架在应用设置与系统偏好合并后的实际动画状态。</summary>
    public static bool EffectiveAnimationsEnabled => AnimationsEnabled &&
        (!RespectSystemAnimationPreference || ModernCompatibility.IsClientAreaAnimationEnabled);
    /// <summary>获取或设置是否合并同一 Owner 上内容和语义完全相同的反馈。</summary>
    public static bool MergeDuplicateFeedback { get; set; } = true;
    /// <summary>获取或设置同一 Owner 最多同时显示的顶部消息数。</summary>
    public static int MaximumVisibleMessages { get; set; } = 5;
    /// <summary>获取或设置同一 Owner 最多同时显示的右下角通知数。</summary>
    public static int MaximumVisibleNotifications { get; set; } = 5;

    /// <summary>递归向现代控件应用本地化上下文，不改写用户输入或业务数据。</summary>
    public static void ApplyLocalization(Control root, ILocalizationContext context)
    {
        ModernCompatibility.ThrowIfNull(root, nameof(root));
        ModernCompatibility.ThrowIfNull(context, nameof(context));
        if (root is ModernControl modern) modern.LocalizationContext = context;
        foreach (Control child in root.Controls) ApplyLocalization(child, context);
    }

    /// <summary>递归向现代控件应用主题，并同步普通容器背景。</summary>
    public static void ApplyTheme(Control root, ModernTheme theme)
    {
        ModernCompatibility.ThrowIfNull(root, nameof(root));
        ModernCompatibility.ThrowIfNull(theme, nameof(theme));
        if (RespectSystemHighContrast && SystemInformation.HighContrast) theme = ModernTheme.HighContrast;
        if (root is ModernContextMenu contextMenu)
        {
            contextMenu.Theme = theme;
        }
        else if (!ModernThemeControlAdapter.TryApplyTheme(root, theme))
        {
            root.ForeColor = theme.Text;
            root.BackColor = root switch
            {
                Form or TabPage => theme.Background,
                Label or TableLayoutPanel or FlowLayoutPanel or Panel => Color.Transparent,
                ListBox or TextBoxBase or ComboBox => theme.Control,
                _ => theme.Background
            };
        }

        // ModernControl can itself be a container. Continue walking its children so an existing
        // control tree receives the new theme instead of only updating the outermost container.
        foreach (Control child in root.Controls) ApplyTheme(child, theme);
        if (root.ContextMenuStrip is ModernContextMenu menu) menu.Theme = theme;
        if (root is Form form)
        {
            ModernMessage.Refresh(form);
            ModernNotification.Refresh(form);
        }
    }
}
