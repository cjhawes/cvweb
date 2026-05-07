using System.Threading.Channels;

namespace CvWeb.Client.Services;

public interface IMockStreamService : IAsyncDisposable
{
    ValueTask StartAsync(CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);

    ChannelReader<TelemetrySignal> SubscribeTelemetry(CancellationToken cancellationToken = default);

    ChannelReader<MjpegStreamSample> SubscribeMjpeg(CancellationToken cancellationToken = default);

    ValueTask<WebRtcTrackEnvelope> GetWebRtcTrackProfileAsync(string profile, CancellationToken cancellationToken = default);
}
