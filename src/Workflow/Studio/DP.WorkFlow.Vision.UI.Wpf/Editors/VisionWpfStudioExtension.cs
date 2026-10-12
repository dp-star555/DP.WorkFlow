using DP.WorkFlow.UI.Wpf;

namespace DP.WorkFlow.Vision.UI.Wpf;

/// <summary>注册唯一的新版DP.Vision WPF页面与原生Renderer。</summary>
public sealed class VisionWpfStudioExtension : IWorkflowWpfStudioExtension
{
    /// <summary>运行图像与事实预览源。</summary>
    public IWorkflowVisionPreviewSource? FrameSource { get; init; }
    /// <summary>用户显式请求的文件预览读取器。</summary>
    public DP.Vision.Algorithms.IImageFileReader? FileReader { get; init; }
    /// <summary>节点内模板制作使用的引擎和资源上下文。</summary>
    public VisionTemplateEditingRuntime? Templates { get; init; }
    /// <summary>画布工具栏“运行”使用的单节点试运行器；为空时不显示“运行”。</summary>
    public WorkflowNodeTrialRunner? NodeRunner { get; init; }
    /// <inheritdoc/>
    public string ExtensionId => "Vision.Studio.Wpf";
    /// <inheritdoc/>
    public void Register(WorkflowWpfStudioExtensionCatalog extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        extensions.RegisterPageProvider(new VisionFrameEditorPageProvider(FrameSource, FileReader, Templates, NodeRunner))
            .RegisterRenderer(new VisionFrameEditorRenderer())
            .RegisterRenderer(new VisionTemplateAuthoringRenderer());
    }
}
