using System.Globalization;

namespace DP.WorkFlow;

/// <summary>设备目录里对象的类别。</summary>
public enum WorkflowMotionObjectKind
{
    /// <summary>轴，<see cref="WorkflowMotionObject.DeviceId"/> 与 <see cref="WorkflowMotionObject.Number"/> 即节点里的设备 ID 与轴号。</summary>
    Axis = 0,
    /// <summary>轴步骤，<see cref="WorkflowMotionObject.Key"/> 即节点里的步骤键。</summary>
    AxisStep = 1,
    /// <summary>数字输入点，设备 ID 与编号即节点里的驱动 ID 与索引。</summary>
    DigitalInput = 2,
    /// <summary>数字输出点，设备 ID 与编号即节点里的驱动 ID 与索引。</summary>
    DigitalOutput = 3,
    /// <summary>气缸，键即气缸名称。</summary>
    Cylinder = 4,
    /// <summary>真空，键即真空名称。</summary>
    Vacuum = 5,
    /// <summary>读码器，键即设备键。</summary>
    CodeReader = 6
}

/// <summary>设备目录里的一个对象。</summary>
/// <param name="Kind">类别。</param>
/// <param name="Key">宿主里的逻辑名称（轴 / IO 点名称、步骤 ID、气缸名称等）。</param>
/// <param name="DisplayName">显示名称；为空时用 <paramref name="Key"/>。</param>
/// <param name="DeviceId">轴和 IO 点所在设备（控制卡节点）；其他类别为空。</param>
/// <param name="Number">轴号或 IO 索引；其他类别为空。</param>
public sealed record WorkflowMotionObject(WorkflowMotionObjectKind Kind, string Key, string? DisplayName = null, string? DeviceId = null, int? Number = null)
{
    /// <summary>显示名称，缺省为键。</summary>
    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Key : DisplayName!;
}

/// <summary>
/// 宿主提供的只读设备目录：本机配置了哪些轴、步骤、IO、气缸、真空、读码器。
/// 运动节点只用它做属性下拉和发布前检查，执行仍走各设备服务接口；没有目录时节点照常运行，只是没有下拉和检查。
/// </summary>
public interface IWorkflowMotionCatalog
{
    /// <summary>全部对象。</summary>
    IReadOnlyList<WorkflowMotionObject> Objects { get; }
}

/// <summary>运动节点属性的宿主候选键（见 <see cref="WorkflowPropertyEditorKeys.HostChoicePrefix"/>）。</summary>
public static class WorkflowMotionEditorKeys
{
    /// <summary>轴所在设备 ID。</summary>
    public const string AxisDevice = WorkflowPropertyEditorKeys.HostChoicePrefix + "Motion.AxisDevice";
    /// <summary>轴号。</summary>
    public const string AxisNumber = WorkflowPropertyEditorKeys.HostChoicePrefix + "Motion.AxisNumber";
    /// <summary>轴步骤键。</summary>
    public const string AxisStep = WorkflowPropertyEditorKeys.HostChoicePrefix + "Motion.AxisStep";
    /// <summary>IO 驱动 ID。</summary>
    public const string IoDevice = WorkflowPropertyEditorKeys.HostChoicePrefix + "Motion.IoDevice";
    /// <summary>IO 索引。</summary>
    public const string IoIndex = WorkflowPropertyEditorKeys.HostChoicePrefix + "Motion.IoIndex";
    /// <summary>气缸名称。</summary>
    public const string Cylinder = WorkflowPropertyEditorKeys.HostChoicePrefix + "Motion.Cylinder";
    /// <summary>真空名称。</summary>
    public const string Vacuum = WorkflowPropertyEditorKeys.HostChoicePrefix + "Motion.Vacuum";
    /// <summary>读码器设备键。</summary>
    public const string CodeReader = WorkflowPropertyEditorKeys.HostChoicePrefix + "Motion.CodeReader";
}

