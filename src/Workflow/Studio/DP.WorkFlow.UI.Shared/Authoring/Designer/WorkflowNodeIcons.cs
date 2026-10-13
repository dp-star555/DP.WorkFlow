using System.Globalization;

namespace DP.WorkFlow.UI;

/// <summary>一个已解析的节点图标：24×24 视图框内的线框矢量路径（描边绘制、单色）。</summary>
/// <param name="Key">图标键。</param>
/// <param name="Figures">已解析的子路径。</param>
public sealed record WorkflowNodeIcon(string Key, IReadOnlyList<WorkflowVectorFigure> Figures)
{
    /// <summary>图标视图框边长。</summary>
    public const double ViewBox = 24;

    /// <summary>图标在视图框内的描边宽度。</summary>
    public const double StrokeWidth = 1.7;
}

/// <summary>
/// 节点图标目录：图标是展示元数据，与节点实现分开，节点程序集不需要引用任何界面类型。
/// 查找顺序：节点定义声明的图标（<see cref="WorkflowNodeAttribute.Icon"/>：图标键或路径数据）→ 节点类型 → 类别（最长前缀匹配）→ 默认图标。
/// 插件或宿主也可以调用 <see cref="RegisterIcon"/>、
/// <see cref="MapNodeType"/>、<see cref="MapCategory"/> 补充或覆盖，画布节点和工具箱都从这里取同一个图标。
/// </summary>
public static class WorkflowNodeIcons
{
    /// <summary>默认图标键。</summary>
    public const string DefaultKey = "node";

    private static readonly object Sync = new();
    private static readonly Dictionary<string, string> Paths = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, WorkflowNodeIcon> Parsed = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> NodeTypes = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Categories = new(StringComparer.Ordinal);

    static WorkflowNodeIcons()
    {
        foreach (var (key, path) in BuiltInIcons()) Paths[key] = path;
        foreach (var (nodeType, key) in BuiltInNodeTypes()) NodeTypes[nodeType] = key;
        foreach (var (category, key) in BuiltInCategories()) Categories[category] = key;
    }

    /// <summary>登记或替换一个图标（SVG 路径数据，24×24 视图框，线框描边）。</summary>
    /// <param name="key">图标键。</param>
    /// <param name="pathData">SVG 路径数据。</param>
    public static void RegisterIcon(string key, string pathData)
    {
        Guard.NotNullOrWhiteSpace(key);
        WorkflowVectorPath.Parse(pathData);
        lock (Sync) { Paths[key] = pathData; Parsed.Remove(key); }
    }

    /// <summary>为节点类型指定图标。</summary>
    /// <param name="nodeType">节点类型键。</param>
    /// <param name="iconKey">图标键。</param>
    public static void MapNodeType(string nodeType, string iconKey)
    {
        Guard.NotNullOrWhiteSpace(nodeType); Guard.NotNullOrWhiteSpace(iconKey);
        lock (Sync) NodeTypes[nodeType] = iconKey;
    }

    /// <summary>为类别（或类别前缀，如“4.Motion/IO”）指定图标。</summary>
    /// <param name="category">节点类别或类别前缀。</param>
    /// <param name="iconKey">图标键。</param>
    public static void MapCategory(string category, string iconKey)
    {
        Guard.NotNullOrWhiteSpace(category); Guard.NotNullOrWhiteSpace(iconKey);
        lock (Sync) Categories[category] = iconKey;
    }

    /// <summary>按节点定义声明的图标、节点类型和类别查找图标。</summary>
    /// <param name="nodeType">节点类型键。</param>
    /// <param name="category">节点类别；未知时为空。</param>
    /// <param name="declared">节点定义声明的图标：已登记的图标键，或 24×24 视图框的 SVG 路径数据；无法解析时忽略。</param>
    public static WorkflowNodeIcon Resolve(string nodeType, string? category, string? declared = null)
    {
        lock (Sync)
        {
            if (!string.IsNullOrWhiteSpace(declared))
            {
                if (Paths.ContainsKey(declared!)) return Parse(declared!);
                var cacheKey = "path:" + declared;
                if (Parsed.TryGetValue(cacheKey, out var custom)) return custom;
                try
                {
                    return Parsed[cacheKey] = new WorkflowNodeIcon(cacheKey, WorkflowVectorPath.Parse(declared!));
                }
                catch (FormatException)
                {
                    // 声明的不是图标键也不是合法路径：按类型和类别继续查找。
                }
            }
            if (!NodeTypes.TryGetValue(nodeType, out var key) || !Paths.ContainsKey(key))
            {
                key = DefaultKey;
                if (!string.IsNullOrEmpty(category))
                {
                    var match = Categories.Where(item => category!.StartsWith(item.Key, StringComparison.Ordinal) && Paths.ContainsKey(item.Value))
                        .OrderByDescending(item => item.Key.Length).FirstOrDefault();
                    if (match.Key is not null) key = match.Value;
                }
            }
            return Parse(key);
        }
    }

