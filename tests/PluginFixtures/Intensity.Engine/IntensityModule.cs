using System.Globalization;
using DP.Vision.Algorithms;
using External.Intensity;

namespace External.IntensityEngine;

/// <summary>引擎包贡献宿主不知道的新能力，并提供两个实现。</summary>
public sealed class IntensityModule : IVisionAlgorithmModule
{
    /// <inheritdoc/>
    public string ExtensionId => "fixture.intensity.engine";
    /// <inheritdoc/>
    public void Register(IVisionAlgorithmRegistration registrations)
    {
        foreach (var id in new[] { "fixture.add", "fixture.multiply" })
            registrations.Add(new VisionAlgorithmDescriptor(id, "Fixture", "1", new VisionAlgorithmFactory<IIntensityOffset>((config, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (config.SettingsVersion != 1 || config.Settings.Keys.Any(key => key != "amount"))
                    throw new ArgumentException("不支持的初始化参数版本或字段。");
                var amount = config.Settings.TryGetValue("amount", out var value) ? int.Parse(value, CultureInfo.InvariantCulture) : 1;
                return Task.FromResult(new VisionAlgorithmActivation(amount.ToString(CultureInfo.InvariantCulture), EVisionAlgorithmSharing.SharedConcurrent,
                    _ => Task.FromResult(new VisionAlgorithmResource(new Operation(amount, id == "fixture.add")))));
            }), parameters: new[] { new VisionAlgorithmParameter("amount", "变换量", typeof(int), "1") }));
    }
    private sealed class Operation(int amount, bool add) : IIntensityOffset
    {
        public int Apply(int value) => add ? value + amount : value * amount;
    }
}
