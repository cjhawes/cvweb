using System.Buffers;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using CvWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CvWeb.Client.Components.Widgets;

public sealed partial class MjpegDecoder : IAsyncDisposable
{
    private static readonly byte[] BoundaryBytes = Encoding.ASCII.GetBytes("--frame");
    private static readonly byte[] HeaderTerminatorBytes = Encoding.ASCII.GetBytes("\r\n\r\n");
    private static readonly byte[] CrLfBytes = Encoding.ASCII.GetBytes("\r\n");

    private readonly string CanvasId = $"mjpeg-decoder-{Guid.NewGuid():N}";
    private readonly CancellationTokenSource _streamCts = new();
    private readonly Queue<DecodedFrame> _decodedFrames = new();
    private readonly object _queueSync = new();

    private DotNetObjectReference<MjpegDecoder>? _dotNetReference;
    private Task? _ingestTask;
    private Task? _renderTask;

    private byte[] _carryBuffer = Array.Empty<byte>();
    private bool _isDrawing;

    private int ChunkCount { get; set; }
    private int BoundaryCount { get; set; }
    private int DecodedFrames { get; set; }
    private int RenderedFrames { get; set; }
    private int DroppedFrames { get; set; }
    private int QueuedFrames { get; set; }
    private double RenderFps { get; set; }
    private string StreamState { get; set; } = "warming";

    [Inject]
    private IMockStreamService MockStreams { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    private string StatusClass => StreamState switch
    {
        "connected" => "status-ok",
        "degraded" => "status-warning",
        _ => "status-critical"
    };

    private string StatusLabel => StreamState switch
    {
        "connected" => "Software MJPEG decode active",
        "degraded" => "Stream active with dropped frames",
        "waiting" => "Waiting for MJPEG byte stream",
        _ => "Initializing decoder"
    };

    protected override void OnInitialized()
    {
        var reader = MockStreams.SubscribeMjpegByteChunks(_streamCts.Token);
        _ingestTask = Task.Run(() => ConsumeStreamAsync(reader, _streamCts.Token));
        _renderTask = Task.Run(() => RenderLoopAsync(_streamCts.Token));
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _dotNetReference = DotNetObjectReference.Create(this);
        await JS.InvokeVoidAsync("cvDashboard.startMjpegDecoder", CanvasId, string.Empty, _dotNetReference);
    }

    [JSInvokable]
    public Task UpdateMjpegStats(int boundaryCount, int renderFps)
    {
        BoundaryCount = Math.Max(BoundaryCount, boundaryCount);
        RenderFps = Math.Max(0, renderFps);
        return InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        _streamCts.Cancel();

        if (_ingestTask is not null)
        {
            try
            {
                await _ingestTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during teardown.
            }
        }

        if (_renderTask is not null)
        {
            try
            {
                await _renderTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during teardown.
            }
        }

        try
        {
            await JS.InvokeVoidAsync("cvDashboard.stopMjpegDecoder", CanvasId);
        }
        catch (JSException)
        {
            // Ignore teardown races during route transitions.
        }
        catch (InvalidOperationException)
        {
            // Ignore teardown when JS runtime is unavailable.
        }

        lock (_queueSync)
        {
            _decodedFrames.Clear();
            QueuedFrames = 0;
        }

        _dotNetReference?.Dispose();
        _streamCts.Dispose();
    }

    private async Task ConsumeStreamAsync(ChannelReader<MjpegByteChunk> reader, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var chunk in reader.ReadAllAsync(cancellationToken))
            {
                ChunkCount += 1;
                StreamState = "connected";

                ParseChunk(chunk.ChunkBytes);

                if (ChunkCount % 8 == 0)
                {
                    await InvokeAsync(StateHasChanged);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during component teardown.
        }
        catch
        {
            StreamState = "degraded";
            await InvokeAsync(StateHasChanged);
        }
    }

    private void ParseChunk(byte[] chunkBytes)
    {
        var merged = new byte[_carryBuffer.Length + chunkBytes.Length];
        Buffer.BlockCopy(_carryBuffer, 0, merged, 0, _carryBuffer.Length);
        Buffer.BlockCopy(chunkBytes, 0, merged, _carryBuffer.Length, chunkBytes.Length);

        var scanOffset = 0;
        while (scanOffset < merged.Length)
        {
            var boundaryStart = FindSequence(merged, BoundaryBytes, scanOffset);
            if (boundaryStart < 0)
            {
                break;
            }

            var headerStart = boundaryStart + BoundaryBytes.Length;
            var headerEnd = FindSequence(merged, HeaderTerminatorBytes, headerStart);
            if (headerEnd < 0)
            {
                break;
            }

            var headerSpan = new ReadOnlySpan<byte>(merged, headerStart, headerEnd - headerStart);
            if (!TryGetContentLength(headerSpan, out var contentLength))
            {
                scanOffset = headerEnd + HeaderTerminatorBytes.Length;
                continue;
            }

            var payloadStart = headerEnd + HeaderTerminatorBytes.Length;
            var payloadEnd = payloadStart + contentLength;
            if (payloadEnd + CrLfBytes.Length > merged.Length)
            {
                break;
            }

            if (merged[payloadEnd] != CrLfBytes[0] || merged[payloadEnd + 1] != CrLfBytes[1])
            {
                scanOffset = payloadEnd;
                continue;
            }

            var frameBytes = new byte[contentLength];
            Buffer.BlockCopy(merged, payloadStart, frameBytes, 0, contentLength);

            EnqueueFrame(frameBytes);
            DecodedFrames += 1;
            BoundaryCount += 1;

            scanOffset = payloadEnd + CrLfBytes.Length;
        }

        if (scanOffset >= merged.Length)
        {
            _carryBuffer = Array.Empty<byte>();
            return;
        }

        var remaining = merged.Length - scanOffset;
        _carryBuffer = new byte[remaining];
        Buffer.BlockCopy(merged, scanOffset, _carryBuffer, 0, remaining);
    }

    private void EnqueueFrame(byte[] frameBytes)
    {
        lock (_queueSync)
        {
            const int maxQueueDepth = 4;
            if (_decodedFrames.Count >= maxQueueDepth)
            {
                _decodedFrames.Dequeue();
                DroppedFrames += 1;
                StreamState = "degraded";
            }

            _decodedFrames.Enqueue(new DecodedFrame(frameBytes));
            QueuedFrames = _decodedFrames.Count;
        }
    }

    private async Task RenderLoopAsync(CancellationToken cancellationToken)
    {
        var frameInterval = TimeSpan.FromMilliseconds(1000d / 30d);
        var renderedInWindow = 0;
        var fpsWindowStart = DateTimeOffset.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = TryTakeLatestFrame();
            if (frame is not null)
            {
                if (!_isDrawing)
                {
                    _isDrawing = true;
                    try
                    {
                        await JS.InvokeVoidAsync("cvDashboard.drawMjpegFrameBytes", CanvasId, Convert.ToBase64String(frame.FrameBytes));
                        RenderedFrames += 1;
                        renderedInWindow += 1;
                    }
                    finally
                    {
                        _isDrawing = false;
                    }
                }
            }
            else if (ChunkCount == 0)
            {
                StreamState = "waiting";
            }

            var elapsedSeconds = (DateTimeOffset.UtcNow - fpsWindowStart).TotalSeconds;
            if (elapsedSeconds >= 1)
            {
                RenderFps = renderedInWindow / elapsedSeconds;
                renderedInWindow = 0;
                fpsWindowStart = DateTimeOffset.UtcNow;
                await InvokeAsync(StateHasChanged);
            }

            await Task.Delay(frameInterval, cancellationToken);
        }
    }

