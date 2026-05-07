using Bunit;
using System.Threading.Channels;
using CvWeb.Client.Components.Widgets;
using CvWeb.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace CvWeb.Client.Tests;

public sealed class MjpegDecoderWidgetTests
{
    [Fact]
    public void RendersDecoderStatusAndMetricsShell()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IMockStreamService>(new FakeMockStreamService());

        var component = context.Render<MjpegDecoder>();

        Assert.Contains("Transport: multipart byte stream", component.Markup);
        Assert.Contains("Chunks: 0", component.Markup);
    }

    [Fact]
    public async Task UpdatesRenderMetricsViaInteropCallback()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IMockStreamService>(new FakeMockStreamService());

        var component = context.Render<MjpegDecoder>();

        await component.Instance.UpdateMjpegStats(boundaryCount: 12, renderFps: 30);

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Boundaries: 12", component.Markup);
            Assert.Contains("Render FPS: 30.0", component.Markup);
        });
    }

    private sealed class FakeMockStreamService : IMockStreamService
    {
        private readonly Channel<MjpegByteChunk> _mjpegBytes = Channel.CreateUnbounded<MjpegByteChunk>();
        private readonly Channel<TelemetrySignal> _telemetry = Channel.CreateUnbounded<TelemetrySignal>();
        private readonly Channel<TelemetryJsonSample> _telemetryJson = Channel.CreateUnbounded<TelemetryJsonSample>();

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
            return _mjpegBytes.Reader;
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
            _mjpegBytes.Writer.TryComplete();
            _telemetry.Writer.TryComplete();
            _telemetryJson.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
