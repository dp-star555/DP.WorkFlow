using System;
using System.Linq;
using DP.Vision.Acquisition;
using Xunit;

namespace DP.WorkFlow.Tests;

/// <summary>V2-2 需求4：Composition 直接投影 WorkflowVisionSourceCatalog（FromAcquisition）。</summary>
public sealed class WorkflowVisionSourceCatalogProjectionTests
{
    private const string TypeId = "dp.acquisition.test.area";

    /// <summary>可用源投影为可用条目，策略与模式随组合发布。</summary>
    [Fact]
    public void 可用源投影为可用条目并携带策略()
    {
        var composition = Compose(new VisionAcquisitionCameraDefinition(
            "Camera.Top",
            TypeId,
            1,
            false,
            new VisionAcquisitionConnectionPolicy(),
            inbox: null,
            "{\"serialNumber\":\"SN-1\"}"));

        var catalog = WorkflowVisionSourceCatalog.FromAcquisition(composition);

        var source = Assert.Single(catalog.Sources);
        Assert.Equal("Camera.Top", source.SourceId);
        Assert.Equal(TypeId, source.ProviderId);
        Assert.True(source.IsAvailable);
        Assert.Null(source.Diagnostic);
        Assert.Equal(EVisionAcquisitionMode.OnDemand, source.AcquisitionMode);
        Assert.Equal(EVisionSourceSharingPolicy.ExclusiveOperation, source.SharingPolicy);
        Assert.True(catalog.TryGet("Camera.Top", out _));
        Assert.False(catalog.TryGet("Camera.Missing", out _));
    }

    /// <summary>未安装Type被保真投影为不可用条目，诊断保留，不丢失。</summary>
    [Fact]
    public void 未安装Type保真投影为不可用条目()
    {
        var emptyCatalog = new VisionAcquisitionTypeCatalogComposer()
            .Compose(Array.Empty<IVisionAcquisitionDriverModule>());
        var composition = new VisionAcquisitionMachineConfigurationComposer().Compose(emptyCatalog, new[]
        {
            new VisionAcquisitionCameraDefinition(
                "Camera.Top",
                "dp.acquisition.missing",
                1,
                false,
                new VisionAcquisitionConnectionPolicy(),
                inbox: null,
                "{\"serialNumber\":\"SN-1\"}")
        });

        var catalog = WorkflowVisionSourceCatalog.FromAcquisition(composition);

        var source = Assert.Single(catalog.Sources);
        Assert.False(source.IsAvailable);
        Assert.Contains("未安装", source.Diagnostic);
    }

    private static VisionAcquisitionProviderComposition Compose(params VisionAcquisitionCameraDefinition[] cameras)
    {
        var catalog = new VisionAcquisitionTypeCatalogComposer().Compose(
            new IVisionAcquisitionDriverModule[] { new TestDriverModule() });
        return new VisionAcquisitionMachineConfigurationComposer().Compose(catalog, cameras);
    }

    private sealed class TestDriverModule : IVisionAcquisitionDriverModule
    {
        public string ExtensionId => "dp.vision.test.driver";

        public void Contribute(IVisionAcquisitionTypeContributionBuilder builder) =>
            builder.Register(new VisionAcquisitionTypeRegistration(
                TypeId,
                "dp.vision.test",
                "1.0.0",
                EVisionAcquisitionKind.AreaScan,
                1,
                "测试面阵",
                new VisionAcquisitionTypeCapabilities(
                    SupportsFreeRun: true,
                    SupportsSoftwareTrigger: true,
                    SupportsExternalTrigger: true,
                    SupportsCompleteFrameCallback: true),
                () => new FakeStreamingProvider(TypeId, "camera:serial:SN-1", _ => { }),
                Parse));
    }

    private static VisionDeviceSettingsParseResult Parse(string? json) =>
        new VisionDeviceSettingsParseResult(
            "test:serial:SN-1",
            "camera:serial:SN-1",
            "serialNumber=SN-1");
}
