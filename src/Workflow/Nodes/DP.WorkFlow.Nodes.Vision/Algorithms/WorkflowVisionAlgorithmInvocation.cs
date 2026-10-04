using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>统一节点调用入口；旧宿主仅允许原实现及空初始化配置。</summary>
internal static class WorkflowVisionAlgorithmInvocation
{
    public static TResult Invoke<T, TResult>(IWorkflowNodeExecutionContext context, VisionAlgorithmSelection selection,
        string legacyImplementation, Func<T, TResult> invoke, CancellationToken token) where T : class
    {
        if (context.Services.GetService(typeof(IWorkflowVisionAlgorithmBindings)) is IWorkflowVisionAlgorithmBindings bindings)
            return bindings.Invoke(context, "algorithm", invoke, token);
        RequireLegacySelection(selection, legacyImplementation);
        return invoke(context.GetRequiredCapability<T>());
    }

    public static Task<TResult> InvokeAsync<T, TResult>(IWorkflowNodeExecutionContext context, VisionAlgorithmSelection selection,
        string legacyImplementation, Func<T, CancellationToken, Task<TResult>> invoke, CancellationToken token) where T : class
    {
        if (context.Services.GetService(typeof(IWorkflowVisionAlgorithmBindings)) is IWorkflowVisionAlgorithmBindings bindings)
            return bindings.InvokeAsync(context, "algorithm", invoke, token);
        RequireLegacySelection(selection, legacyImplementation);
        return invoke(context.GetRequiredCapability<T>(), token);
    }

    public static void RequireLegacySelection(VisionAlgorithmSelection selection, string legacyImplementation)
    {
        if (selection is null || selection.ImplementationId != legacyImplementation || selection.SettingsVersion != 1
            || selection.Settings is null || selection.Settings.Count != 0 || selection.Dependencies is null || selection.Dependencies.Count != 0)
            throw new InvalidOperationException("宿主未注册算法绑定服务，无法应用节点的引擎选择或初始化配置。");
    }
}

/// <summary>供制作界面读取图像；读取时准备显式选择并持有租约到异步解码结束。</summary>
public sealed class WorkflowVisionImageFileReader : IImageFileReader
{
    private readonly VisionAlgorithmRuntime _runtime;
    private readonly VisionAlgorithmSelection _selection;

    /// <summary>读取器不拥有运行时；宿主须在制作界面停止读取之后释放运行时。</summary>
    public WorkflowVisionImageFileReader(VisionAlgorithmRuntime runtime, VisionAlgorithmSelection selection)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
    }

    /// <inheritdoc/>
    public async Task<IImageSource> ReadAsync(string path, CancellationToken token = default)
    {
        using var plan = await _runtime.PrepareAsync(new[] { new VisionAlgorithmRequest("editor", typeof(IImageFileReader), _selection) }, token).ConfigureAwait(false);
        return await plan.InvokeAsync<IImageFileReader, IImageSource>("editor", (reader, cancellation) => reader.ReadAsync(path, cancellation), token).ConfigureAwait(false);
    }
}
