namespace DP.WorkFlow.UI;

/// <summary>
/// 界面调色板：与 DP.Foundation 操作台的主题（<c>ThemePalette</c>）同一套颜色角色和取值，
/// Workflow 页面挂进操作台后直接换成操作台当前主题即可，不需要另配颜色。
/// </summary>
/// <param name="Background">工作区底色。</param>
/// <param name="Surface">卡片底色。</param>
/// <param name="SurfaceAlt">次一级表面（表头、隔行、节点标题行）。</param>
/// <param name="Navigation">侧栏底色。</param>
/// <param name="NavigationText">侧栏文字。</param>
/// <param name="NavigationActive">侧栏选中文字。</param>
/// <param name="NavigationSelected">侧栏选中底色（可半透明）。</param>
/// <param name="Text">正文。</param>
/// <param name="TextMuted">次要文字。</param>
/// <param name="Line">卡片描边、表格线。</param>
/// <param name="Accent">唯一的强调色。</param>
/// <param name="AccentSoft">强调色浅底。</param>
/// <param name="Success">成功 / 运行。</param>
/// <param name="SuccessSurface">成功浅底。</param>
/// <param name="Warning">警告 / 暂停 / 提示。</param>
/// <param name="WarningSurface">警告浅底。</param>
/// <param name="Error">错误 / 异常。</param>
/// <param name="ErrorSurface">错误浅底。</param>
public sealed record WorkflowThemeColors(
    WorkflowColor Background,
    WorkflowColor Surface,
    WorkflowColor SurfaceAlt,
    WorkflowColor Navigation,
    WorkflowColor NavigationText,
    WorkflowColor NavigationActive,
    WorkflowColor NavigationSelected,
    WorkflowColor Text,
    WorkflowColor TextMuted,
    WorkflowColor Line,
    WorkflowColor Accent,
    WorkflowColor AccentSoft,
    WorkflowColor Success,
    WorkflowColor SuccessSurface,
    WorkflowColor Warning,
    WorkflowColor WarningSurface,
    WorkflowColor Error,
    WorkflowColor ErrorSurface);

/// <summary>一套可选主题：标识、显示名、明暗、圆角与颜色。</summary>
/// <param name="Id">主题标识（与 Foundation 内置主题同名）。</param>
/// <param name="Name">中文显示名。</param>
/// <param name="EnglishName">英文显示名。</param>
/// <param name="IsDark">工作区是否为深色。</param>
/// <param name="Radius">卡片圆角（逻辑像素）；按钮、输入框取它的 0.8 倍。</param>
/// <param name="Colors">颜色。</param>
public sealed record WorkflowThemePalette(string Id, string Name, string EnglishName, bool IsDark, int Radius, WorkflowThemeColors Colors)
{
    /// <summary>默认主题的标识（与 Foundation 默认一致）。</summary>
    public const string DefaultId = "blue";

    /// <summary>浅色专业蓝（默认）。</summary>
    public static WorkflowThemePalette Blue { get; } = new("blue", "浅色专业蓝", "Professional blue", false, 10, new(
        Hex("#F3F6FB"), Hex("#FFFFFF"), Hex("#F8FAFC"), Hex("#14243D"), Hex("#ACB9CD"), Hex("#FFFFFF"), Hex("#FFFFFF1A"),
        Hex("#1C2C44"), Hex("#6E7F94"), Hex("#E4EAF2"), Hex("#2563EB"), Hex("#EAF1FF"),
        Hex("#15835A"), Hex("#E8F6EF"), Hex("#A36408"), Hex("#FFF5DF"), Hex("#C53030"), Hex("#FBEDED")));

    /// <summary>深色监控。</summary>
    public static WorkflowThemePalette Night { get; } = new("night", "深色监控", "Night monitor", true, 8, new(
        Hex("#101722"), Hex("#192330"), Hex("#1D2938"), Hex("#0B111B"), Hex("#8E9FB7"), Hex("#DCEAFF"), Hex("#2877F526"),
        Hex("#E6EDF7"), Hex("#9CAEC4"), Hex("#2B394C"), Hex("#75AAFF"), Hex("#263C59"),
        Hex("#66D6AC"), Hex("#1D3E36"), Hex("#F5C46B"), Hex("#423723"), Hex("#E5686B"), Hex("#3A2224")));

