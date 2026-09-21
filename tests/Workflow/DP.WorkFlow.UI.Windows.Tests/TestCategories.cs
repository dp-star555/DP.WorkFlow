namespace DP.WorkFlow.Tests;

/// <summary>
/// 测试分类常量。分类写进 xUnit 的 Trait，因此可以用 <c>dotnet test</c> 的 <c>--filter</c> 精确挑选：
/// <code>
/// dotnet test tests/Workflow/DP.WorkFlow.UI.Windows.Tests -c Debug --filter "Category!=UiControls"   # 跳过控件库用例
/// dotnet test tests/Workflow/DP.WorkFlow.UI.Windows.Tests -c Debug --filter "Category=UiControls"    # 只跑控件库用例
/// </code>
/// <c>tools/Test-DPWorkFlow.ps1</c> 的 <c>-Suite</c> 参数就是按这两个过滤条件实现的。
/// </summary>
internal static class TestCategories
{
    /// <summary>分类维度名，必须与 <c>--filter</c> 里的键一致。</summary>
    internal const string Category = "Category";

    /// <summary>
    /// 现代控件库自身的用例：控件外观与行为、DPI 布局、控件本地化、控件元数据、脚本编辑器控件布局。
    /// <para>
    /// 判定标准是"它保护的是控件库契约，而不是业务流程"——这些用例只引用
    /// <c>ModernUI.WinForms</c>（含 <c>ModernPropertyGrid.WinForms</c>）、<c>ModernUI.Localization</c>
    /// 与 <c>ScriptEngine.WinForms</c>，不依赖节点、运行时或采集。
    /// 因此只有这些程序集的源码改动时才需要重跑，日常改动应当跳过。
    /// </para>
    /// <para>
    /// 与之相对，视觉管线、操作台窗口、Studio 节点编辑器渲染校验等用例虽然也寄宿在同一个
    /// Windows 测试宿主里，但它们会因为业务流程改动而失败，**不属于**本分类，默认照跑。
    /// </para>
    /// </summary>
    internal const string UiControls = "UiControls";
}
