using DP.Vision;
using DP.Vision.Acquisition;

namespace DP.WorkFlow.Tests;

/// <summary>
/// 生产者图像句柄的释放记录。
/// <para>
/// 断言"帧被释放"必须观察真实资源：被测对象自己维护的计数器即使不释放也会自增，用它做断言等于自证。
/// 这里只记录设备创建的那一份句柄；<see cref="IImageSource.Retain"/> 产生的下游句柄由各自持有者释放，
/// 混在一起就无法判断"队列里那一帧到底有没有被回收"。
/// </para>
/// </summary>
internal sealed class FrameReleaseLog
{
    private readonly object _gate = new();
    private readonly List<string> _released = new();

    /// <summary>生产者句柄被释放的帧标识，按释放顺序。</summary>
    public IReadOnlyList<string> Released
    {
        get { lock (_gate) return _released.ToArray(); }
    }

    /// <summary>登记一次生产者句柄释放。</summary>
    /// <param name="frameTag">帧标识。</param>
    public void Record(string frameTag)
    {
        lock (_gate)
            _released.Add(frameTag);
    }
}

/// <summary>
/// 可控的假流式Provider：设备在打开时创建并交回给测试，使测试能在任意时刻推动一次"外部触发回调"。
/// <para>
/// 帧的推送完全由测试触发（<see cref="FakeStreamingDevice.Emit"/>），不使用计时器，
/// 因此端到端结果确定可复现。
/// </para>
/// </summary>
internal sealed class FakeStreamingProvider : IVisionAcquisitionProvider
{
    private readonly Action<FakeStreamingDevice> _onDeviceCreated;
    private int _openCount;

    /// <summary>创建Provider。</summary>
    /// <param name="providerId">Provider稳定身份。</param>
    /// <param name="resourceKey">设备报告的规范资源键；与机器配置声明的资源键一致。</param>
    /// <param name="onDeviceCreated">设备创建回调；测试用它拿到推动回调的入口。</param>
    public FakeStreamingProvider(string providerId, string resourceKey, Action<FakeStreamingDevice> onDeviceCreated)
    {
        ProviderId = providerId;
        ResourceKey = resourceKey;
        _onDeviceCreated = onDeviceCreated;
    }

    /// <inheritdoc/>
    public string ProviderId { get; }

    /// <summary>设备报告的规范资源键。</summary>
    public string ResourceKey { get; }

    /// <summary>已执行的打开次数；用来证明"运行准备失败时设备根本没有被打开"。</summary>
    public int OpenCount => Volatile.Read(ref _openCount);

    /// <inheritdoc/>
    public ValueTask<IVisionAcquisitionDevice> OpenAsync(string providerBindingId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _openCount);
        var device = new FakeStreamingDevice(new VisionDeviceIdentity(ProviderId, providerBindingId, ResourceKey));
        _onDeviceCreated(device);
        return new ValueTask<IVisionAcquisitionDevice>(device);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => default;
}

/// <summary>把假Provider贡献给候选组合的Module。</summary>
internal sealed class FakeStreamingProviderModule : IVisionAcquisitionProviderModule
{
    private readonly IVisionAcquisitionProvider _provider;

    /// <summary>创建Module。</summary>
    /// <param name="extensionId">Module稳定身份。</param>
    /// <param name="providerId">Provider稳定身份。</param>
    /// <param name="provider">要贡献的Provider实例。</param>
    public FakeStreamingProviderModule(string extensionId, string providerId, IVisionAcquisitionProvider provider)
    {
        ExtensionId = extensionId;
        ProviderId = providerId;
        _provider = provider;
    }

    /// <inheritdoc/>
    public string ExtensionId { get; }

    /// <summary>本Module贡献的Provider身份。</summary>
    public string ProviderId { get; }

    /// <inheritdoc/>
    public void Contribute(IVisionAcquisitionProviderContributionBuilder builder) =>
        builder.Register(new VisionAcquisitionProviderRegistration(ProviderId, "1.0.0", () => _provider));
}

/// <summary>
/// 假流式设备：既声明持续接收能力，也保留主动采集路径（缓冲源不应走到那里，因此主动采集直接失败）。
/// <para>
/// 它模拟厂商SDK的回调语义：<see cref="IVisionProviderFrameSink.Publish"/> 串行执行，
/// 且异常不得抛回调用线程——SDK回调线程上抛异常通常直接崩进程。
/// </para>
/// </summary>
internal sealed class FakeStreamingDevice : IVisionAcquisitionDevice, IVisionStreamingAcquisitionDevice
{
    private readonly object _gate = new();
    private readonly List<string> _emitted = new();
    private readonly List<string> _sinkFailures = new();
    private IVisionProviderFrameSink? _sink;
    private int _streamStartCount;
    private int _streamStopCount;
    private int _disposeCount;