    private static WorkflowNodeIcon Parse(string key)
    {
        if (!Parsed.TryGetValue(key, out var icon))
            Parsed[key] = icon = new WorkflowNodeIcon(key, WorkflowVectorPath.Parse(Paths[key]));
        return icon;
    }

    /// <summary>已登记的全部图标键。</summary>
    public static IReadOnlyList<string> Keys
    {
        get { lock (Sync) return Paths.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray(); }
    }

    private static string Circle(double cx, double cy, double r) => string.Format(CultureInfo.InvariantCulture,
        "M{0} {1}a{2} {2} 0 1 0 {3} 0a{2} {2} 0 1 0 {4} 0", cx - r, cy, r, 2 * r, -2 * r);

    private static IEnumerable<(string Key, string Path)> BuiltInIcons() => new[]
    {
        (DefaultKey, "M6 5h12a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z"),
        ("play", "M8 6l10 6-10 6z"),
        ("stop", "M9 7h6a2 2 0 0 1 2 2v6a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2z"),
        ("split", "M12 4v6M12 10l-6 6v4M12 10l6 6v4"),
        ("merge", "M6 4v4l6 6M18 4v4l-6 6M12 14v6"),
        ("decision", "M12 3l9 9-9 9-9-9zM12 9v4M12 15.5v.5"),
        ("compare", "M5 9h14M5 15h14"),
        ("bolt", "M13 3L5 14h6l-1 7 8-11h-6z"),
        ("hourglass", "M7 4h10M7 20h10M8 4c0 5 8 5 8 8s-8 3-8 8M16 4c0 5-8 5-8 8s8 3 8 8"),
        ("code", "M9 7l-5 5 5 5M15 7l5 5-5 5"),
        ("convert", "M4 8h13M14 5l3 3-3 3M20 16H7M10 13l-3 3 3 3"),
        ("loop", "M4 12a8 8 0 0 1 14-5.3M20 12a8 8 0 0 1-14 5.3M18 3v4h-4M6 21v-4h4"),
        ("clock", Circle(12, 12, 8) + "M12 8v4l3 2"),
        ("jump", "M4 12h11M11 8l4 4-4 4M19 5v14"),
        ("layers", "M12 4l8 4-8 4-8-4zM4 12l8 4 8-4M4 16l8 4 8-4"),
        ("signal", "M3 12h4l3-7 4 14 3-7h4"),
        ("queue", "M4 6h16M4 12h16M4 18h10"),
        ("axis", "M12 3v18M3 12h18M10 5l2-2 2 2M19 10l2 2-2 2M14 19l-2 2-2-2M5 14l-2-2 2-2"),
        ("barcode", "M4 7V4h3M17 4h3v3M20 17v3h-3M7 20H4v-3M8 8v8M11 8v8M14 8v8M16.5 8v8"),
        ("plug", "M9 3v5M15 3v5M7 8h10v3a5 5 0 0 1-10 0zM12 16v5"),
        ("cylinder", "M3 9h12v6H3zM15 12h6M21 9v6"),
        ("vacuum", "M12 4v10M8 10l4 4 4-4M5 19h14"),
        ("box", "M12 3l8 4.5v9L12 21l-8-4.5v-9zM4 7.5l8 4.5 8-4.5M12 12v9"),
        ("station", "M4 20h16M6 20V9l6-5 6 5v11M10 20v-6h4v6"),
        ("robot", "M8 8h8v8H8zM12 4v4M4 12h4M16 12h4M10 16v4M14 16v4"),
        ("warning", "M12 4l9 16H3zM12 10v4M12 17v.5"),
        ("user", Circle(12, 8, 4) + "M5 20a7 7 0 0 1 14 0"),
        ("camera", "M4 8h3l2-2h6l2 2h3v10H4z" + Circle(12, 13, 3.5)),
        ("sliders", "M4 7h10M18 7h2M4 17h4M12 17h8M16 5v4M10 15v4"),
        ("region", "M5 5h6v6H5zM13 13h6v6h-6zM15 5h4v4M5 15v4h4"),
        ("target", Circle(12, 12, 7) + Circle(12, 12, 2.5) + "M12 2v3M12 19v3M2 12h3M19 12h3"),
        ("axes", "M5 19V5M5 19h14M5 19l9-9M3 7l2-2 2 2M17 17l2 2-2 2"),
        ("ruler", "M4 16L16 4l4 4L8 20zM8 12l2 2M11 9l2 2M14 6l2 2"),
        ("line", "M4 17L20 7" + Circle(8, 14.5, 1.6) + Circle(16, 9.5, 1.6)),
        ("circle", Circle(12, 12, 7.5) + Circle(12, 4.5, 1.4) + Circle(18.5, 15.8, 1.4) + Circle(5.5, 15.8, 1.4)),
        ("rectangle", "M5 7h14v10H5z" + Circle(5, 7, 1.4) + Circle(19, 17, 1.4)),
        ("caliper", "M6 4v16M18 4v16M6 12h12M9 9l-3 3 3 3M15 9l3 3-3 3"),
        ("geometry", "M5 19L19 5" + Circle(6, 18, 1.8) + Circle(18, 6, 1.8) + Circle(12, 12, 1.8)),
        ("inspect", Circle(11, 11, 7) + "M16 16l4 4"),
        ("blobs", Circle(8, 9, 3.5) + Circle(16, 15, 4) + Circle(17, 6.5, 1.8)),
        ("text", "M5 7V5h14v2M12 5v14M9 19h6"),
        ("calibration", "M4 4h16v16H4zM12 4v16M4 12h16" + Circle(12, 12, 2.5)),
        ("tag", "M4 12V5h7l9 9-7 7z" + Circle(8, 9, 1.3)),
        ("color", Circle(12, 12, 8) + "M12 4v16M4.7 8.5l14.6 7M4.7 15.5l14.6-7")
    };