    /// <summary>青绿极简。</summary>
    public static WorkflowThemePalette Mint { get; } = new("mint", "青绿极简", "Mint minimal", false, 16, new(
        Hex("#F2F7F5"), Hex("#FFFFFF"), Hex("#F8FBF9"), Hex("#FFFFFF"), Hex("#658077"), Hex("#117261"), Hex("#E9F5EF"),
        Hex("#203F35"), Hex("#6D847B"), Hex("#DFEAE4"), Hex("#087F6C"), Hex("#E8F5EF"),
        Hex("#147753"), Hex("#E4F3E9"), Hex("#9C6816"), Hex("#FBF2DF"), Hex("#C53030"), Hex("#FBEDED")));

    /// <summary>暖灰工程。</summary>
    public static WorkflowThemePalette Warm { get; } = new("warm", "暖灰工程", "Warm engineering", false, 5, new(
        Hex("#F3F1ED"), Hex("#FFFEFA"), Hex("#F8F7F3"), Hex("#EAE7E0"), Hex("#736E63"), Hex("#39362E"), Hex("#DCD7CC"),
        Hex("#3A382F"), Hex("#817D71"), Hex("#E4E0D6"), Hex("#675C49"), Hex("#EDE9DF"),
        Hex("#4D7750"), Hex("#EBF1E5"), Hex("#9B651B"), Hex("#F8ECD6"), Hex("#B4423A"), Hex("#F7E8E4")));

    /// <summary>石墨橙。</summary>
    public static WorkflowThemePalette Orange { get; } = new("orange", "石墨橙", "Graphite orange", true, 4, new(
        Hex("#181B20"), Hex("#23272E"), Hex("#2A2F37"), Hex("#121519"), Hex("#A4AAB4"), Hex("#FFB173"), Hex("#FA8C2720"),
        Hex("#EDF0F4"), Hex("#A6AFBD"), Hex("#393F48"), Hex("#FFA05A"), Hex("#473426"),
        Hex("#8FD1AC"), Hex("#263F33"), Hex("#EFCA78"), Hex("#443D29"), Hex("#EE7A72"), Hex("#46282A")));

    /// <summary>内置主题，按显示顺序排列。</summary>
    public static IReadOnlyList<WorkflowThemePalette> BuiltIn { get; } = new[] { Blue, Night, Mint, Warm, Orange };

    /// <summary>按标识查找内置主题（不区分大小写）；找不到时返回默认主题。</summary>
    /// <param name="id">主题标识；兼容旧写法 "Light" / "Dark"。</param>
    public static WorkflowThemePalette Resolve(string? id)
    {
        if (string.Equals(id, "Light", StringComparison.OrdinalIgnoreCase)) id = Blue.Id;
        if (string.Equals(id, "Dark", StringComparison.OrdinalIgnoreCase)) id = Night.Id;
        return BuiltIn.FirstOrDefault(theme => string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Blue;
    }

    /// <summary>显示顺序中的下一个主题（循环）。</summary>
    /// <param name="current">当前主题。</param>
    public static WorkflowThemePalette Next(WorkflowThemePalette current)
    {
        var index = -1;
        for (var i = 0; i < BuiltIn.Count; i++)
            if (string.Equals(BuiltIn[i].Id, current?.Id, StringComparison.OrdinalIgnoreCase)) index = i;
        return BuiltIn[(index + 1) % BuiltIn.Count];
    }

    /// <summary>按钮、输入框、表格的圆角：比卡片小一档，保持嵌套时的同心感（与 Foundation 相同）。</summary>
    public int ControlRadius => Math.Max(2, (int)Math.Round(Radius * .8));

    /// <summary>输入框、标题文字所用的描边：比 <see cref="WorkflowThemeColors.Line"/> 深一档。</summary>
    public WorkflowColor Border => Colors.Line.Mix(Colors.Text, IsDark ? .14 : .12);

    /// <summary>
    /// 主按钮底色：主按钮固定用白字，深色主题里偏亮的强调色压暗到白字可读为止，色相不变（与 Foundation 相同）。
    /// </summary>
    public WorkflowColor ButtonFill
    {
        get
        {
            var fill = Colors.Accent;
            for (var step = 1; step <= 10 && fill.ContrastWith(WorkflowColors.White) < 3.2; step++)
                fill = Colors.Accent.Mix(WorkflowColors.Black, step * .06);
            return fill;
        }
    }

    private static WorkflowColor Hex(string value) => WorkflowColors.Parse(value);
}

/// <summary>颜色计算：解析、混合、叠加与对比度。</summary>
public static class WorkflowColors
{
    /// <summary>白色。</summary>
    public static WorkflowColor White { get; } = new(255, 255, 255);

