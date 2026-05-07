using System.Text.Json;
using System.Threading.Channels;
using System.Runtime.CompilerServices;
using Microsoft.JSInterop;

namespace CvWeb.Client.Services;

/// <summary>
/// Provides an in-browser synthetic stream broker backed by a Web Worker.
/// </summary>
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

    private readonly Channel<MjpegByteChunk> _mjpegByteIngress = Channel.CreateBounded<MjpegByteChunk>(
        new BoundedChannelOptions(1024)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

    private readonly List<Channel<TelemetrySignal>> _telemetrySubscribers = [];
    private readonly List<Channel<TelemetryJsonSample>> _telemetryJsonSubscribers = [];
    private readonly List<Channel<MjpegByteChunk>> _mjpegByteSubscribers = [];
    private readonly List<CancellationTokenRegistration> _subscriberCancellationRegistrations = [];
    private readonly Dictionary<string, WebRtcTrackEnvelope> _webrtcProfiles = new(StringComparer.OrdinalIgnoreCase);

    private readonly CancellationTokenSource _fanoutCts = new();
    private readonly Task _telemetryFanoutTask;
    private readonly Task _telemetryJsonFanoutTask;
    private readonly Task _mjpegByteFanoutTask;

    private DotNetObjectReference<MockStreamService>? _dotNetReference;
    private bool _isStarted;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MockStreamService"/> class.
    /// </summary>
    /// <param name="js">The JS runtime used to start and stop worker sessions.</param>
    public MockStreamService(IJSRuntime js)
    {
        _js = js;

        _telemetryFanoutTask = Task.Run(() => TelemetryFanOutAsync(_fanoutCts.Token));
        _telemetryJsonFanoutTask = Task.Run(() => TelemetryJsonFanOutAsync(_fanoutCts.Token));
        _mjpegByteFanoutTask = Task.Run(() => MjpegByteFanOutAsync(_fanoutCts.Token));
    }

    /// <inheritdoc />
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        DotNetObjectReference<MockStreamService>? referenceToStart;

        lock (_sync)
        {
            ThrowIfDisposed();

            if (_isStarted)
            {
                return;
            }

            referenceToStart = DotNetObjectReference.Create(this);
            _dotNetReference = referenceToStart;
            _isStarted = true;
        }

        try
        {
            await _js.InvokeVoidAsync("cvDashboard.startMockStreamWorker", cancellationToken, referenceToStart);
        }
        catch
        {
            lock (_sync)
            {
                if (ReferenceEquals(_dotNetReference, referenceToStart))
                {
                    _dotNetReference = null;
                    _isStarted = false;
                }
            }

            referenceToStart.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        DotNetObjectReference<MockStreamService>? referenceToDispose = null;
        var shouldStopWorker = false;

        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }

            if (_isStarted)
            {
                shouldStopWorker = true;
                _isStarted = false;
                referenceToDispose = _dotNetReference;
                _dotNetReference = null;
            }
        }

        if (!shouldStopWorker)
        {
            return;
        }

        try
        {
            await _js.InvokeVoidAsync("cvDashboard.stopMockStreamWorker", cancellationToken);
        }
        finally
        {
            referenceToDispose?.Dispose();
        }
    }

    /// <inheritdoc />
    public ChannelReader<TelemetrySignal> SubscribeTelemetry(CancellationToken cancellationToken = default)
    {
        var channel = CreateSubscriberChannel<TelemetrySignal>(128);

        lock (_sync)
        {
            ThrowIfDisposed();
            _telemetrySubscribers.Add(channel);
        }

        RegisterCancellation(channel, _telemetrySubscribers, cancellationToken);

        return channel.Reader;
    }

    /// <inheritdoc />
    public ChannelReader<TelemetryJsonSample> SubscribeTelemetryJson(CancellationToken cancellationToken = default)
    {
        var channel = CreateSubscriberChannel<TelemetryJsonSample>(128);

        lock (_sync)
        {
            ThrowIfDisposed();
            _telemetryJsonSubscribers.Add(channel);
        }

        RegisterCancellation(channel, _telemetryJsonSubscribers, cancellationToken);

        return channel.Reader;
    }

    /// <inheritdoc />
    public ChannelReader<MjpegByteChunk> SubscribeMjpegByteChunks(CancellationToken cancellationToken = default)
    {
        var channel = CreateSubscriberChannel<MjpegByteChunk>(256);

        lock (_sync)
        {
            ThrowIfDisposed();
            _mjpegByteSubscribers.Add(channel);
        }

        RegisterCancellation(channel, _mjpegByteSubscribers, cancellationToken);

        return channel.Reader;
    }

    /// <inheritdoc />
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

    /// <summary>
    /// Handles a worker message and publishes supported payloads into bounded ingress channels.
    /// </summary>
    /// <param name="messageType">The worker message type discriminator.</param>
    /// <param name="payloadJson">The serialized payload.</param>
    /// <returns>A completed value task.</returns>
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

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        DotNetObjectReference<MockStreamService>? referenceToDispose;
        CancellationTokenRegistration[] registrations;
        Channel<TelemetrySignal>[] telemetrySubscribers;
        Channel<TelemetryJsonSample>[] telemetryJsonSubscribers;
        Channel<MjpegByteChunk>[] mjpegByteSubscribers;
        bool shouldStopWorker;

        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            shouldStopWorker = _isStarted;
            _isStarted = false;

            referenceToDispose = _dotNetReference;
            _dotNetReference = null;

            registrations = [.. _subscriberCancellationRegistrations];
            _subscriberCancellationRegistrations.Clear();

            telemetrySubscribers = [.. _telemetrySubscribers];
            _telemetrySubscribers.Clear();

            telemetryJsonSubscribers = [.. _telemetryJsonSubscribers];
            _telemetryJsonSubscribers.Clear();

            mjpegByteSubscribers = [.. _mjpegByteSubscribers];
            _mjpegByteSubscribers.Clear();
        }

        foreach (var registration in registrations)
        {
            registration.Dispose();
        }

        if (shouldStopWorker)
        {
            try
            {
                await _js.InvokeVoidAsync("cvDashboard.stopMockStreamWorker");
            }
            catch (JSException)
            {
                // Ignore shutdown races where JS runtime is already detached.
            }
            catch (InvalidOperationException)
            {
                // Ignore shutdown races where JS runtime is unavailable.
            }
            finally
            {
                referenceToDispose?.Dispose();
            }
        }
        else
        {
            referenceToDispose?.Dispose();
        }

        try
        {
            _fanoutCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ignore duplicate cancellation during shutdown.
        }

        _telemetryIngress.Writer.TryComplete();
        _telemetryJsonIngress.Writer.TryComplete();
        _mjpegByteIngress.Writer.TryComplete();

        CompleteSubscribers(telemetrySubscribers);
        CompleteSubscribers(telemetryJsonSubscribers);
        CompleteSubscribers(mjpegByteSubscribers);

        try
        {
            await _telemetryFanoutTask;
            await _telemetryJsonFanoutTask;
            await _mjpegByteFanoutTask;
        }
        catch (OperationCanceledException)
        {
            // Expected during normal disposal.
        }

        _fanoutCts.Dispose();
    }

    private void RegisterCancellation<T>(Channel<T> channel, List<Channel<T>> subscribers, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            return;
        }

        var registrationBox = new StrongBox<CancellationTokenRegistration>();
        var registration = cancellationToken.Register(() =>
        {
            lock (_sync)
            {
                subscribers.Remove(channel);
                _subscriberCancellationRegistrations.Remove(registrationBox.Value);
            }

            channel.Writer.TryComplete();
            registrationBox.Value.Dispose();
        });
        registrationBox.Value = registration;

        lock (_sync)
        {
            if (_isDisposed)
            {
                registration.Dispose();
                channel.Writer.TryComplete();
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                registration.Dispose();
                channel.Writer.TryComplete();
                return;
            }

            _subscriberCancellationRegistrations.Add(registration);
        }
    }

    private static Channel<T> CreateSubscriberChannel<T>(int capacity)
    {
        return Channel.CreateBounded<T>(
            new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });
    }

    private static void CompleteSubscribers<T>(IEnumerable<Channel<T>> subscribers)
    {
        foreach (var subscriber in subscribers)
        {
            subscriber.Writer.TryComplete();
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

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(MockStreamService));
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
