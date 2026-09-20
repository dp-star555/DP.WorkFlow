namespace DP.WorkFlow;

/// <summary>工作流属性编辑器稳定键。节点只声明编辑语义，不依赖具体 UI 技术。</summary>
public static class WorkflowPropertyEditorKeys
{
    /// <summary>使用文件选择对话框编辑字符串路径。</summary>
    public const string FilePath = "FilePath";

    /// <summary>使用文件夹选择对话框编辑字符串路径。</summary>
    public const string FolderPath = "FolderPath";

    /// <summary>从宿主已发布的候选集中选择；只对逻辑标识类属性有效，不允许自由文本。</summary>
    public const string VisionSource = "VisionSource";
}

/// <summary>为节点属性显式指定跨 WinForms/WPF 的专用编辑器。</summary>
/// <param name="editorKey">宿主和共享属性模型约定的稳定编辑器键。</param>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class WorkflowPropertyEditorAttribute(string editorKey) : Attribute
{
    /// <summary>获取专用编辑器稳定键。</summary>
    public string EditorKey { get; } = string.IsNullOrWhiteSpace(editorKey)
        ? throw new ArgumentException("属性编辑器键不能为空。", nameof(editorKey))
        : editorKey.Trim();

    /// <summary>文件选择过滤器，例如“图像文件|*.bmp;*.png|所有文件|*.*”。</summary>
    public string? Filter { get; set; }

    /// <summary>选择窗口标题。</summary>
    public string? DialogTitle { get; set; }

    /// <summary>提交非空路径时是否验证文件或目录存在。</summary>
    public bool CheckExists { get; set; }
}
