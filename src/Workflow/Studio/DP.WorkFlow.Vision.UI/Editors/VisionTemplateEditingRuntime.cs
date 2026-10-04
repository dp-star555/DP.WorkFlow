using DP.Vision.Algorithms;

namespace DP.WorkFlow.Vision.UI;

/// <summary>配方中的已发布模板版本；按引用选择，不在列举时加载原生模型。</summary>
public sealed record VisionTemplateResourceChoice(string Reference, string Label)
{
    /// <inheritdoc/>
    public override string ToString() => Label;
}

/// <summary>制作界面使用宿主已经发现的引擎，保持租约直到制作结束。</summary>
public sealed class VisionTemplateEditingRuntime(VisionAlgorithmCatalog catalog, VisionAlgorithmRuntime runtime,
    Func<VisionAlgorithmResourceContext?> resources)
{
    /// <summary>当前资源解析上下文。</summary>
    public VisionAlgorithmResourceContext? Resources => resources();
    /// <summary>只查询支持节点能力的制作实现，不初始化引擎。</summary>
    public IReadOnlyList<VisionAlgorithmDescriptor> Choices(bool pose) => catalog.Implementations.Where(d =>
        d.ContractType == typeof(IPreparedVisionTemplateMatcher) && d.Factory is IVisionTemplateFactoryDescription
        && d.Features.Contains("translation") && (!pose || d.Features.Contains("rotation") && d.Features.Contains("scale"))).ToArray();
    /// <summary>获取所选实现的描述。</summary>
    public VisionAlgorithmDescriptor Descriptor(string id) => catalog.GetRequired(id);
    /// <summary>显式列举配方资源；只读清单，每项损坏不影响其他模板，最多展示256个版本。</summary>
    public Task<IReadOnlyList<VisionTemplateResourceChoice>> ListResourcesAsync(bool pose, CancellationToken token)
    {
        var directory = Resources?.RecipeDirectory;
        var compatible = Choices(pose).Select(d => d.ImplementationId).ToHashSet(StringComparer.Ordinal);
        return Task.Run<IReadOnlyList<VisionTemplateResourceChoice>>(() =>
        {
            var result = new List<VisionTemplateResourceChoice>();
            if (directory == null) return result;
            var root = Path.Combine(directory, "Resources", "Templates");
            if (!Directory.Exists(root)) return result;
            var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
            foreach (var asset in Directory.EnumerateDirectories(root, "*", options).OrderBy(p => p, StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                if (!Guid.TryParseExact(Path.GetFileName(asset), "N", out _)) continue;
                var revisions = Path.Combine(asset, "revisions");
                if (!Directory.Exists(revisions) || (File.GetAttributes(revisions) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (var revision in Directory.EnumerateDirectories(revisions, "*", options).OrderByDescending(p => p, StringComparer.Ordinal))
                {
                    token.ThrowIfCancellationRequested();
                    if (!Guid.TryParseExact(Path.GetFileName(revision), "N", out _)) continue;
                    var path = Path.Combine(revision, "manifest.json");
                    var reference = Path.GetRelativePath(directory, path);
                    try
                    {
                        var manifest = VisionTemplateStore.Inspect(path);
                        var state = compatible.Contains(manifest.ImplementationId) ? "" : "（引擎未安装或不兼容）";
                        result.Add(new(reference, $"{manifest.TemplateId[..8]} / {manifest.ImplementationId} / 版本 {manifest.RevisionId[..8]} / 参考v{manifest.Definition.ReferenceVersion}{state}"));
                    }
                    catch (Exception ex) when (ex is IOException or ArgumentException or System.Runtime.Serialization.SerializationException)
                    { result.Add(new(reference, $"{Path.GetFileName(asset)[..8]} / 模板损坏：{ex.Message}")); }
                    if (result.Count == 256) return result;
                }
            }
            return result;
        }, token);
    }
    /// <summary>在后台捕获模型快照；不发布资源。</summary>
    public Task<VisionTemplateBuild> BuildAsync(string id, VisionTemplateBuildRequest request, CancellationToken token) => Task.Run(async () =>
    {
        var factory = Descriptor(id).Factory as IVisionTemplateFactoryDescription ?? throw new InvalidOperationException("所选实现不支持模板制作。");
        var expectedDefinition = VisionTemplateStore.CopyDefinition(request.Definition);
        using var plan = await runtime.PrepareAsync(new[] { new VisionAlgorithmRequest("template-build", typeof(IVisionTemplateBuilder),
            new VisionAlgorithmSelection { ImplementationId = factory.BuilderImplementationId }) }, Resources, token).ConfigureAwait(false);
        var build = await plan.InvokeAsync<IVisionTemplateBuilder, VisionTemplateBuild>("template-build", (builder, cancellation) => builder.BuildAsync(request, cancellation), token).ConfigureAwait(false);
        if (build.ImplementationId != id) throw new InvalidOperationException("制作结果与所选匹配实现不一致。");
        if (build.Definition.GeometrySignature() != expectedDefinition.GeometrySignature() || build.Definition.ReferenceVersion != expectedDefinition.ReferenceVersion
            || build.Definition.ReferenceIdentity != expectedDefinition.ReferenceIdentity)
            throw new InvalidOperationException("引擎不能改变用户设置的共同参考几何。");
        return new VisionTemplateBuild(build.ImplementationId, build.Format, build.Definition, build.Settings,
            build.Files.Where(f => f.Name != "source/image.bin" && f.Name != "source/regions.xml")
                .Concat(new[] { new VisionTemplateArtifact("source/image.bin", VisionTemplateSource.Encode(request.Source.Image, token)) }));
    }, token);
    /// <summary>创建由编辑器独立拥有的试匹配模型。</summary>
    public Task<VisionAlgorithmResource> PreviewAsync(VisionTemplateBuild build, CancellationToken token) => Task.Run(async () =>
    {
        var factory = Descriptor(build.ImplementationId).Factory as IVisionTemplatePreviewFactory ?? throw new InvalidOperationException("该实现没有提供草稿试匹配。");
        return await factory.PreparePreviewAsync(build, token).ConfigureAwait(false);
    }, token);
}
