using System.Text.Json;
using System.Threading.Channels;
using Microsoft.JSInterop;

namespace CvWeb.Client.Services;

public sealed class MockStreamService : IMockStreamService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IJSRuntime _js;
    private readonly object _sync = new();

    private readonly Channel<TelemetrySignal> _telemetryIngress = Channel.CreateBounded<TelemetrySignal>(
        new BoundedChannelOptions(512)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

    private readonly Channel<TelemetryJsonSample> _telemetryJsonIngress = Channel.CreateBounded<TelemetryJsonSample>(
        new BoundedChannelOptions(512)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

    private readonly Channel<MjpegStreamSample> _mjpegIngress = Channel.CreateBounded<MjpegStreamSample>(
        new BoundedChannelOptions(512)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

    private readonly Channel<MjpegByteChunk> _mjpegByteIngress = Channel.CreateBounded<MjpegByteChunk>(
        new BoundedChannelOptions(1024)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

    private readonly List<Channel<TelemetrySignal>> _telemetrySubscribers = [];
    private readonly List<Channel<TelemetryJsonSample>> _telemetryJsonSubscribers = [];
    private readonly List<Channel<MjpegStreamSample>> _mjpegSubscribers = [];
    private readonly List<Channel<MjpegByteChunk>> _mjpegByteSubscribers = [];
    private readonly Dictionary<string, WebRtcTrackEnvelope> _webrtcProfiles = new(StringComparer.OrdinalIgnoreCase);

    private readonly CancellationTokenSource _fanoutCts = new();
    private readonly Task _telemetryFanoutTask;
    private readonly Task _telemetryJsonFanoutTask;
    private readonly Task _mjpegFanoutTask;
    private readonly Task _mjpegByteFanoutTask;

    private DotNetObjectReference<MockStreamService>? _dotNetReference;
    private bool _isStarted;

    public MockStreamService(IJSRuntime js)
    {
        _js = js;

        _telemetryFanoutTask = Task.Run(() => TelemetryFanOutAsync(_fanoutCts.Token));
        _telemetryJsonFanoutTask = Task.Run(() => TelemetryJsonFanOutAsync(_fanoutCts.Token));
        _mjpegFanoutTask = Task.Run(() => MjpegFanOutAsync(_fanoutCts.Token));
        _mjpegByteFanoutTask = Task.Run(() => MjpegByteFanOutAsync(_fanoutCts.Token));
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isStarted)
            {
                return;
            }

            _dotNetReference = DotNetObjectReference.Create(this);
            _isStarted = true;
        }

        await _js.InvokeVoidAsync("cvDashboard.startMockStreamWorker", cancellationToken, _dotNetReference);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        DotNetObjectReference<MockStreamService>? referenceToDispose = null;
        var shouldStop = false;

        lock (_sync)
        {
            if (_isStarted)
            {
                shouldStop = true;
                _isStarted = false;
                referenceToDispose = _dotNetReference;
                _dotNetReference = null;
            }
        }

        if (!shouldStop)
        {
            return;
        }

        await _js.InvokeVoidAsync("cvDashboard.stopMockStreamWorker", cancellationToken);
        referenceToDispose?.Dispose();
    }

    public ChannelReader<TelemetrySignal> SubscribeTelemetry(CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<TelemetrySignal>(
            new BoundedChannelOptions(128)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });

        lock (_sync)
        {
            _telemetrySubscribers.Add(channel);
        }

        if (cancellationToken.CanBeCanceled)
        {
            _ = RemoveTelemetrySubscriberOnCancelAsync(channel, cancellationToken);
        }

        return channel.Reader;
    }

    public ChannelReader<TelemetryJsonSample> SubscribeTelemetryJson(CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<TelemetryJsonSample>(
            new BoundedChannelOptions(128)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });

        lock (_sync)
        {
            _telemetryJsonSubscribers.Add(channel);
        }

        if (cancellationToken.CanBeCanceled)
        {
            _ = RemoveTelemetryJsonSubscriberOnCancelAsync(channel, cancellationToken);
        }

        return channel.Reader;
    }

    public ChannelReader<MjpegStreamSample> SubscribeMjpeg(CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<MjpegStreamSample>(
            new BoundedChannelOptions(128)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });

        lock (_sync)
        {
            _mjpegSubscribers.Add(channel);
        }

        if (cancellationToken.CanBeCanceled)
        {
            _ = RemoveMjpegSubscriberOnCancelAsync(channel, cancellationToken);
        }

        return channel.Reader;
    }

    public ChannelReader<MjpegByteChunk> SubscribeMjpegByteChunks(CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<MjpegByteChunk>(
            new BoundedChannelOptions(256)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });

        lock (_sync)
        {
            _mjpegByteSubscribers.Add(channel);
        }

        if (cancellationToken.CanBeCanceled)
        {
            _ = RemoveMjpegByteSubscriberOnCancelAsync(channel, cancellationToken);
        }

        return channel.Reader;
    }

    public ValueTask<WebRtcTrackEnvelope> GetWebRtcTrackProfileAsync(string profile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var safeProfile = string.IsNullOrWhiteSpace(profile) ? "balanced" : profile.Trim().ToLowerInvariant();

        lock (_sync)
        {
            if (_webrtcProfiles.TryGetValue(safeProfile, out var envelope))
            {
                return ValueTask.FromResult(envelope);
            }
        }

        return ValueTask.FromResult(BuildFallbackProfile(safeProfile));
    }

    [JSInvokable]
    public ValueTask HandleWorkerMessage(string messageType, string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(messageType))
        {
            return ValueTask.CompletedTask;
        }

        try
        {
            switch (messageType)
            {
                case "telemetry":
                {
                    _telemetryJsonIngress.Writer.TryWrite(new TelemetryJsonSample(
                        DateTimeOffset.UtcNow,
                        payloadJson));

                    var signal = JsonSerializer.Deserialize<TelemetrySignal>(payloadJson, JsonOptions);
                    if (signal is not null)
                    {
                        _telemetryIngress.Writer.TryWrite(signal);
                    }

                    break;
                }
                case "mjpeg":
                {
                    var sample = JsonSerializer.Deserialize<MjpegStreamSample>(payloadJson, JsonOptions);
                    if (sample is not null)
                    {
                        _mjpegIngress.Writer.TryWrite(sample);
                    }

                    break;
                }
                case "mjpeg-byte-chunk":
                {
                    var envelope = JsonSerializer.Deserialize<MjpegByteChunkEnvelope>(payloadJson, JsonOptions);
                    if (envelope is not null && TryDecodeBase64(envelope.ChunkBase64, out var chunkBytes))
                    {
                        _mjpegByteIngress.Writer.TryWrite(new MjpegByteChunk(
                            envelope.Sequence,
                            envelope.Timestamp,
                            chunkBytes));
                    }

                    break;
                }
                case "webrtc-profile":
                {
                    var envelope = JsonSerializer.Deserialize<WebRtcTrackEnvelope>(payloadJson, JsonOptions);
                    if (envelope is not null)
                    {
                        lock (_sync)
                        {
                            _webrtcProfiles[envelope.Profile] = envelope;
                        }
                    }

                    break;
                }
            }
        }
        catch (JsonException)
        {
            // Ignore malformed worker payloads during refreshes or navigation transitions.
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync();
        }
        catch (JSException)
        {
            // Ignore shutdown races where JS runtime is already detached.
        }
        catch (InvalidOperationException)
        {
            // Ignore shutdown races where JS runtime is unavailable.
        }

        _fanoutCts.Cancel();

        _telemetryIngress.Writer.TryComplete();
        _telemetryJsonIngress.Writer.TryComplete();
        _mjpegIngress.Writer.TryComplete();
        _mjpegByteIngress.Writer.TryComplete();

        CompleteSubscribers(_telemetrySubscribers);
        CompleteSubscribers(_telemetryJsonSubscribers);
        CompleteSubscribers(_mjpegSubscribers);
        CompleteSubscribers(_mjpegByteSubscribers);

        try
        {
            await _telemetryFanoutTask;
            await _telemetryJsonFanoutTask;
            await _mjpegFanoutTask;
            await _mjpegByteFanoutTask;
        }
        catch (OperationCanceledException)
        {
            // Expected during normal disposal.
        }

        _fanoutCts.Dispose();
    }

    private static void CompleteSubscribers<T>(List<Channel<T>> subscribers)
    {
        foreach (var subscriber in subscribers)
        {
            subscriber.Writer.TryComplete();
        }

        subscribers.Clear();
    }

    private async Task RemoveTelemetrySubscriberOnCancelAsync(Channel<TelemetrySignal> channel, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            lock (_sync)
            {
                _telemetrySubscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
        }
    }

    private async Task RemoveTelemetryJsonSubscriberOnCancelAsync(Channel<TelemetryJsonSample> channel, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            lock (_sync)
            {
                _telemetryJsonSubscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
        }
    }

    private async Task RemoveMjpegSubscriberOnCancelAsync(Channel<MjpegStreamSample> channel, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            lock (_sync)
            {
                _mjpegSubscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
        }
    }

    private async Task RemoveMjpegByteSubscriberOnCancelAsync(Channel<MjpegByteChunk> channel, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            lock (_sync)
            {
                _mjpegByteSubscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
        }
    }

    private async Task TelemetryFanOutAsync(CancellationToken cancellationToken)
    {
        await foreach (var signal in _telemetryIngress.Reader.ReadAllAsync(cancellationToken))
        {
            Channel<TelemetrySignal>[] subscribers;
            lock (_sync)
            {
                subscribers = [.. _telemetrySubscribers];
            }

            foreach (var subscriber in subscribers)
            {
                _ = subscriber.Writer.TryWrite(signal);
            }
        }
    }

    private async Task TelemetryJsonFanOutAsync(CancellationToken cancellationToken)
    {
        await foreach (var sample in _telemetryJsonIngress.Reader.ReadAllAsync(cancellationToken))
        {
            Channel<TelemetryJsonSample>[] subscribers;
            lock (_sync)
            {
                subscribers = [.. _telemetryJsonSubscribers];
            }

            foreach (var subscriber in subscribers)
            {
                _ = subscriber.Writer.TryWrite(sample);
            }
        }
    }

    private async Task MjpegFanOutAsync(CancellationToken cancellationToken)
    {
        await foreach (var sample in _mjpegIngress.Reader.ReadAllAsync(cancellationToken))
        {
            Channel<MjpegStreamSample>[] subscribers;
            lock (_sync)
            {
                subscribers = [.. _mjpegSubscribers];
            }

            foreach (var subscriber in subscribers)
            {
                _ = subscriber.Writer.TryWrite(sample);
            }
        }
    }

    private async Task MjpegByteFanOutAsync(CancellationToken cancellationToken)
    {
        await foreach (var sample in _mjpegByteIngress.Reader.ReadAllAsync(cancellationToken))
        {
            Channel<MjpegByteChunk>[] subscribers;
            lock (_sync)
            {
                subscribers = [.. _mjpegByteSubscribers];
            }

            foreach (var subscriber in subscribers)
            {
                _ = subscriber.Writer.TryWrite(sample);
            }
        }
    }

    private static bool TryDecodeBase64(string? payload, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(payload);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed record MjpegByteChunkEnvelope(
        long Sequence,
        DateTimeOffset Timestamp,
        string ChunkBase64);

    private static WebRtcTrackEnvelope BuildFallbackProfile(string profile)
    {
        var normalized = profile switch
        {
            "low-bandwidth" => "low-bandwidth",
            "high-fidelity" => "high-fidelity",
            _ => "balanced"
        };

        IReadOnlyList<WebRtcTrack> tracks = normalized switch
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

        return new WebRtcTrackEnvelope(normalized, tracks);
    }
}
