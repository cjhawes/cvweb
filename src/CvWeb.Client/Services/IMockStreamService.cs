using System.Threading.Channels;

namespace CvWeb.Client.Services;

/// <summary>
/// Defines the client-side synthetic stream broker used by dashboard widgets.
/// </summary>
public interface IMockStreamService : IAsyncDisposable
{
    /// <summary>
    /// Starts the browser worker that emits synthetic telemetry and media signals.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel startup.</param>
    ValueTask StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the browser worker and detaches active callbacks.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel shutdown.</param>
    ValueTask StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to typed telemetry samples.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the subscription.</param>
    /// <returns>A channel reader for telemetry samples.</returns>
    ChannelReader<TelemetrySignal> SubscribeTelemetry(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to raw telemetry JSON samples for local triage.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the subscription.</param>
    /// <returns>A channel reader for telemetry JSON payloads.</returns>
    ChannelReader<TelemetryJsonSample> SubscribeTelemetryJson(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to MJPEG multipart byte chunks for software decoding.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the subscription.</param>
    /// <returns>A channel reader for MJPEG byte chunks.</returns>
    ChannelReader<MjpegByteChunk> SubscribeMjpegByteChunks(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves WebRTC track metadata for a named profile.
    /// </summary>
    /// <param name="profile">The profile name (for example balanced or high-fidelity).</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns>The resolved track envelope for the requested profile.</returns>
    ValueTask<WebRtcTrackEnvelope> GetWebRtcTrackProfileAsync(string profile, CancellationToken cancellationToken = default);
}
