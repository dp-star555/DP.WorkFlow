using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 工作台主题：当前调色板（与 DP.Foundation 操作台同一套 5 个内置主题），以及由它推导出的 ModernUI 主题和画布配色。
/// 页面、弹窗和画布都从这里取颜色，不写死颜色；换主题只调用 <see cref="Use(WorkflowThemePalette)"/>。
/// 挂进操作台后，由操作台在切换主题时调用 <see cref="Use(WorkflowThemePalette)"/>，Workflow 页面跟随。
/// </summary>
/// <remarks>只在界面线程使用。</remarks>
public static class WorkflowWinFormsTheme
{
    private static WorkflowThemePalette _palette = WorkflowThemePalette.Resolve(null);
    private static ModernTheme _modern = Derive(_palette);
    private static WorkflowCanvasPalette _canvas = WorkflowCanvasPalette.From(_palette);

    /// <summary>主题切换后触发（界面线程）。已打开的工作台、节点窗口和画布据此重新着色。</summary>
    public static event EventHandler? Changed;

    /// <summary>当前调色板。</summary>
    public static WorkflowThemePalette Palette => _palette;

    /// <summary>当前 ModernUI 主题：系统高对比度优先，其次当前调色板。</summary>
    public static ModernTheme Modern => HighContrast ? ModernTheme.HighContrast : _modern;

    /// <summary>当前画布配色。</summary>
    public static WorkflowCanvasPalette Canvas => _canvas;

    /// <summary>工作区是否为深色。</summary>
    public static bool IsDark => Modern.IsDark;

    /// <summary>切换主题并通知已打开的界面。</summary>
    /// <param name="palette">新主题。</param>
    public static void Use(WorkflowThemePalette palette)
    {
        Guard.NotNull(palette);
        if (ReferenceEquals(palette, _palette)) return;
        _palette = palette;
        _modern = Derive(palette);
        _canvas = WorkflowCanvasPalette.From(palette);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>按标识切换内置主题（找不到时用默认主题）。</summary>
    /// <param name="id">主题标识，如 blue、night。</param>
    public static void Use(string? id) => Use(WorkflowThemePalette.Resolve(id));

    /// <summary>为控件树着色：ModernUI 控件设置主题，原生控件（按钮、列表表头、下拉框等）按当前主题自绘。</summary>
    /// <param name="root">根控件。</param>
    /// <param name="followAddedControls">之后动态加入的子控件也着色。</param>
    public static void Apply(Control root, bool followAddedControls = false)
    {
        Guard.NotNull(root);
        WorkflowWinFormsStyle.Apply(root, followAddedControls);
    }

    /// <summary>
    /// 着色并跟随主题：立即为 <paramref name="root"/> 着色，之后每次切换主题重新着色并调用 <paramref name="reapply"/>
    /// （用于自己画的图标、自绘颜色等着色规则管不到的部分）；控件释放时自动取消。
    /// </summary>
    /// <param name="root">根控件（工作台、窗口、页面）。</param>
    /// <param name="reapply">切换主题后额外要做的事；可为空。</param>
    /// <param name="followAddedControls">之后动态加入的子控件也着色。</param>
    public static void Follow(Control root, Action? reapply = null, bool followAddedControls = false)
    {
        Guard.NotNull(root);
        Apply(root, followAddedControls);
        reapply?.Invoke();
        void OnChanged(object? sender, EventArgs e)
        {
            if (root.IsDisposed) return;
            root.SuspendLayout();
            try
            {
                Apply(root, followAddedControls);
                reapply?.Invoke();
            }
            finally
            {
                root.ResumeLayout(true);
            }
            root.Invalidate(true);
        }
        Changed += OnChanged;
        root.Disposed += (_, _) => Changed -= OnChanged;
    }

    /// <summary>旧名称，等同 <see cref="Apply"/>；颜色已跟随当前主题，不再固定为深色。</summary>
    /// <param name="root">根控件。</param>
    /// <param name="followAddedControls">之后动态加入的子控件也着色。</param>
    public static void ApplyDark(Control root, bool followAddedControls = false) => Apply(root, followAddedControls);

    /// <summary>表格主题：数据行与卡片同底，表头与隔行取次一级表面色（与 Foundation 相同）。</summary>
    /// <param name="theme">卡片所用主题。</param>
    public static ModernTheme Table(ModernTheme theme) => HighContrast ? theme : theme with
    {
        Control = theme.Container,
        Container = Solid(_palette.Colors.SurfaceAlt)
    };

    /// <summary>调色板颜色转成 GDI 颜色（半透明值叠到不透明，控件库不处理半透明填充）。</summary>
    /// <param name="color">调色板颜色。</param>
    public static Color Solid(WorkflowColor color) => Color.FromArgb(color.R, color.G, color.B);

    /// <summary>调色板颜色转成 GDI 颜色，保留不透明度（自绘时使用）。</summary>
    /// <param name="color">调色板颜色。</param>
    public static Color ToColor(WorkflowColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    private static bool HighContrast => ModernUiSettings.RespectSystemHighContrast && SystemInformation.HighContrast;

    // 与 Foundation 的 OperatorShellStyle.Derive 同一套推导：悬停、按下、禁用、滚动条等颜色都由调色板计算，
    // 只有一种强调色；红 / 黄 / 绿只用于状态。
    private static ModernTheme Derive(WorkflowThemePalette palette)
    {
        var c = palette.Colors;
        var dark = palette.IsDark;
        var field = dark ? c.SurfaceAlt : c.Surface;
        var fill = palette.ButtonFill;
        return (dark ? ModernTheme.Dark : ModernTheme.Light) with
        {
            Background = Solid(c.Background),
            Container = Solid(c.Surface),
            Elevated = Solid(field),
            Control = Solid(field),
            ControlHover = Solid(field.Mix(c.Text, dark ? .08 : .04)),
            Border = Solid(palette.Border),
            BorderSecondary = Solid(c.Line),
            Text = Solid(c.Text),
            TextSecondary = Solid(c.TextMuted),
            TextDisabled = Solid(c.TextMuted.Mix(c.Surface, .45)),
            Primary = Solid(fill),
            PrimaryHover = Solid(fill.Mix(WorkflowColors.White, .12)),
            PrimaryActive = Solid(fill.Mix(WorkflowColors.Black, .14)),
            PrimaryBackground = Solid(c.AccentSoft),
            PrimarySurface = Solid(c.AccentSoft.Mix(c.Surface, .5)),
            Success = Solid(c.Success),
            Warning = Solid(c.Warning),
            Error = Solid(c.Error),
            SuccessSurface = Solid(c.SuccessSurface),
            WarningSurface = Solid(c.WarningSurface),
            ErrorSurface = Solid(c.ErrorSurface),
            OverlaySurface = Solid(dark ? c.SurfaceAlt.Mix(WorkflowColors.White, .06) : c.Text),
            ScrollThumb = Color.FromArgb(100, c.TextMuted.R, c.TextMuted.G, c.TextMuted.B),
            ScrollThumbHover = Color.FromArgb(140, c.TextMuted.R, c.TextMuted.G, c.TextMuted.B),
            ScrollThumbDragging = Color.FromArgb(180, c.TextMuted.R, c.TextMuted.G, c.TextMuted.B),
            Radius = palette.ControlRadius
        };
    }
}
