using System.Text.Json;
using CvWeb.Client.Services;
using Microsoft.JSInterop;

namespace CvWeb.Client.Tests;

public sealed class MockStreamServiceTests
{
    [Fact]
    public async Task GetWebRtcTrackProfileAsync_ReturnsFallbackProfile_WhenWorkerHasNotPublished()
    {
        await using var service = new MockStreamService(new NoopJsRuntime());

        var profile = await service.GetWebRtcTrackProfileAsync("balanced");

        Assert.Equal("balanced", profile.Profile);
        Assert.Equal(3, profile.Tracks.Count);
        Assert.Contains(profile.Tracks, track => track.Id == "telemetry-overlay");
    }

    [Fact]
    public async Task HandleWorkerMessage_PublishesTelemetryToSubscribers()
    {
        await using var service = new MockStreamService(new NoopJsRuntime());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var reader = service.SubscribeTelemetry(timeout.Token);

        var sample = new TelemetrySignal(
            "edge-gateway-a",
            DateTimeOffset.UtcNow,
            56.7,
            62.1,
            0.22,
            31,
            0);

        var payload = JsonSerializer.Serialize(sample);
        await service.HandleWorkerMessage("telemetry", payload);

        var streamed = await reader.ReadAsync(timeout.Token);
        Assert.Equal(sample.Node, streamed.Node);
        Assert.Equal(sample.CpuLoadPercent, streamed.CpuLoadPercent);
        Assert.Equal(sample.MemoryLoadPercent, streamed.MemoryLoadPercent);
        Assert.Equal(sample.PacketLossPercent, streamed.PacketLossPercent);
    }

    [Fact]
    public async Task HandleWorkerMessage_IgnoresTelemetryGridMessagesWithoutThrowing()
    {
        await using var service = new MockStreamService(new NoopJsRuntime());

        var payload = """
        {
            "sequence": 42,
            "gridWidth": 32,
            "gridHeight": 32,
            "sensorCount": 1024,
            "intensities": [10, 20, 30, 40],
            "alerts": [0, 1, 0, 2],
            "alertCount": 2,
            "cpuAveragePercent": 61.2,
            "packetLossAveragePercent": 0.218
        }
        """;

        var exception = await Record.ExceptionAsync(async () =>
            await service.HandleWorkerMessage("telemetry-grid", payload));

        Assert.Null(exception);
    }

    private sealed class NoopJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
