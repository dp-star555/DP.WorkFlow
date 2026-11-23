namespace DP.WorkFlow.UI;

/// <summary>把流程服务表里的 <see cref="IWorkflowPropertyChoiceSource"/> 接成属性面板的候选提供者。</summary>
public static class WorkflowHostPropertyChoices
{
    /// <summary>
    /// 创建候选提供者：<see cref="WorkflowPropertyEditorKeys.HostChoicePrefix"/> 开头的键依次问各来源，第一个回答的为准；
    /// 其他键交给 <paramref name="fallback"/>。
    /// </summary>
    /// <param name="sources">候选来源；顺序即优先级。</param>
    /// <param name="fallback">其他编辑器键的提供者，例如视觉源候选。</param>
    public static WorkflowPropertyChoiceProvider CreateProvider(IEnumerable<IWorkflowPropertyChoiceSource> sources, WorkflowPropertyChoiceProvider? fallback = null)
    {
        Guard.NotNull(sources);
        var list = sources.Where(source => source is not null).ToArray();
        return (editorKey, propertyName) =>
        {
            if (!editorKey.StartsWith(WorkflowPropertyEditorKeys.HostChoicePrefix, StringComparison.Ordinal))
                return fallback?.Invoke(editorKey, propertyName) ?? Array.Empty<WorkflowPropertyChoice>();
            foreach (var source in list)
            {
                IReadOnlyList<KeyValuePair<string, object?>>? choices;
                try { choices = source.GetChoices(editorKey, propertyName); }
                catch (Exception) { continue; }
                if (choices is not null)
                    return choices.Select(pair => new WorkflowPropertyChoice(pair.Key, pair.Value)).ToArray();
            }
            return Array.Empty<WorkflowPropertyChoice>();
        };
    }

    /// <summary>从流程服务表取全部候选来源（登记为 <c>IEnumerable&lt;IWorkflowPropertyChoiceSource&gt;</c> 或单个）。</summary>
    /// <param name="services">流程服务表。</param>
    public static IReadOnlyList<IWorkflowPropertyChoiceSource> FromServices(IServiceProvider services)
    {
        Guard.NotNull(services);
        if (services.GetService(typeof(IEnumerable<IWorkflowPropertyChoiceSource>)) is IEnumerable<IWorkflowPropertyChoiceSource> many)
            return many.ToArray();
        return services.GetService(typeof(IWorkflowPropertyChoiceSource)) is IWorkflowPropertyChoiceSource one
            ? new[] { one } : Array.Empty<IWorkflowPropertyChoiceSource>();
    }
}
