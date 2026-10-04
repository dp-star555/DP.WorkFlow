using System.Text.Json;
using DP.Vision.Algorithms;

namespace DP.WorkFlow.Vision.UI;

/// <summary>机器级资源目录配置；模型及算法选择仍保存于配方。</summary>
public sealed class WorkflowVisionAlgorithmEnvironment
{
    /// <summary>机器配置格式版本。</summary>
    public int SettingsVersion { get; set; } = 1;
    /// <summary>绝对路径或相对于机器配置文件的资源目录。</summary>
    public string ResourceDirectory { get; set; } = "VisionResources";
    /// <summary>读取可选机器配置；不存在时使用宿主旁的 VisionResources。</summary>
    public static WorkflowVisionAlgorithmEnvironment Load(string hostDirectory)
    {
        var root = Path.GetFullPath(hostDirectory);
        var file = Path.Combine(root, "algorithm-environment.json");
        var settings = File.Exists(file)
            ? JsonSerializer.Deserialize<WorkflowVisionAlgorithmEnvironment>(File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("算法机器配置为空。")
            : new WorkflowVisionAlgorithmEnvironment();
        if (settings.SettingsVersion != 1 || string.IsNullOrWhiteSpace(settings.ResourceDirectory))
            throw new InvalidOperationException("算法机器配置只支持版本1，且资源目录不能为空。");
        settings.ResourceDirectory = Path.GetFullPath(Path.Combine(root, settings.ResourceDirectory));
        return settings;
    }
    /// <summary>捕获当前已保存配方的解析上下文；未保存时禁止普通相对模型路径。</summary>
    public VisionAlgorithmResourceContext Capture(string? recipeFilePath) => new(
        string.IsNullOrWhiteSpace(recipeFilePath) ? null : Path.GetDirectoryName(Path.GetFullPath(recipeFilePath)), ResourceDirectory);
}