    private static IEnumerable<(string NodeType, string Key)> BuiltInNodeTypes() => new[]
    {
        ("Start", "play"), ("End", "stop"),
        ("ParallelAll", "split"), ("WaitAllInputsCompleted", "merge"),
        ("Delay", "clock"), ("Jump", "jump"), ("Loop", "loop"), ("LoopBlock", "loop"), ("Block", "layers"),
        ("Decision", "decision"), ("ValueCompare", "compare"), ("StringCompare", "compare"),
        ("Action", "bolt"), ("WaitFunction", "hourglass"), ("CSharpScript", "code"), ("ConvertValue", "convert"),
        ("VacuumControl", "vacuum"), ("VacuumWait", "vacuum"),
        ("StationCanReceive", "station"), ("StationCanSend", "station"), ("StationFinished", "station"),
        ("StationSlotHasProduct", "station"), ("StationWaitCanReceive", "station"), ("StationWaitCanSend", "station"),
        ("OperatorChoice", "user"), ("OperatorStepConfirm", "user"),
        ("Vision.BuildCoordinateSystem", "axes"),
        ("Vision.FindLine", "line"), ("Vision.FitRobustLine", "line"), ("Vision.FindCircle", "circle"),
        ("Vision.FindRectangle", "rectangle"), ("Vision.MeasureCaliper", "caliper"),
        ("Vision.AnalyzeBlobs", "blobs"), ("Vision.SelectBlobs", "blobs"), ("Vision.AnalyzeColor", "color"),
        ("Vision.ReadBarcode", "barcode"), ("Vision.RecognizeTextLine", "text"),
        ("LabelInspection.Inspect", "tag")
    };

    private static IEnumerable<(string Category, string Key)> BuiltInCategories() => new[]
    {
        ("1.Base", "play"),
        ("2.Function/判断", "decision"), ("2.Function/执行", "bolt"), ("2.Function/等待", "hourglass"),
        ("2.Function/脚本", "code"), ("2.Function/转换", "convert"),
        ("3.Control/信号", "signal"), ("3.Control/流程控制", "split"), ("3.Control/队列", "queue"), ("Control", "layers"),
        ("4.Motion/Axis", "axis"), ("4.Motion/CodeReader", "barcode"), ("4.Motion/IO", "plug"), ("4.Motion/Pneumatic", "cylinder"),
        ("8.Process/ProductFlow", "box"), ("8.Process/WaferRobot", "robot"), ("8.Process/异常恢复", "warning"),
        ("5.Vision/1.取图", "camera"), ("5.Vision/2.图像处理", "sliders"), ("5.Vision/3.区域", "region"),
        ("5.Vision/4.定位", "target"), ("5.Vision/5.测量", "ruler"), ("5.Vision/6.几何", "geometry"),
        ("5.Vision/7.检测", "inspect"), ("5.Vision/8.识别", "text"), ("5.Vision/9.标定", "calibration"),
        ("业务检测", "tag")
    };
}
