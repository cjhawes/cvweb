using System.Threading.Channels;
using Bunit;
using CvWeb.Client.Components.Widgets;
using CvWeb.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CvWeb.Client.Tests;

public sealed class AlarmTriageWidgetTests
{
    [Fact]
    public void RendersWarmupStateBeforeAnyTelemetryArrives()
    {
        using var context = new BunitContext();
        var stream = new FakeMockStreamService();
        context.Services.AddSingleton<IMockStreamService>(stream);

        var component = context.Render<AlarmTriage>();

        Assert.Contains("Local triage engine warming up", component.Markup);
        Assert.Contains("No priority alerts yet.", component.Markup);
    }

    [Fact]
    public async Task EmitsPriorityAlertAfterDebouncedClusterProcessing()
    {
        using var context = new BunitContext();
        var stream = new FakeMockStreamService();
        context.Services.AddSingleton<IMockStreamService>(stream);

        var component = context.Render<AlarmTriage>();

        var t0 = DateTimeOffset.Parse("2026-05-07T12:00:00Z");
        await stream.PublishTelemetryJsonAsync(new TelemetryJsonSample(t0, CreatePayload("edge-a", 91.4, 77.2, 1.94, 45, 2)));
        await stream.PublishTelemetryJsonAsync(new TelemetryJsonSample(t0.AddMilliseconds(2200), CreatePayload("edge-a", 90.6, 76.8, 1.88, 44, 2)));

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Local triage engine active", component.Markup);
            Assert.Contains("Priority Alerts: 1", component.Markup);
            Assert.Contains("edge-a", component.Markup);
        });
    }

    private static string CreatePayload(string node, double cpu, double memory, double packetLoss, int activeStreams, int alertLevel)
    {
        return $$"""
        {
          "node": "{{node}}",
          "timestamp": "2026-05-07T12:00:00Z",
          "cpuLoadPercent": {{cpu}},
          "memoryLoadPercent": {{memory}},
          "packetLossPercent": {{packetLoss}},
          "activeStreams": {{activeStreams}},
          "alertLevel": {{alertLevel}}
        }
        """;
    }

    private sealed class FakeMockStreamService : IMockStreamService
    {
        private readonly Channel<TelemetrySignal> _telemetry = Channel.CreateUnbounded<TelemetrySignal>();
        private readonly Channel<TelemetryJsonSample> _telemetryJson = Channel.CreateUnbounded<TelemetryJsonSample>();
        private readonly Channel<MjpegByteChunk> _mjpegByte = Channel.CreateUnbounded<MjpegByteChunk>();

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ChannelReader<TelemetrySignal> SubscribeTelemetry(CancellationToken cancellationToken = default)
        {
            return _telemetry.Reader;
        }

        public ChannelReader<TelemetryJsonSample> SubscribeTelemetryJson(CancellationToken cancellationToken = default)
        {
            return _telemetryJson.Reader;
        }

        public ChannelReader<MjpegByteChunk> SubscribeMjpegByteChunks(CancellationToken cancellationToken = default)
        {
            return _mjpegByte.Reader;
        }

        public ValueTask<WebRtcTrackEnvelope> GetWebRtcTrackProfileAsync(string profile, CancellationToken cancellationToken = default)
        {
            var envelope = new WebRtcTrackEnvelope("balanced", [
                new WebRtcTrack("video-main", "video", "sendonly", "VP9", 2200, 30, "Primary real-time stream")
            ]);

            return ValueTask.FromResult(envelope);
        }

        public ValueTask DisposeAsync()
        {
            _telemetry.Writer.TryComplete();
            _telemetryJson.Writer.TryComplete();
            _mjpegByte.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishTelemetryJsonAsync(TelemetryJsonSample sample)
        {
            return _telemetryJson.Writer.WriteAsync(sample);
        }
    }
}
