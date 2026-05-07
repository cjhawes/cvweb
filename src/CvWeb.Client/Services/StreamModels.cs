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
