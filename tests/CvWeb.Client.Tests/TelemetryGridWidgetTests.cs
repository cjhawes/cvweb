using Bunit;
using CvWeb.Client.Components.Widgets;

namespace CvWeb.Client.Tests;

public sealed class TelemetryGridWidgetTests
{
    [Fact]
    public void RendersWarmupStateBeforeTelemetryFramesArrive()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var component = context.Render<TelemetryGrid>();

        Assert.Contains("Telemetry grid warming up", component.Markup);
    }

    [Fact]
    public async Task UpdatesMetricChipsAfterStatsCallback()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var component = context.Render<TelemetryGrid>();

        await component.Instance.UpdateTelemetryGridStats(
            sensorCount: 1024,
            ingestRateHz: 60,
            renderRateFps: 59.4,
            droppedFrames: 14,
            alertSensors: 126,
            cpuAveragePercent: 68.2,
            packetLossAveragePercent: 0.374,
            streamState: "connected",
            sequence: 4420);

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Telemetry worker active at 60Hz", component.Markup);
            Assert.Contains("Sensors: 1,024", component.Markup);
            Assert.Contains("Ingest: 60.0Hz", component.Markup);
            Assert.Contains("Render: 59.4fps", component.Markup);
            Assert.Contains("Dropped: 14", component.Markup);
            Assert.Contains("Alerts: 126", component.Markup);
            Assert.Contains("CPU Mean: 68.2%", component.Markup);
            Assert.Contains("Packet Loss Mean: 0.374%", component.Markup);
            Assert.Contains("Sequence: 4,420", component.Markup);
        });
    }
}
