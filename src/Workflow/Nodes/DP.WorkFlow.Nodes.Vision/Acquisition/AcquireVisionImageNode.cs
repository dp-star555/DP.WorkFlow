using System.ComponentModel;
using System.Text.Json.Serialization;
using DP.Vision;
using DP.Vision.Acquisition;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>图像获取的来源；相机形态决定候选源与整图请求。</summary>
public enum EWorkflowVisionImageSource
{
    /// <summary>读取一个图像文件。</summary>
    File = 0,
    /// <summary>按冻结清单读取文件夹中的下一张图像。</summary>
    Folder = 1,
    /// <summary>从面阵逻辑源主动取图或领取回调帧。</summary>
    AreaCamera = 2,
    /// <summary>从线扫逻辑源取得已拼接的整图。</summary>
    LineCamera = 3
}

/// <summary>图像获取输出的像素格式。</summary>
public enum EWorkflowVisionPixelFormat
{
    /// <summary>按文件或相机的原始格式输出。</summary>
    Original = 0,
    /// <summary>彩色按亮度转换为8位灰度；16位灰度需用图像预处理转换。</summary>
    Gray8 = 1
}

/// <summary>统一文件、目录及相机取图入口，输出具有独立租约和帧身份的 ImageFrame。</summary>
[WorkflowNode("Vision.AcquireFrame", DisplayName = "图像获取", Category = "5.Vision/Acquisition",
    Description = "选择文件、文件夹或相机来源，输出统一图像帧。")]
