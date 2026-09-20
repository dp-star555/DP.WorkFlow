namespace DP.WorkFlow;

/// <summary>工作流 C# 程序可使用的稳定宿主上下文，不依赖具体脚本节点包。</summary>
public interface IWorkflowScriptHostContext
{
    /// <summary>获取当前节点执行上下文。</summary>
    IWorkflowNodeExecutionContext Context { get; }

    /// <summary>获取本次脚本执行取消令牌。</summary>
    CancellationToken CancellationToken { get; }

    /// <summary>获取写入节点 Trace 的脚本控制台。</summary>
    IWorkflowScriptConsole Console { get; }

    /// <summary>解析宿主注册依赖。</summary>
    T? GetService<T>() where T : class;

    /// <summary>读取流程局部变量。</summary>
    T? GetVariable<T>(string key);

    /// <summary>暂存流程局部变量。</summary>
    void SetVariable(string key, object value);

    /// <summary>删除流程局部变量。</summary>
    bool RemoveVariable(string key);

    /// <summary>暂存公共数据发布。</summary>
    void PublishData(string key, object value);

    /// <summary>写入脚本 Trace。</summary>
    void Trace(string message);
}

/// <summary>将脚本输出写入工作流 Trace 的稳定控制台接口。</summary>
public interface IWorkflowScriptConsole
{
    void Write(object? value);
    void Write(string? value);
    void WriteLine();
    void WriteLine(object? value);
    void WriteLine(string? value);
    void WriteLine(string format, params object?[] args);
}