    private DecodedFrame? TryTakeLatestFrame()
    {
        lock (_queueSync)
        {
            if (_decodedFrames.Count == 0)
            {
                return null;
            }

            while (_decodedFrames.Count > 1)
            {
                _decodedFrames.Dequeue();
                DroppedFrames += 1;
            }

            var frame = _decodedFrames.Dequeue();
            QueuedFrames = _decodedFrames.Count;
            return frame;
        }
    }

    private static int FindSequence(byte[] buffer, byte[] needle, int startIndex)
    {
        if (needle.Length == 0 || buffer.Length < needle.Length)
        {
            return -1;
        }

        var limit = buffer.Length - needle.Length;
        for (var index = Math.Max(0, startIndex); index <= limit; index += 1)
        {
            var matched = true;
            for (var offset = 0; offset < needle.Length; offset += 1)
            {
                if (buffer[index + offset] != needle[offset])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TryGetContentLength(ReadOnlySpan<byte> headerBytes, out int contentLength)
    {
        const string marker = "Content-Length:";
        contentLength = 0;

        var headerText = Encoding.ASCII.GetString(headerBytes);
        var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (!line.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var raw = line[marker.Length..].Trim();
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
            {
                contentLength = parsed;
                return true;
            }
        }

        return false;
    }

    private sealed record DecodedFrame(byte[] FrameBytes);
}