public sealed class AcquireVisionImageNodeModel : WorkflowNodeModel, IWorkflowNodeConfigurationValidator, IWorkflowVisionAlgorithmNode,
    IWorkflowDocumentPropertyChoices
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.AcquireFrame";

    /// <summary>当前启用的来源，其他来源的配置保留但不参与校验及运行。</summary>
    [WorkflowProperty("图像来源", "文件、文件夹、面阵相机或线扫相机；切换来源会保留其他来源的配置。", Category = "图像来源")]
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.VisionImageSourceMode)]
    public EWorkflowVisionImageSource SourceMode { get; set; }

    /// <summary>输出像素格式；文件、文件夹与相机来源都适用。</summary>
    [WorkflowProperty("像素格式", "保持原样：按文件或相机的原始格式输出。8位灰度：彩色按亮度转换为8位灰度；16位灰度请用图像预处理转换。模板匹配、卡尺、边缘测量、阈值分割、连通域分析只支持8位灰度。", Category = "图像来源")]
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.DocumentChoice)]
    public EWorkflowVisionPixelFormat PixelFormat { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<KeyValuePair<string, object?>> GetPropertyChoices(string propertyName, IReadOnlyList<IWorkflowNodeModel> documentNodes) =>
        propertyName == nameof(PixelFormat)
            ? [new("保持原样", EWorkflowVisionPixelFormat.Original), new("8位灰度", EWorkflowVisionPixelFormat.Gray8)]
            : [];

    /// <summary>离线文件解码器；相机模式不声明此能力。</summary>
    [Browsable(false)]
    public VisionAlgorithmSelection Algorithm { get; set; } = new() { ImplementationId = "opencv.image-read" };

    /// <summary>单文件路径。</summary>
    [WorkflowProperty("图像文件", "每次执行读取这一张图像。", Category = "文件参数")]
    [WorkflowPropertyVisibleWhen(nameof(SourceMode), nameof(EWorkflowVisionImageSource.File))]
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.FilePath, CheckExists = true)]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>目录来源路径。</summary>
    [WorkflowProperty("图像文件夹", "按文件名排序，每执行一次节点读取下一张；清单变化时从头读取。", Category = "文件夹参数")]
    [WorkflowPropertyVisibleWhen(nameof(SourceMode), nameof(EWorkflowVisionImageSource.Folder))]
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.FolderPath)]
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>目录筛选的扩展名。</summary>
    [WorkflowProperty("文件类型", "用分号分隔图像扩展名，例如 .png;.bmp;.jpg。", Category = "文件夹参数")]
    [WorkflowPropertyVisibleWhen(nameof(SourceMode), nameof(EWorkflowVisionImageSource.Folder))]
    public string Extensions { get; set; } = ".png;.bmp;.jpg;.jpeg;.tif;.tiff;.pgm;.ppm";

    /// <summary>目录末尾是否明确循环。</summary>
    [WorkflowProperty("循环读取", "开启后末尾回到第一张；关闭时末尾报错。不会自动循环执行下游流程。", Category = "文件夹参数")]
    [WorkflowPropertyVisibleWhen(nameof(SourceMode), nameof(EWorkflowVisionImageSource.Folder))]
    public bool Loop { get; set; }

    /// <summary>是否在每次根运行开始时重置目录序列。</summary>
    [WorkflowProperty("每次运行从头读取", "关闭时，同一宿主的多次运行继续读取下一张；开启时每次根运行从第一张开始。目录、筛选、循环选项或文件清单变化也会重置。", Category = "文件夹参数")]
    [WorkflowPropertyVisibleWhen(nameof(SourceMode), nameof(EWorkflowVisionImageSource.Folder))]
    public bool RestartFolderEachRun { get; set; }

    /// <summary>持久化一次的相机逻辑源；编辑代理按当前相机形态筛选候选。</summary>
    [Browsable(false)]
    public VisionSourceReference? Source { get; set; }

    /// <summary>面阵源编辑代理，不重复持久化 Source。</summary>
    [JsonIgnore]
    [WorkflowProperty("逻辑图像源", "选择机器配置发布的面阵源；主动取图或回调缓冲由源配置决定。", Category = "相机参数")]
    [WorkflowPropertyVisibleWhen(nameof(SourceMode), nameof(EWorkflowVisionImageSource.AreaCamera))]
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.VisionAreaSource)]
    public VisionSourceReference? AreaSource { get => Source; set => Source = value; }

    /// <summary>线扫源编辑代理，不重复持久化 Source。</summary>
    [JsonIgnore]
    [WorkflowProperty("逻辑图像源", "选择机器配置发布的线扫整图源；拼接与触发时序由采集驱动处理。", Category = "相机参数")]
    [WorkflowPropertyVisibleWhen(nameof(SourceMode), nameof(EWorkflowVisionImageSource.LineCamera))]
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.VisionLineScanSource)]
    public VisionSourceReference? LineSource { get => Source; set => Source = value; }

    /// <summary>相机取图或回调帧领取超时。</summary>
    [WorkflowProperty("采集超时", "等待完整图像帧的最长时间。", Category = "相机参数", Unit = "ms")]
    [WorkflowPropertyVisibleWhen(nameof(SourceMode), nameof(EWorkflowVisionImageSource.AreaCamera), nameof(EWorkflowVisionImageSource.LineCamera))]
    public int TimeoutMilliseconds { get; set; } = 5000;

    /// <summary>主动采集曝光覆盖，回调缓冲源在运行前拒绝覆盖。</summary>
    [WorkflowProperty("曝光", "留空保持设备当前设置；回调缓冲源请在机器配置中设置。", Category = "相机参数", Unit = "µs")]
    [WorkflowPropertyVisibleWhen(nameof(SourceMode), nameof(EWorkflowVisionImageSource.AreaCamera), nameof(EWorkflowVisionImageSource.LineCamera))]
    public double? ExposureMicroseconds { get; set; }

    /// <summary>主动采集增益覆盖。</summary>
    [WorkflowProperty("增益", "留空保持设备当前设置；回调缓冲源请在机器配置中设置。", Category = "相机参数", Unit = "dB")]
    [WorkflowPropertyVisibleWhen(nameof(SourceMode), nameof(EWorkflowVisionImageSource.AreaCamera), nameof(EWorkflowVisionImageSource.LineCamera))]
    public double? GainDecibels { get; set; }

    /// <summary>面阵主动采集的触发模式，线扫始终保持源配置。</summary>
    [WorkflowProperty("触发模式", "面阵主动取图的触发模式；线扫触发时序在机器配置中设置。", Category = "相机参数")]
    [WorkflowPropertyVisibleWhen(nameof(SourceMode), nameof(EWorkflowVisionImageSource.AreaCamera))]
    public EVisionTriggerMode TriggerMode { get; set; } = EVisionTriggerMode.KeepCurrent;

    /// <summary>当前模式是否为相机来源。</summary>
    [Browsable(false), JsonIgnore]
    public bool IsCamera => SourceMode is EWorkflowVisionImageSource.AreaCamera or EWorkflowVisionImageSource.LineCamera;

    /// <summary>取得相机所要求的整图形态；离线模式不得调用。使用方法避免配置扫描读取相机专属操作。</summary>
    /// <returns>当前相机模式要求的采集形态。</returns>
    /// <exception cref="InvalidOperationException">当前来源不是相机。</exception>
    public EVisionAcquisitionKind GetCameraKind() => SourceMode switch
    {
        EWorkflowVisionImageSource.AreaCamera => EVisionAcquisitionKind.AreaScan,
        EWorkflowVisionImageSource.LineCamera => EVisionAcquisitionKind.LineScan,
        _ => throw new InvalidOperationException("当前图像来源不是相机。")
    };

    /// <inheritdoc/>
    public IReadOnlyList<WorkflowVisionAlgorithmSlot> GetAlgorithmSlots() =>
        SourceMode is EWorkflowVisionImageSource.File or EWorkflowVisionImageSource.Folder
            ? [new("algorithm", typeof(IImageFileReader), Algorithm)] : [];

    /// <inheritdoc/>
    public IReadOnlyList<string> ValidateConfiguration()
    {
        switch (SourceMode)
        {
            case EWorkflowVisionImageSource.File:
                return string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath) ? ["图像文件路径为空或文件不存在。"] : [];
            case EWorkflowVisionImageSource.Folder:
                return !Directory.Exists(FolderPath) || string.IsNullOrWhiteSpace(Extensions) ? ["图像目录不存在或文件类型为空。"] : [];
            case EWorkflowVisionImageSource.AreaCamera:
            case EWorkflowVisionImageSource.LineCamera:
                var errors = new List<string>();
                if (Source is null || string.IsNullOrWhiteSpace(Source.SourceId)) errors.Add("逻辑图像源不能为空；请在机器配置中发布源后选择。");
                try { _ = CreateRequest(); }
                catch (ArgumentOutOfRangeException error) { errors.Add(error.Message); }
                return errors;
            default:
                return ["图像来源类型未定义。"];
        }
    }

    /// <summary>创建中立相机请求；线扫忽略离线保存的面阵触发设置。</summary>
    /// <returns>交给采集运行时的整图请求。</returns>
    public VisionCaptureRequest CreateRequest() => IsCamera
        ? new(TimeSpan.FromMilliseconds(TimeoutMilliseconds), ExposureMicroseconds, GainDecibels,
            SourceMode == EWorkflowVisionImageSource.AreaCamera ? TriggerMode : EVisionTriggerMode.KeepCurrent)
        : throw new InvalidOperationException("当前图像来源不是相机。");
}

/// <summary>统一调度离线读图与中立采集主干，不在相机回调中直接执行节点。</summary>
public sealed class AcquireVisionImageNodeHandler : WorkflowNodeHandler<AcquireVisionImageNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(AcquireVisionImageNodeModel node,
        IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) => node.SourceMode switch
    {
        EWorkflowVisionImageSource.File => LoadVisionFileNodeHandler.ReadAsync(node.FilePath, node.Algorithm, context, cancellationToken, node.PixelFormat),
        EWorkflowVisionImageSource.Folder => LoadVisionFolderNodeHandler.ReadAsync(node.Id, node.Algorithm, context, cancellationToken, node.PixelFormat),
        EWorkflowVisionImageSource.AreaCamera or EWorkflowVisionImageSource.LineCamera =>
            VisionCaptureNodeExecution.ExecuteAsync(context, node.Id, node.Source, node.CreateRequest(), cancellationToken, node.PixelFormat),
        _ => throw new InvalidOperationException("图像来源类型未定义。")
    };
}
