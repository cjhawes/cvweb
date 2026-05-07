namespace CvWeb.Client.Services;

public sealed record TelemetrySignal(
    string Node,
    DateTimeOffset Timestamp,
    double CpuLoadPercent,
    double MemoryLoadPercent,
    double PacketLossPercent,
    int ActiveStreams,
    int AlertLevel);

public sealed record TelemetryJsonSample(
    DateTimeOffset ReceivedAt,
    string PayloadJson);

public sealed record MjpegStreamSample(
    int BoundaryCount,
    int RenderFps,
    DateTimeOffset Timestamp);

public sealed record MjpegByteChunk(
    long Sequence,
    DateTimeOffset Timestamp,
    byte[] ChunkBytes);

public sealed record WebRtcTrackEnvelope(
    string Profile,
    IReadOnlyList<WebRtcTrack> Tracks);

public sealed record WebRtcTrack(
    string Id,
    string Kind,
    string Direction,
    string Codec,
    int? MaxBitrateKbps,
    int? TargetFps,
    string Purpose);

public sealed class WebRtcProbeStatsSample
{
    public long Sequence { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public double BitrateKbps { get; set; }

    public double PacketLossPercent { get; set; }

    public double JitterMs { get; set; }

    public double RoundTripTimeMs { get; set; }

    public double FramesPerSecond { get; set; }

    public string ConnectionState { get; set; } = "connecting";
}