/// <summary>不可变设备目录，同时按 <see cref="WorkflowMotionEditorKeys"/> 回答属性候选。</summary>
public sealed class WorkflowMotionCatalog : IWorkflowMotionCatalog, IWorkflowPropertyChoiceSource
{
    /// <summary>创建目录。</summary>
    /// <param name="objects">对象；空项忽略。</param>
    public WorkflowMotionCatalog(IEnumerable<WorkflowMotionObject> objects)
    {
        Guard.NotNull(objects);
        Objects = objects.Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Key)).ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<WorkflowMotionObject> Objects { get; }

    /// <inheritdoc />
    public IReadOnlyList<KeyValuePair<string, object?>>? GetChoices(string editorKey, string propertyName) => editorKey switch
    {
        WorkflowMotionEditorKeys.AxisDevice => Devices(WorkflowMotionObjectKind.Axis),
        WorkflowMotionEditorKeys.AxisNumber => Numbers(WorkflowMotionObjectKind.Axis),
        WorkflowMotionEditorKeys.AxisStep => Keys(WorkflowMotionObjectKind.AxisStep),
        WorkflowMotionEditorKeys.IoDevice => Devices(WorkflowMotionObjectKind.DigitalInput, WorkflowMotionObjectKind.DigitalOutput),
        WorkflowMotionEditorKeys.IoIndex => Numbers(WorkflowMotionObjectKind.DigitalInput, WorkflowMotionObjectKind.DigitalOutput),
        WorkflowMotionEditorKeys.Cylinder => Keys(WorkflowMotionObjectKind.Cylinder),
        WorkflowMotionEditorKeys.Vacuum => Keys(WorkflowMotionObjectKind.Vacuum),
        WorkflowMotionEditorKeys.CodeReader => Keys(WorkflowMotionObjectKind.CodeReader),
        _ => null
    };

    private KeyValuePair<string, object?>[] Keys(WorkflowMotionObjectKind kind) => Objects
        .Where(item => item.Kind == kind)
        .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
        .Select(item => new KeyValuePair<string, object?>(item.Label == item.Key ? item.Key : $"{item.Key}（{item.Label}）", item.Key))
        .ToArray();

    private KeyValuePair<string, object?>[] Devices(params WorkflowMotionObjectKind[] kinds) => Objects
        .Where(item => kinds.Contains(item.Kind) && !string.IsNullOrWhiteSpace(item.DeviceId))
        .Select(item => item.DeviceId!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
        .Select(id => new KeyValuePair<string, object?>(id, id))
        .ToArray();

    // 轴号 / 索引只能按属性给全部候选（属性面板不按同节点的设备 ID 过滤），所以显示文字带上设备和名称。
    private KeyValuePair<string, object?>[] Numbers(params WorkflowMotionObjectKind[] kinds) => Objects
        .Where(item => kinds.Contains(item.Kind) && item.Number is not null)
        .OrderBy(item => item.DeviceId, StringComparer.OrdinalIgnoreCase)
        .ThenBy(item => item.Kind)
        .ThenBy(item => item.Number)
        .Select(item => new KeyValuePair<string, object?>(
            $"{item.DeviceId} · {KindLabel(item.Kind)}{item.Number!.Value.ToString(CultureInfo.InvariantCulture)} · {item.Label}",
            item.Number!.Value.ToString(CultureInfo.InvariantCulture)))
        .ToArray();

    private static string KindLabel(WorkflowMotionObjectKind kind) => kind switch
    {
        WorkflowMotionObjectKind.DigitalInput => "输入 ",
        WorkflowMotionObjectKind.DigitalOutput => "输出 ",
        _ => "轴 "
    };
}

/// <summary>运动节点的发布前检查：按目录核对固定写在节点里的对象；绑定值运行时才知道，不检查。</summary>
internal static class WorkflowMotionEnvironment
{
    public static IReadOnlyList<string> Check(IServiceProvider services, Func<IWorkflowMotionCatalog, IEnumerable<string?>> check)
    {
        if (services?.GetService(typeof(IWorkflowMotionCatalog)) is not IWorkflowMotionCatalog catalog)
            return Array.Empty<string>();
        return check(catalog).Where(message => !string.IsNullOrWhiteSpace(message)).Select(message => message!).ToArray();
    }

    public static string? Key(IWorkflowMotionCatalog catalog, WorkflowMotionObjectKind kind, string? key, string label)
    {
        if (string.IsNullOrWhiteSpace(key)) return $"{label}没有填写。";
        return catalog.Objects.Any(item => item.Kind == kind && string.Equals(item.Key, key.Trim(), StringComparison.OrdinalIgnoreCase))
            ? null : $"找不到{label}“{key.Trim()}”。";
    }

    public static string? Address(IWorkflowMotionCatalog catalog, string? deviceId, string? number, string label, params WorkflowMotionObjectKind[] kinds)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return $"{label}的设备 ID 没有填写。";
        if (!int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0) return $"{label}编号“{number}”不是有效的数字。";
        return catalog.Objects.Any(item => kinds.Contains(item.Kind) && item.Number == value && string.Equals(item.DeviceId, deviceId.Trim(), StringComparison.OrdinalIgnoreCase))
            ? null : $"找不到{label} {deviceId.Trim()}[{value}]。";
    }

    public static WorkflowMotionObjectKind IoKind(WorkflowIoPointType type) =>
        type == WorkflowIoPointType.Output ? WorkflowMotionObjectKind.DigitalOutput : WorkflowMotionObjectKind.DigitalInput;
}
