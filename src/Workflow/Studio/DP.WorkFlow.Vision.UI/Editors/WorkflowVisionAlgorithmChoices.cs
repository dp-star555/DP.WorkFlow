using DP.Vision.Algorithms;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.Vision.UI;

/// <summary>把已发现的算法实现投影成两个桌面宿主共用的属性候选。</summary>
public static class WorkflowVisionAlgorithmChoices
{
    /// <summary>将算法候选与现有采集等候选提供者组合。</summary>
    /// <param name="catalog">启动时冻结的算法目录。</param>
    /// <param name="fallback">其他属性的候选提供者。</param>
    /// <returns>按能力过滤的实现候选。</returns>
    public static WorkflowPropertyChoiceProvider CreateProvider(VisionAlgorithmCatalog catalog, WorkflowPropertyChoiceProvider? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return (key, property) => key.StartsWith(WorkflowPropertyEditorKeys.VisionAlgorithmPrefix, StringComparison.Ordinal)
            ? catalog.Implementations.Where(d => d.CapabilityId == key[WorkflowPropertyEditorKeys.VisionAlgorithmPrefix.Length..])
                .Select(d => new WorkflowPropertyChoice($"{d.Engine} / {d.ImplementationId}", d.ImplementationId)).ToArray()
            : fallback?.Invoke(key, property) ?? Array.Empty<WorkflowPropertyChoice>();
    }
}
