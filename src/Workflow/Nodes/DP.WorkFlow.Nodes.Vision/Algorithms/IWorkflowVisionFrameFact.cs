namespace DP.WorkFlow;

/// <summary>外部视觉节点的同帧事实及通用结果说明。</summary>
public interface IWorkflowVisionFrameFact
{
    /// <summary>本次输入帧身份。</summary>
    string FrameId { get; }
    /// <summary>无需具体插件类型即可显示的结果说明。</summary>
    string Summary { get; }
}
