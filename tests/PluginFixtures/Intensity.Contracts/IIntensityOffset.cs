using DP.Vision.Algorithms;

namespace External.Intensity;

/// <summary>独立契约，用于验证宿主未知能力的跨包类型身份。</summary>
[VisionCapability("fixture.intensity", "插件验收", "灰度数值变换")]
public interface IIntensityOffset
{
    /// <summary>对输入灰度数值执行变换。</summary>
    int Apply(int value);
}
