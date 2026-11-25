namespace DP.WorkFlow;

/// <summary>
/// 节点按宿主服务检查自己引用的外部对象（轴、IO 点、气缸、读码器等）是否存在。
/// 和 <see cref="IWorkflowNodeConfigurationValidator"/> 不同，结果取决于当前机器的装配：
/// 宿主没有提供对应目录时节点应当跳过检查（返回空），而不是报错。发布和试运行前由宿主调用。
/// </summary>
public interface IWorkflowNodeEnvironmentValidator
{
    /// <summary>返回找不到或不可用的引用；全部可用或无法检查时返回空集合。</summary>
    /// <param name="services">流程用的宿主服务表。</param>
    IReadOnlyList<string> ValidateEnvironment(IServiceProvider services);
}