    /// <summary>黑色。</summary>
    public static WorkflowColor Black { get; } = new(0, 0, 0);

    /// <summary>解析 #RRGGBB 或 #RRGGBBAA。</summary>
    /// <param name="value">颜色文字。</param>
    public static WorkflowColor Parse(string value)
    {
        Guard.NotNull(value);
        var text = value.Trim().TrimStart('#');
        if (text.Length is not (6 or 8))
            throw new FormatException("颜色应写成 #RRGGBB 或 #RRGGBBAA：" + value);
        byte Part(int index) => Convert.ToByte(text.Substring(index, 2), 16);
        return new WorkflowColor(Part(0), Part(2), Part(4), text.Length == 8 ? Part(6) : (byte)255);
    }

    /// <summary>按比例向另一颜色混合（0 为本色，1 为目标色）；结果不透明度取本色。</summary>
    /// <param name="color">本色。</param>
    /// <param name="other">目标色。</param>
    /// <param name="amount">比例。</param>
    public static WorkflowColor Mix(this WorkflowColor color, WorkflowColor other, double amount)
    {
        amount = Math.Max(0, Math.Min(1, amount));
        byte Channel(byte from, byte to) => (byte)Math.Round(from + (to - from) * amount);
        return new WorkflowColor(Channel(color.R, other.R), Channel(color.G, other.G), Channel(color.B, other.B), color.A);
    }

    /// <summary>把半透明颜色叠到不透明底色上，得到不透明结果。</summary>
    /// <param name="color">上层颜色。</param>
    /// <param name="backdrop">底色。</param>
    public static WorkflowColor Over(this WorkflowColor color, WorkflowColor backdrop)
    {
        var alpha = color.A / 255d;
        byte Channel(byte top, byte bottom) => (byte)Math.Round(top * alpha + bottom * (1 - alpha));
        return new WorkflowColor(Channel(color.R, backdrop.R), Channel(color.G, backdrop.G), Channel(color.B, backdrop.B));
    }

    /// <summary>相对亮度（WCAG 定义，0～1）。</summary>
    /// <param name="color">颜色。</param>
    public static double Luminance(this WorkflowColor color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= .03928 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }

    /// <summary>两种颜色的对比度（WCAG，1～21）。</summary>
    /// <param name="color">颜色。</param>
    /// <param name="other">另一颜色。</param>
    public static double ContrastWith(this WorkflowColor color, WorkflowColor other)
    {
        var a = color.Luminance();
        var b = other.Luminance();
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    /// <summary>在白色和近黑色中选出放在 <paramref name="fill"/> 上更清楚的文字颜色。</summary>
    /// <param name="fill">底色。</param>
    public static WorkflowColor ReadableOn(this WorkflowColor fill)
    {
        var dark = new WorkflowColor(0x1A, 0x14, 0x08);
        return fill.ContrastWith(White) >= fill.ContrastWith(dark) ? White : dark;
    }
}
