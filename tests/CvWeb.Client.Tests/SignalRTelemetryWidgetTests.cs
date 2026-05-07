using System.Threading.Channels;
using Bunit;
using CvWeb.Client.Components.Widgets;
using CvWeb.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CvWeb.Client.Tests;

public sealed class SignalRTelemetryWidgetTests
{
    [Fact]
    public void RendersConnectingStateBeforeStreamDataArrives()
    {
        using var context = new BunitContext();
        var stream = new FakeMockStreamService();
        context.Services.AddSingleton<IMockStreamService>(stream);

        var component = context.Render<SignalRTelemetryWidget>();

        Assert.Contains("Connecting to stream...", component.Markup);
    }

    [Fact]
    public async Task UpdatesMetricsAfterTelemetrySampleArrives()
    {
        using var context = new BunitContext();
        var stream = new FakeMockStreamService();
        context.Services.AddSingleton<IMockStreamService>(stream);

        var component = context.Render<SignalRTelemetryWidget>();

        await stream.PublishTelemetryAsync(new TelemetrySignal(
            "edge-gateway-a",
            DateTimeOffset.UtcNow,
            61.3,
            70.4,
            0.41,
            29,
            1));

        component.WaitForAssertion(() =>
        {
            Assert.Contains("CPU: 61.3%", component.Markup);
            Assert.Contains("Memory: 70.4%", component.Markup);
            Assert.Contains("Packet Loss: 0.41%", component.Markup);
            Assert.Contains("Live stream: edge-gateway-a", component.Markup);
        });
    }

    private sealed class FakeMockStreamService : IMockStreamService
    {
        private readonly Channel<TelemetrySignal> _telemetry = Channel.CreateUnbounded<TelemetrySignal>();
        private readonly Channel<MjpegStreamSample> _mjpeg = Channel.CreateUnbounded<MjpegStreamSample>();

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

        public ChannelReader<MjpegStreamSample> SubscribeMjpeg(CancellationToken cancellationToken = default)
        {
            return _mjpeg.Reader;
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
            _mjpeg.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishTelemetryAsync(TelemetrySignal signal)
        {
            return _telemetry.Writer.WriteAsync(signal);
        }
    }
}
