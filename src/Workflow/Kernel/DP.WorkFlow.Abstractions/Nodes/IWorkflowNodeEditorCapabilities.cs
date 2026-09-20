namespace DP.WorkFlow;

/// <summary>由提供可编辑脚本正文的节点实现，设计器据此自动增加脚本页面。</summary>
public interface IWorkflowScriptNode : IWorkflowNodeModel
{
    /// <summary>获取或设置脚本实例的全局稳定标识；编辑保留，复制节点时重新生成。</summary>
    string ScriptId { get; set; }

    /// <summary>获取或设置脚本正文。</summary>
    string Script { get; set; }

    /// <summary>获取脚本语言的稳定标识。</summary>
    string ScriptLanguage { get; }
}

/// <summary>由支持显式 DLL 引用的脚本节点实现。</summary>
public interface IWorkflowScriptReferenceNode : IWorkflowScriptNode
{
    /// <summary>获取脚本使用的 DLL 路径集合；持久化时应优先保存相对路径。</summary>
    IList<string> ScriptReferencePaths { get; set; }
}

/// <summary>由需要图像展示区域的节点实现，设计器据此自动增加图像页面。</summary>
public interface IWorkflowImageDisplayNode : IWorkflowNodeModel
{
    /// <summary>获取宿主图像源使用的稳定键。</summary>
    string ImageSourceKey { get; }
}

/// <summary>UI 无关的图像帧，像素数据的所有权归发布者，订阅者必须按需复制。</summary>
/// <param name="Width">图像宽度，单位为像素。</param>
/// <param name="Height">图像高度，单位为像素。</param>
/// <param name="Stride">相邻图像行起始地址之间的字节数。</param>
/// <param name="PixelFormat">像素内存的通道布局。</param>
/// <param name="Pixels">像素内存；订阅者不得假定事件返回后仍然有效。</param>
/// <param name="Timestamp">帧产生或转换完成的时间。</param>
/// <param name="FrameId">图像源内单调递增或可用于去重的帧标识。</param>
public sealed record WorkflowImageFrame(
    int Width,
    int Height,
    int Stride,
    WorkflowImagePixelFormat PixelFormat,
    ReadOnlyMemory<byte> Pixels,
    DateTimeOffset Timestamp,
    long FrameId);

/// <summary>工作流图像帧像素格式。</summary>
public enum WorkflowImagePixelFormat
{
    Gray8,
    Bgr24,
    Bgra32
}

/// <summary>宿主提供的异步图像帧源。</summary>
public interface IWorkflowImageFrameSource : IAsyncDisposable
{
    /// <summary>新图像帧可用时发生。</summary>
    event EventHandler<WorkflowImageFrame>? FrameAvailable;

    /// <summary>开始采集或订阅图像帧。</summary>
    /// <param name="cancellationToken">取消本次启动操作的令牌。</param>
    /// <returns>图像源已经进入可发布状态时完成的异步操作。</returns>
    ValueTask StartAsync(CancellationToken cancellationToken);

    /// <summary>停止采集或解除订阅；重复停止应保持幂等。</summary>
    /// <param name="cancellationToken">取消本次停止操作的令牌。</param>
    /// <returns>图像源不再发布新帧时完成的异步操作。</returns>
    ValueTask StopAsync(CancellationToken cancellationToken);
}

/// <summary>宿主按节点和图像源键解析视觉数据源。</summary>
public interface IWorkflowImageFrameSourceResolver
{
    /// <summary>解析指定节点对应的像素帧源。</summary>
    /// <param name="node">声明稳定图像源键的工作流节点。</param>
    /// <returns>可供 UI 启停和订阅的帧源；宿主不支持该节点时返回 <see langword="null"/>。</returns>
    IWorkflowImageFrameSource? Resolve(IWorkflowImageDisplayNode node);
}
