using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>旧配方默认绑定图像，新制作使用已发布资源。</summary>
public enum EWorkflowVisionTemplateSource
{
    /// <summary>上游动态模板图像。</summary>
    ImageBinding,
    /// <summary>确定版本的本地模板资源。</summary>
    Resource
}

/// <summary>节点内模板制作的共同编辑约定，不依赖具体引擎。</summary>
public interface IWorkflowVisionTemplateNode
{
    /// <summary>模板来源模式。</summary>
    EWorkflowVisionTemplateSource TemplateSource { get; set; }
    /// <summary>资源匹配实现与清单引用。</summary>
    VisionAlgorithmSelection ModelAlgorithm { get; set; }
    /// <summary>模板稳定资源身份。</summary>
    string TemplateResourceId { get; set; }
    /// <summary>输出坐标系稳定身份。</summary>
    string CoordinateSystemId { get; set; }
    /// <summary>是否要求旋转尺度搜索。</summary>
    bool RequiresPoseSearch { get; }
    /// <summary>节点应用时确认的参考定义，独立于资源路径和引擎配置。</summary>
    VisionTemplateDefinition? TemplateReferenceDefinition { get; set; }
    /// <summary>应用时记录的样图内容身份，用于替换样图时确认参考版本。</summary>
    string TemplateSourceHash { get; set; }
}

/// <summary>共同模型调用，运行前已捕获资源和租约。</summary>
internal static class WorkflowVisionTemplateResource
{
    internal static WorkflowVisionAlgorithmSlot Slot(IWorkflowVisionTemplateNode node) => new("model", typeof(IPreparedVisionTemplateMatcher), node.ModelAlgorithm,
        node.RequiresPoseSearch
            ? node is LocateVisionTemplatePoseNodeModel pose && (Math.Abs(pose.MinimumScale - 1) > 1e-9 || Math.Abs(pose.MaximumScale - 1) > 1e-9)
                ? new[] { "translation", "rotation", "scale" } : new[] { "translation", "rotation" }
            : new[] { "translation" });
    internal static IReadOnlyList<string> Validate(IWorkflowVisionTemplateNode node)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(node.TemplateSource)) errors.Add("模板来源未定义。");
        if (node.TemplateReferenceDefinition != null)
            try { node.TemplateReferenceDefinition.Validate(); } catch (ArgumentException error) { errors.Add(error.Message); }
        if (node.TemplateSource == EWorkflowVisionTemplateSource.Resource && (node.ModelAlgorithm?.Settings == null
            || !node.ModelAlgorithm.Settings.TryGetValue("templatePath", out var path) || string.IsNullOrWhiteSpace(path))) errors.Add("请在节点内制作模板或选择已发布的模板清单。");
        return errors;
    }
    internal static TemplatePoseResult Match(IWorkflowVisionTemplateNode node, IWorkflowNodeExecutionContext context,
        ImageFrame frame, PixelBounds bounds, RegionGeometry? region, VisionCoordinateSystem? parent, TemplatePoseOptions options, CancellationToken token)
    {
        var bindings = context.Services.GetService(typeof(IWorkflowVisionAlgorithmBindings)) as IWorkflowVisionAlgorithmBindings
            ?? throw new InvalidOperationException("模板资源需要宿主注册算法绑定服务。");
        return bindings.Invoke<IPreparedVisionTemplateMatcher, TemplatePoseResult>(context, "model", matcher =>
        {
            if (node.TemplateReferenceDefinition is { } expected && expected.CoordinateDefinition(node.CoordinateSystemId).Signature != matcher.Definition.CoordinateDefinition(node.CoordinateSystemId).Signature)
                throw new InvalidOperationException("模板资源参考定义与节点确认内容不一致，请在节点内读取并确认资源。");
            var result = matcher.Match(frame, bounds, options, region, token).InReferenceCoordinates(node.CoordinateSystemId, frame, matcher.Definition, matcher.ModelIdentity);
            return parent == null ? result : result.WithSearchCoordinates(parent);
        }, token);
    }
}
