using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DP.WorkFlow.Tests;

/// <summary>
/// Establishes the deterministic 96-DPI behavior-test process context before any WinForms HWND is
/// created. Real PMv2 coverage lives in the Gallery transition probes; these tests assert logical
/// metrics and must not inherit the invoking monitor or a previously reused test host.
/// </summary>
internal static class UiTestDpiBaseline
{
    private static readonly IntPtr DpiAwarenessContextUnaware = new(-1);

    internal static bool ProcessContextWasSet { get; private set; }
    internal static bool LoadingThreadIsUnaware { get; private set; }

    [ModuleInitializer]
    internal static void Initialize()
    {
        ProcessContextWasSet = SetProcessDpiAwarenessContext(DpiAwarenessContextUnaware);
        _ = SetThreadDpiAwarenessContext(DpiAwarenessContextUnaware);
        LoadingThreadIsUnaware = AreDpiAwarenessContextsEqual(
            GetThreadDpiAwarenessContext(), DpiAwarenessContextUnaware);
    }

    internal static bool IsCurrentThreadUnaware()
    {
        _ = SetThreadDpiAwarenessContext(DpiAwarenessContextUnaware);
        return AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), DpiAwarenessContextUnaware);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
}

/// <summary>Creates STA test threads with a deterministic DPI context even when vstest reuses an aware host process.</summary>
internal static class UiTestThread
{
    internal static Thread Create(ThreadStart action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new Thread(() =>
        {
            _ = UiTestDpiBaseline.IsCurrentThreadUnaware();
            // WinForms首次Control构造会缓存初始DPI。必须先在96-DPI上下文建立基线，
            // 否则第一个测试切到PMv2后会把144等系统DPI污染到后续无句柄控件。
            // 默认Font另有惰性静态缓存，仅读DeviceDpi不够；必须同时在96-DPI初始化字体。
            var autoInstall = System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall;
            System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall = false;
            try
            {
                using var baseline = new System.Windows.Forms.Control();
                _ = baseline.DeviceDpi;
                _ = baseline.Font;
            }
            finally { System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall = autoInstall; }
            action();
        });
    }
}

// 控件库用例：本类直接驱动控件行为用例来验证 96-DPI 进程基线，属于控件库测试的一部分。
[Trait(TestCategories.Category, TestCategories.UiControls)]
public sealed class UiTestInfrastructureTests
{
    [Fact]
    public void HighDpiFirstUse_DoesNotPoisonLogicalDpiTests()
    {
        // 使用单独测试进程筛选本用例，可确定性复现先初始化PMv2再创建96-DPI控件的顺序。
        new DpiLayoutRegressionTests().ComboBox_HighDpiChromeDoesNotOverlapSelectedTextLine();
        new ModernControlBehaviorTests().ModernTreeView_ModernScrollChromeDoesNotExposeNativeRectangleBars();
        new ModernControlBehaviorTests().RichTextBoxKeepsReadableFontScaleAfterHandleCreationAt96Dpi();
        new ModernControlBehaviorTests().TimePickerUsesFullHeightModernSpinnerAndPreservesNativeStepSemantics();
        new DpiLayoutRegressionTests().ModernCompositeControls_DoNotDoubleScaleDirectChildren();
    }

    [Fact]
    public void BehaviorTestProcess_UsesDeterministic96DpiContext()
    {
        Assert.True(UiTestDpiBaseline.LoadingThreadIsUnaware,
            "The test assembly loading thread did not establish DPI_AWARENESS_CONTEXT_UNAWARE.");
        Assert.True(UiTestDpiBaseline.IsCurrentThreadUnaware(),
            "The behavior-test execution thread is not DPI_AWARENESS_CONTEXT_UNAWARE.");
    }
}
