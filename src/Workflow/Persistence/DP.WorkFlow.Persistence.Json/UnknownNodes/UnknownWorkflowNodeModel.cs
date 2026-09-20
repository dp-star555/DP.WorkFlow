using System.Text.Json;

namespace DP.WorkFlow.Persistence.Json;

/// <summary>
/// 表示当前宿主尚未安装对应插件的节点。
/// 原始配置会被保留，以便安装插件后恢复或由迁移工具修复。
/// </summary>
public sealed class UnknownWorkflowNodeModel : WorkflowNodeModel
{
    /// <summary>初始化未知节点。</summary>
    public UnknownWorkflowNodeModel(string nodeType, int nodeVersion, JsonElement rawConfig)
    {
        if (string.IsNullOrWhiteSpace(nodeType))
            throw new ArgumentException("未知节点仍必须具有 NodeType。", nameof(nodeType));
        NodeTypeValue = nodeType;
        NodeVersion = nodeVersion;
        RawConfig = rawConfig.Clone();
    }

    /// <summary>获取保存的节点类型键。</summary>
    public string NodeTypeValue { get; }

    /// <inheritdoc />
    public override string NodeType => NodeTypeValue;

    /// <summary>获取保存的节点配置版本。</summary>
    public int NodeVersion { get; }

    /// <summary>获取未解释的原始 JSON 配置。</summary>
    public JsonElement RawConfig { get; }
}