    /// <summary>创建设备。</summary>
    /// <param name="identity">设备报告身份。</param>
    public FakeStreamingDevice(VisionDeviceIdentity identity) =>
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));

    /// <inheritdoc/>
    public VisionDeviceIdentity Identity { get; }

    /// <summary>生产者图像句柄的释放记录。</summary>
    public FrameReleaseLog FrameReleases { get; } = new FrameReleaseLog();

    /// <summary>布防次数；嵌套运行重新布防会让它大于 1。</summary>
    public int StreamStartCount => Volatile.Read(ref _streamStartCount);

    /// <summary>接收流被显式释放的次数。</summary>
    public int StreamStopCount => Volatile.Read(ref _streamStopCount);

    /// <summary>设备自身被释放的次数。</summary>
    public int DisposeCount => Volatile.Read(ref _disposeCount);

    /// <summary>是否处于布防状态。</summary>
    public bool IsStreaming
    {
        get { lock (_gate) return _sink is not null; }
    }

    /// <summary>已成功交付给接收方的帧标识，按交付顺序。</summary>
    public IReadOnlyList<string> EmittedFrames
    {
        get { lock (_gate) return _emitted.ToArray(); }
    }

    /// <summary>接收方抛出的异常信息；异常本身被设备吞掉，不返回调用线程。</summary>
    public IReadOnlyList<string> SinkFailures
    {
        get { lock (_gate) return _sinkFailures.ToArray(); }
    }

    /// <inheritdoc/>
    public ValueTask<VisionProviderFrame> CaptureAsync(VisionCaptureRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("外部回调缓冲源不接受主动单次采集；节点必须走队列领取路径。");

    /// <inheritdoc/>
    public ValueTask<IVisionAcquisitionStream> StartStreamAsync(
        IVisionProviderFrameSink sink,
        CancellationToken cancellationToken)
    {
        if (sink is null)
            throw new ArgumentNullException(nameof(sink));
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            // 一台设备同时只能有一条接收流；重复布防明确拒绝，不静默替换接收方。
            if (_sink is not null)
                throw new InvalidOperationException("该设备已经在布防状态；一台设备同时只允许一条接收流。");
            _sink = sink;
            Interlocked.Increment(ref _streamStartCount);
        }

        return new ValueTask<IVisionAcquisitionStream>(new Stream(this));
    }

    /// <summary>模拟一次外部触发回调。</summary>
    /// <param name="frameTag">帧标识；用于观察该帧的生产者句柄最终是否被释放。</param>
    /// <param name="deviceSequence">设备报告的帧序号；不支持时为空。</param>
    /// <returns>帧是否被交付给接收方；未布防时返回 <see langword="false"/>。</returns>
    public bool Emit(string frameTag, long? deviceSequence = null)
    {
        lock (_gate)
        {
            if (_sink is null)
                return false;

            var frame = new VisionProviderFrame(
                new TrackedImageSource(frameTag, FrameReleases),
                DateTimeOffset.UtcNow,
                deviceSequence);
            _emitted.Add(frameTag);
            try
            {
                _sink.Publish(frame);
            }
            catch (Exception exception)
            {
                // 接收方拒绝时帧已由接收方释放；这里只记录，不把异常抛回调用线程。
                _sinkFailures.Add(exception.GetType().Name + ": " + exception.Message);
            }

            return true;
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            // 真实SDK关闭设备同样会结束接收；但这不算"运行时主动停流"，因此不计入 StreamStopCount。
            _sink = null;
        }

        Interlocked.Increment(ref _disposeCount);
        return default;
    }

    private sealed class Stream : IVisionAcquisitionStream
    {
        private readonly FakeStreamingDevice _device;
        private int _disposed;

        public Stream(FakeStreamingDevice device) => _device = device;

        /// <summary>释放接收流：等待已经进入的回调退出，之后不再交付任何帧。</summary>
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return default;

            lock (_device._gate)
            {
                _device._sink = null;
                Interlocked.Increment(ref _device._streamStopCount);
            }

            return default;
        }
    }
}

/// <summary>中立图像源；只有设备创建的那一份句柄计入释放记录，下游 Retain 出来的句柄不重复计入。</summary>
internal sealed class TrackedImageSource : IImageSource
{
    private readonly FrameReleaseLog _releases;
    private readonly bool _isProduced;

    /// <summary>由设备创建一份新的生产者句柄。</summary>
    /// <param name="frameTag">帧标识。</param>
    /// <param name="releases">生产者句柄释放记录。</param>
    public TrackedImageSource(string frameTag, FrameReleaseLog releases)
    {
        FrameTag = frameTag;
        _releases = releases;
        _isProduced = true;
    }

    private TrackedImageSource(TrackedImageSource origin)
    {
        FrameTag = origin.FrameTag;
        _releases = origin._releases;
    }

    /// <summary>帧标识。</summary>
    public string FrameTag { get; }

    /// <inheritdoc/>
    public ImageInfo Info { get; } = new ImageInfo(2, 2, EPixelLayout.Gray8);

    /// <inheritdoc/>
    public IImageSource Retain() => new TrackedImageSource(this);

    /// <inheritdoc/>
    public IImageSource ReadTile(int level, int tileX, int tileY, int tileSize) => throw new NotSupportedException();

    /// <inheritdoc/>
    public void CopyTo(int sourceOffset, byte[] destination, int destinationOffset, int count) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_isProduced)
            _releases.Record(FrameTag);
    }
}
