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
        ArgumentNullException.ThrowIfNull(service);
        _services[typeof(TService)] = service;
        return this;
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return _services.TryGetValue(serviceType, out var service) ? service : null;
    }
}
