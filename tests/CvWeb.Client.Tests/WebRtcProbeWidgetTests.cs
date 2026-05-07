using System.Threading.Channels;
using Bunit;
using CvWeb.Client.Components.Widgets;
using CvWeb.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CvWeb.Client.Tests;

public sealed class WebRtcProbeWidgetTests
{
    [Fact]
    public void RendersConnectingStateBeforeRtcSamplesArrive()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IMockStreamService>(new FakeMockStreamService());

        var component = context.Render<WebRtcProbe>();

        Assert.Contains("Peer Connection: connecting", component.Markup);
        Assert.Contains("Track Profile: balanced", component.Markup);
    }

    [Fact]
    public async Task UpdatesMetricChipsAfterRtcStatsCallback()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IMockStreamService>(new FakeMockStreamService());

        var component = context.Render<WebRtcProbe>();

        await component.Instance.UpdateWebRtcProbeStats(new WebRtcProbeStatsSample
        {
            Sequence = 42,
            Timestamp = DateTimeOffset.UtcNow,
            BitrateKbps = 2600.4,
            PacketLossPercent = 0.124,
            JitterMs = 4.7,
            RoundTripTimeMs = 40.2,
            FramesPerSecond = 29.8,
            ConnectionState = "connected"
        });

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Peer Connection: connected", component.Markup);
            Assert.Contains("Bitrate: 2600.4 kbps", component.Markup);
            Assert.Contains("Packet Loss: 0.124%", component.Markup);
            Assert.Contains("Jitter: 4.7 ms", component.Markup);
            Assert.Contains("RTT: 40.2 ms", component.Markup);
            Assert.Contains("FPS: 29.8", component.Markup);
            Assert.Contains("Sequence: 42", component.Markup);
        });
    }

    private sealed class FakeMockStreamService : IMockStreamService
    {
        private readonly Channel<TelemetrySignal> _telemetry = Channel.CreateUnbounded<TelemetrySignal>();
        private readonly Channel<TelemetryJsonSample> _telemetryJson = Channel.CreateUnbounded<TelemetryJsonSample>();
        private readonly Channel<MjpegByteChunk> _mjpegByteChunks = Channel.CreateUnbounded<MjpegByteChunk>();

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
            return _mjpegByteChunks.Reader;
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
            _mjpegByteChunks.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
