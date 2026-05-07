namespace CvWeb.Client.Services;

/// <summary>
/// Represents a single typed telemetry signal produced by the synthetic worker.
/// </summary>
/// <param name="Node">The source node identifier.</param>
/// <param name="Timestamp">The source timestamp of the sample.</param>
/// <param name="CpuLoadPercent">The CPU load percentage.</param>
/// <param name="MemoryLoadPercent">The memory load percentage.</param>
/// <param name="PacketLossPercent">The packet loss percentage.</param>
/// <param name="ActiveStreams">The count of active streams at sample time.</param>
/// <param name="AlertLevel">The synthetic alert level.</param>
public sealed record TelemetrySignal(
    string Node,
    DateTimeOffset Timestamp,
    double CpuLoadPercent,
    double MemoryLoadPercent,
    double PacketLossPercent,
    int ActiveStreams,
    int AlertLevel);

/// <summary>
/// Represents a raw telemetry JSON payload emitted by the worker.
/// </summary>
/// <param name="ReceivedAt">The local receive timestamp.</param>
/// <param name="PayloadJson">The raw telemetry JSON payload.</param>
public sealed record TelemetryJsonSample(
    DateTimeOffset ReceivedAt,
    string PayloadJson);

/// <summary>
/// Represents one MJPEG multipart byte chunk emitted by the worker stream.
/// </summary>
/// <param name="Sequence">The worker sequence number for the chunk.</param>
/// <param name="Timestamp">The worker timestamp for the chunk.</param>
/// <param name="ChunkBytes">The raw chunk payload bytes.</param>
public sealed record MjpegByteChunk(
    long Sequence,
    DateTimeOffset Timestamp,
    byte[] ChunkBytes);

/// <summary>
/// Represents a named set of WebRTC tracks used by the diagnostics widgets.
/// </summary>
/// <param name="Profile">The profile identifier.</param>
/// <param name="Tracks">The tracks included in the profile.</param>
public sealed record WebRtcTrackEnvelope(
    string Profile,
    IReadOnlyList<WebRtcTrack> Tracks);

/// <summary>
/// Represents one WebRTC media track definition.
/// </summary>
/// <param name="Id">The unique track identifier.</param>
/// <param name="Kind">The media kind (audio or video).</param>
/// <param name="Direction">The media direction.</param>
/// <param name="Codec">The configured codec.</param>
/// <param name="MaxBitrateKbps">The maximum bitrate in kbps when known.</param>
/// <param name="TargetFps">The target frames per second when known.</param>
/// <param name="Purpose">The operational purpose of the track.</param>
public sealed record WebRtcTrack(
    string Id,
    string Kind,
    string Direction,
    string Codec,
    int? MaxBitrateKbps,
    int? TargetFps,
    string Purpose);

/// <summary>
/// Represents a sampled RTC stats payload used by the WebRTC probe UI.
/// </summary>
public sealed class WebRtcProbeStatsSample
{
    /// <summary>
    /// Gets or sets the monotonic sequence number for the sample.
    /// </summary>
    public long Sequence { get; set; }

    /// <summary>
    /// Gets or sets the timestamp associated with the sample.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the measured bitrate in kilobits per second.
    /// </summary>
    public double BitrateKbps { get; set; }

    /// <summary>
    /// Gets or sets the packet loss percentage.
    /// </summary>
    public double PacketLossPercent { get; set; }

    /// <summary>
    /// Gets or sets the jitter in milliseconds.
    /// </summary>
    public double JitterMs { get; set; }

    /// <summary>
    /// Gets or sets the round-trip time in milliseconds.
    /// </summary>
    public double RoundTripTimeMs { get; set; }

    /// <summary>
    /// Gets or sets the observed frames per second.
    /// </summary>
    public double FramesPerSecond { get; set; }

    /// <summary>
    /// Gets or sets the normalized connection state label.
    /// </summary>
    public string ConnectionState { get; set; } = "connecting";
}
