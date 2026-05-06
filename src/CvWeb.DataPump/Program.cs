using System.Text;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("OpenClient", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddSignalR();
builder.Services.AddHostedService<TelemetryBroadcastService>();

var app = builder.Build();

app.UseCors("OpenClient");

app.MapGet("/", () =>
    Results.Ok(new ServiceStatus("CvWeb.DataPump", "online", DateTimeOffset.UtcNow)));

app.MapGet("/api/health", () =>
    Results.Ok(new ServiceStatus("CvWeb.DataPump", "healthy", DateTimeOffset.UtcNow)));

app.MapGet("/api/telemetry", (string? node, int samples = 24) =>
{
    var safeSampleCount = Math.Clamp(samples, 6, 180);
    var nodeName = string.IsNullOrWhiteSpace(node) ? "edge-gateway-a" : node.Trim();
    var now = DateTimeOffset.UtcNow;
    var random = BuildSeededRandom(nodeName, now);
    var points = new List<TelemetryPoint>(safeSampleCount);

    for (var index = safeSampleCount - 1; index >= 0; index--)
    {
        var timestamp = now.AddSeconds(-index * 2);
        var signal = BuildTelemetrySignal(nodeName, timestamp, random);

        points.Add(new TelemetryPoint(
            signal.Timestamp,
            signal.CpuLoadPercent,
            signal.MemoryLoadPercent,
            signal.PacketLossPercent,
            signal.ActiveStreams,
            signal.AlertLevel));
    }

    return Results.Ok(new TelemetryEnvelope(nodeName, points));
});

app.MapGet("/api/video/frame-meta", (int width = 1920, int height = 1080, int fps = 30) =>
{
    var safeWidth = Math.Clamp(width, 320, 3840);
    var safeHeight = Math.Clamp(height, 240, 2160);
    var safeFps = Math.Clamp(fps, 15, 120);

    var packetRate = safeFps * 2 + (safeWidth * safeHeight) / 180000;
    var keyFrameInterval = Math.Clamp(safeFps * 2, 24, 180);

    return Results.Ok(new VideoFrameMeta(
        safeWidth,
        safeHeight,
        safeFps,
        "VP9",
        keyFrameInterval,
        packetRate,
        "synthetic-pattern-generator",
        DateTimeOffset.UtcNow));
});

app.MapGet("/api/webrtc/tracks", (string? profile) =>
{
    var safeProfile = string.IsNullOrWhiteSpace(profile) ? "balanced" : profile.Trim().ToLowerInvariant();
    IReadOnlyList<WebRtcTrack> tracks = safeProfile switch
    {
        "low-bandwidth" =>
        [
            new WebRtcTrack("video-main", "video", "sendonly", "VP8", 900, 24, "Main stream for constrained links"),
            new WebRtcTrack("telemetry-overlay", "video", "sendonly", "VP8", 350, 12, "Overlay channel for metrics"),
            new WebRtcTrack("audio-ops", "audio", "sendrecv", "OPUS", 48, null, "Operations voice coordination")
        ],
        "high-fidelity" =>
        [
            new WebRtcTrack("video-main", "video", "sendonly", "AV1", 4200, 60, "Primary high-fidelity stream"),
            new WebRtcTrack("video-multiview", "video", "sendonly", "VP9", 2800, 45, "Secondary diagnostics camera"),
            new WebRtcTrack("audio-ops", "audio", "sendrecv", "OPUS", 96, null, "Operations voice coordination")
        ],
        _ =>
        [
            new WebRtcTrack("video-main", "video", "sendonly", "VP9", 2200, 30, "Primary real-time stream"),
            new WebRtcTrack("telemetry-overlay", "video", "sendonly", "VP8", 700, 15, "Telemetry annotation layer"),
            new WebRtcTrack("audio-ops", "audio", "sendrecv", "OPUS", 64, null, "Operations voice coordination")
        ]
    };

    return Results.Ok(new WebRtcTrackEnvelope(safeProfile, tracks));
});

app.MapGet("/api/mjpeg/stream", async (HttpContext context, int fps = 30) =>
{
    var safeFps = Math.Clamp(fps, 5, 30);
    var delay = TimeSpan.FromMilliseconds(1000d / safeFps);
    var random = new Random();
    var frameSequence = 0;

    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.Pragma = "no-cache";
    context.Response.ContentType = "multipart/x-mixed-replace; boundary=frame";

    try
    {
        while (!context.RequestAborted.IsCancellationRequested)
        {
            frameSequence++;
            var payload = new byte[900 + random.Next(0, 300)];
            random.NextBytes(payload);

            var header = new StringBuilder()
                .Append("--frame\r\n")
                .Append("Content-Type: image/jpeg\r\n")
                .Append($"Content-Length: {payload.Length}\r\n")
                .Append($"X-Frame-Id: {frameSequence}\r\n\r\n")
                .ToString();

            await context.Response.WriteAsync(header, context.RequestAborted);
            await context.Response.Body.WriteAsync(payload, context.RequestAborted);
            await context.Response.WriteAsync("\r\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);

            await Task.Delay(delay, context.RequestAborted);
        }
    }
    catch (OperationCanceledException)
    {
        // Expected during client disconnects.
    }
});

app.MapHub<TelemetryHub>("/hubs/telemetry");

app.Run();

static Random BuildSeededRandom(string node, DateTimeOffset timestamp)
{
    var fiveMinuteBucket = timestamp.ToUnixTimeSeconds() / 300;
    var seed = HashCode.Combine(node, fiveMinuteBucket);
    return new Random(seed);
}

static TelemetrySignal BuildTelemetrySignal(string node, DateTimeOffset timestamp, Random random)
{
    var jitter = (random.NextDouble() - 0.5) * 6;
    var phase = timestamp.ToUnixTimeSeconds() / 15d;

    var cpu = Math.Clamp(46 + Math.Sin(phase) * 11 + jitter, 16, 96);
    var memory = Math.Clamp(54 + Math.Cos(phase / 1.4) * 10 + jitter, 20, 94);
    var packetLoss = Math.Clamp(0.08 + Math.Abs(Math.Sin(phase / 2.3)) * 0.34 + random.NextDouble() * 0.05, 0.01, 1.8);
    var activeStreams = 22 + (int)Math.Round(Math.Abs(Math.Cos(phase / 3.2)) * 14) + random.Next(0, 3);
    var alertLevel = packetLoss > 1.1 ? 2 : packetLoss > 0.6 ? 1 : 0;

    return new TelemetrySignal(
        node,
        timestamp,
        Math.Round(cpu, 2),
        Math.Round(memory, 2),
        Math.Round(packetLoss, 3),
        activeStreams,
        alertLevel);
}

internal sealed class TelemetryHub : Hub;

internal sealed class TelemetryBroadcastService : BackgroundService
{
    private readonly IHubContext<TelemetryHub> _hubContext;
    private readonly Random _random = new();

    public TelemetryBroadcastService(IHubContext<TelemetryHub> hubContext)
    {
        _hubContext = hubContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var signal = CreateSignal("edge-gateway-a", DateTimeOffset.UtcNow);
            await _hubContext.Clients.All.SendAsync("telemetry", signal, stoppingToken);
            await Task.Delay(300, stoppingToken);
        }
    }

    private TelemetrySignal CreateSignal(string node, DateTimeOffset timestamp)
    {
        var jitter = (_random.NextDouble() - 0.5) * 8;
        var phase = timestamp.ToUnixTimeSeconds() / 9d;

        var cpu = Math.Clamp(52 + Math.Sin(phase) * 18 + jitter, 18, 99);
        var memory = Math.Clamp(58 + Math.Cos(phase / 1.8) * 14 + jitter, 22, 97);
        var packetLoss = Math.Clamp(0.04 + Math.Abs(Math.Sin(phase / 1.9)) * 0.54 + _random.NextDouble() * 0.08, 0.01, 2.5);
        var activeStreams = 18 + (int)Math.Round(Math.Abs(Math.Cos(phase / 2.4)) * 19) + _random.Next(0, 4);
        var alertLevel = packetLoss > 1.4 ? 2 : packetLoss > 0.75 ? 1 : 0;

        return new TelemetrySignal(
            node,
            timestamp,
            Math.Round(cpu, 2),
            Math.Round(memory, 2),
            Math.Round(packetLoss, 3),
            activeStreams,
            alertLevel);
    }
}

internal sealed record ServiceStatus(string Service, string Status, DateTimeOffset Utc);

internal sealed record TelemetryEnvelope(string Node, IReadOnlyList<TelemetryPoint> Samples);

internal sealed record TelemetryPoint(
    DateTimeOffset Timestamp,
    double CpuLoadPercent,
    double MemoryLoadPercent,
    double PacketLossPercent,
    int ActiveStreams,
    int AlertLevel);

internal sealed record TelemetrySignal(
    string Node,
    DateTimeOffset Timestamp,
    double CpuLoadPercent,
    double MemoryLoadPercent,
    double PacketLossPercent,
    int ActiveStreams,
    int AlertLevel);

internal sealed record VideoFrameMeta(
    int Width,
    int Height,
    int Fps,
    string Codec,
    int KeyFrameInterval,
    int EstimatedPacketRatePerSecond,
    string Source,
    DateTimeOffset GeneratedAtUtc);

internal sealed record WebRtcTrackEnvelope(string Profile, IReadOnlyList<WebRtcTrack> Tracks);

internal sealed record WebRtcTrack(
    string Id,
    string Kind,
    string Direction,
    string Codec,
    int? MaxBitrateKbps,
    int? TargetFps,
    string Purpose);
