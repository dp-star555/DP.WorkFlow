namespace ScriptEngine.WinForms.Demo;

/// <summary>模拟工作流宿主传给脚本的业务数据。</summary>
public sealed class DemoHostContext
{
    public string BatchName { get; init; } = "LOT-2025-001";

    public int PassThreshold { get; init; } = 20;
}
