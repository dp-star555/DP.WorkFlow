namespace DP.WorkFlow;

/// <summary>标准节点列出公共数据用途的辅助方法；键为空时不返回。</summary>
internal static class PublicDataUses
{
    internal static IEnumerable<WorkflowPublicDataDeclaration> Signal(string? key, bool defaultValue = false) =>
        string.IsNullOrWhiteSpace(key)
            ? Array.Empty<WorkflowPublicDataDeclaration>()
            : new[] { WorkflowPublicDataDeclaration.Signal(key!.Trim(), defaultValue) };

    internal static IEnumerable<WorkflowPublicDataDeclaration> Value(string? key, E_SignalValueType valueType, string? defaultValueText = null) =>
        string.IsNullOrWhiteSpace(key)
            ? Array.Empty<WorkflowPublicDataDeclaration>()
            : new[] { WorkflowPublicDataDeclaration.Value(key!.Trim(), valueType, string.IsNullOrEmpty(defaultValueText) ? null : defaultValueText) };

    internal static IEnumerable<WorkflowPublicDataDeclaration> Queue(string? key, E_SignalValueType valueType) =>
        string.IsNullOrWhiteSpace(key)
            ? Array.Empty<WorkflowPublicDataDeclaration>()
            : new[] { WorkflowPublicDataDeclaration.Queue(key!.Trim(), valueType) };
}
