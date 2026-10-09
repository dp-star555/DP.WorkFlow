using DP.Vision;
using DP.Vision.Algorithms;
using PixelRect = DP.Vision.Algorithms.PixelBounds;

namespace DP.WorkFlow.LabelInspection;

// 宿主范围内共享，不使用全进程static资源；生产与配置页的独立宿主不会互相释放。
internal sealed class WorkflowLabelInspectionModels : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);
    private readonly LabelInspectionResourceCache<Model> _cache;
    private readonly Func<string, ITextLineRecognizer> _ocr;
    private readonly Func<string, IPatchAnomalyDetector> _anomaly;
    internal WorkflowLabelInspectionModels(WorkflowLabelInspectionCacheOptions options,
        Func<string, ITextLineRecognizer>? ocr = null, Func<string, IPatchAnomalyDetector>? anomaly = null)
    {
        _cache = new(options.MaximumCachedModels, options.MaximumConcurrentLoads, options.IdleExpiration, options.TimeProvider);
        _ocr = ocr ?? (path => new DP.Vision.PPOcr.Onnx.OnnxTextLineRecognizer(path, new DP.Vision.OpenCv.OpenCvTextLinePreprocessor()));
        _anomaly = anomaly ?? (path => new DP.Vision.OpenCv.OpenCvCnnPatchAnomalyDetector(path));
    }
    internal int Count => _cache.Count;
    internal int Loads => _cache.Loads;
    internal int Hits => _cache.Hits;
    internal int Evictions => _cache.Evictions;
    internal void Cleanup(bool allIdle = false) => _cache.Cleanup(allIdle);

    internal async Task<LabelInspectionResourceCache<Model>.Lease?> AcquireAsync(string root, string relative, bool ocr, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;
        var path = WorkflowLabelInspectionResources.ResolvePath(root, relative);
        var info = new FileInfo(path);
        var stamp = $"{ocr}|{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        string? hash;
        lock (_gate) _aliases.TryGetValue(stamp, out hash);
        byte[]? bytes = null;
        if (hash is null)
        {
            bytes = await Task.Run(() => WorkflowLabelInspectionResources.ReadBounded(path, 256 * 1024 * 1024, token), token).ConfigureAwait(false);
            hash = WorkflowLabelInspectionResources.Hash(bytes);
            lock (_gate) { if (_aliases.Count >= 256) _aliases.Clear(); _aliases[stamp] = hash; }
        }
        // 角色、CPU实现及固定推理参数也是身份的一部分；相同内容的不同路径可以共享。
        var key = (ocr ? "ppocr-cpu-default:" : "opencv-cnn-scale2:") + hash;
        var expected = hash;
        var captured = bytes;
        return await _cache.AcquireAsync(key, lifetime => Task.Run(() =>
        {
            lifetime.ThrowIfCancellationRequested();
            var content = captured ?? WorkflowLabelInspectionResources.ReadBounded(path, 256 * 1024 * 1024, lifetime);
            if (WorkflowLabelInspectionResources.Hash(content) != expected)
                throw new InvalidDataException("模型在加载期间发生变化，请重试：" + path);
            var directory = Path.Combine(Path.GetTempPath(), "workflow-label-model-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var snapshot = Path.Combine(directory, "model.onnx"); File.WriteAllBytes(snapshot, content);
                lifetime.ThrowIfCancellationRequested();
                return ocr ? new Model(directory, expected, _ocr(snapshot), null) : new Model(directory, expected, null, _anomaly(snapshot));
            }
            catch { WorkflowLabelInspectionResources.DeleteTemporary(directory); throw; }
        }, lifetime), token).ConfigureAwait(false);
    }
    internal IAnomalyImplementation Share(IAnomalyImplementation implementation) => new SharedImplementation(this, implementation);
    private sealed class SharedImplementation(WorkflowLabelInspectionModels owner, IAnomalyImplementation inner) : IAnomalyImplementation
    {
        public string ImplementationId => inner.ImplementationId;
        public string DisplayName => inner.DisplayName;
        public ILoadedAnomalyModel Load(AnomalyModelAsset asset, CancellationToken token = default)
        {
            var hash = asset.ContentSha256;
            // 完整资产已含格式、预处理、阈值、配置及全部原生文件。部署策略固定CPU/v1，不跨设备共享。
            string key = "anomaly-runtime-cpu-v1:" + ImplementationId + ":" + asset.ModelFormat + ":" + hash;
            var lease = owner._cache.AcquireAsync(key, lifetime => Task.Run(() => new Model(hash, inner.Load(asset, lifetime)), lifetime), token)
                .GetAwaiter().GetResult();
            return new BorrowedRuntime(lease);
        }
    }
    private sealed class BorrowedRuntime(LabelInspectionResourceCache<Model>.Lease lease) : ILoadedAnomalyModel
    {
        public AnomalyModelAsset Asset => lease.Value.Runtime!.Asset;
        public PatchAnomalyResult Inspect(IImageSource image, AnomalyDetectionOptions options, CancellationToken token = default)
        { lease.Touch(); lock (lease.Value) { token.ThrowIfCancellationRequested(); return lease.Value.Runtime!.Inspect(image, options, token); } }
        public void Dispose() => lease.Dispose();
    }
    public ValueTask DisposeAsync() => _cache.DisposeAsync();

    internal sealed class Model : IDisposable
    {
        private readonly string _directory;
        private readonly IDisposable? _native;
        internal Model(string directory, string hash, ITextLineRecognizer? ocr, IPatchAnomalyDetector? anomaly)
        {
            _directory = directory; Hash = hash; _native = (ocr as IDisposable) ?? (anomaly as IDisposable);
            Recognizer = ocr is null ? null : new SerialRecognizer(ocr);
            Anomaly = anomaly is null ? null : new SerialAnomaly(anomaly);
        }
        internal Model(string hash, ILoadedAnomalyModel runtime) { _directory = ""; Hash = hash; _native = runtime; Runtime = runtime; }
        internal ILoadedAnomalyModel? Runtime { get; }
        internal string Hash { get; }
        internal ITextLineRecognizer? Recognizer { get; }
        internal IPatchAnomalyDetector? Anomaly { get; }
        internal string AnomalyFeatureSource => $"cnn:{Hash[..16]}@2";
        public void Dispose()
        { try { _native?.Dispose(); } finally { WorkflowLabelInspectionResources.DeleteTemporary(_directory); } }
    }
    // 在完整算法调用范围串行化，而非只锁Forward；原生输出读取、派生缓存及训练也不可互相覆盖。
    private sealed class SerialRecognizer(ITextLineRecognizer inner) : ITextLineRecognizer
    {
        private readonly object _sync = new();
        public void Dispose() { /* 借用代理：原生实例仅由模型缓存释放。 */ }
        public TextLineRecognition Recognize(IImageSource frame, PixelRect bounds, CancellationToken token)
        { lock (_sync) { token.ThrowIfCancellationRequested(); return inner.Recognize(frame, bounds, token); } }
    }
    private sealed class SerialAnomaly(IPatchAnomalyDetector inner) : IPatchAnomalyDetector
    {
        private readonly object _sync = new();
        public PatchAnomalyModel Train(IReadOnlyList<IImageSource> good, PatchAnomalyOptions options, CancellationToken token = default)
        { lock (_sync) { token.ThrowIfCancellationRequested(); return inner.Train(good, options, token); } }
        public PatchAnomalyResult Detect(IImageSource actual, PatchAnomalyModel model, PatchAnomalyOptions options, CancellationToken token = default)
        { lock (_sync) { token.ThrowIfCancellationRequested(); return inner.Detect(actual, model, options, token); } }
    }
}
