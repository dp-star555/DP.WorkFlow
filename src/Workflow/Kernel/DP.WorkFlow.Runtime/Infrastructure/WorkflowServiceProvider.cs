using System.Collections.Concurrent;

namespace DP.WorkFlow;

/// <summary>
/// 用于简单宿主和测试的轻量服务容器。生产程序也可以传入 Microsoft DI 的 IServiceProvider。
/// </summary>
public sealed class WorkflowServiceProvider : IServiceProvider
{
    private readonly ConcurrentDictionary<Type, object> _services = new();

    /// <summary>按声明的服务类型注册或替换单例对象。</summary>
    /// <typeparam name="TService">调用方希望节点按其解析服务的类型，通常为接口。</typeparam>
    /// <param name="service">在该容器生命周期内复用的非空实例。</param>
    /// <returns>当前容器实例，便于链式注册。</returns>
    public WorkflowServiceProvider Add<TService>(TService service) where TService : class
    {
        Guard.NotNull(service);
        _services[typeof(TService)] = service;
        return this;
    }

    /// <summary>
    /// 追加一个属性候选来源。多方都可以追加，按追加顺序登记为 <c>IEnumerable&lt;IWorkflowPropertyChoiceSource&gt;</c>，
    /// 不会互相覆盖（<see cref="Add{TService}"/> 按类型只留一个）。
    /// </summary>
    /// <param name="source">候选来源。</param>
    /// <returns>当前容器实例，便于链式注册。</returns>
    public WorkflowServiceProvider AddPropertyChoices(IWorkflowPropertyChoiceSource source)
    {
        Guard.NotNull(source);
        _services.AddOrUpdate(typeof(IEnumerable<IWorkflowPropertyChoiceSource>),
            _ => new[] { source },
            (_, existing) => ((IEnumerable<IWorkflowPropertyChoiceSource>)existing).Append(source).ToArray());
        return this;
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType)
    {
        Guard.NotNull(serviceType);
        return _services.TryGetValue(serviceType, out var service) ? service : null;
    }
}
