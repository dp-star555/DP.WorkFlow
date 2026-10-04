using DP.WorkFlow.UI.WinForms;

namespace DP.WorkFlow.Vision.UI.WinForms;

/// <summary>注册唯一的新版DP.Vision WinForms页面与原生Renderer。</summary>
public sealed class VisionWinFormsStudioExtension : IWorkflowWinFormsStudioExtension
{
    /// <summary>运行图像与事实预览源。</summary>
    public IWorkflowVisionPreviewSource? FrameSource { get; init; }
    /// <summary>用户显式请求的文件预览读取器。</summary>
    public DP.Vision.Algorithms.IImageFileReader? FileReader { get; init; }
    /// <summary>节点内模板制作使用的引擎和资源上下文。</summary>
    public VisionTemplateEditingRuntime? Templates { get; init; }
    /// <inheritdoc/>
    public string ExtensionId => "Vision.Studio.WinForms";
    /// <inheritdoc/>
    public void Register(WorkflowWinFormsStudioExtensionCatalog extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        extensions.RegisterPageProvider(new VisionFrameEditorPageProvider(FrameSource, FileReader, Templates))
            .RegisterRenderer(new VisionFrameEditorRenderer())
            .RegisterRenderer(new VisionTemplateAuthoringRenderer());
    }
}
