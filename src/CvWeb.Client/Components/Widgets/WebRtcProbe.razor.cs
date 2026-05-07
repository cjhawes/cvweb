using CvWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CvWeb.Client.Components.Widgets;

public sealed partial class WebRtcProbe : IAsyncDisposable
{
    private const double ChartWidth = 320d;
    private const double ChartHeight = 92d;

    private readonly string VideoId = $"webrtc-probe-{Guid.NewGuid():N}";
    private readonly WebRtcProbeMetricsEngine _metrics = new();

    private DotNetObjectReference<WebRtcProbe>? _dotNetReference;

    private string ConnectionState { get; set; } = "connecting";
    private double NetworkHealth { get; set; }
    private double BitrateKbps { get; set; }
    private double PacketLossPercent { get; set; }
    private double JitterMs { get; set; }
    private double RoundTripTimeMs { get; set; }
    private double FramesPerSecond { get; set; }
    private long LastSequence { get; set; }
    private string TrackProfile { get; set; } = "balanced";
    private int TrackCount { get; set; }

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Inject]
    private IMockStreamService MockStreams { get; set; } = default!;

    private string StatusClass => ConnectionState switch
    {
        "connected" => "status-ok",
        "degraded" => "status-warning",
        _ => "status-critical"
    };

    private string HealthChipClass => NetworkHealth switch
    {
        >= 82 => "metric-chip-ok",
        >= 60 => "metric-chip-warning",
        _ => "metric-chip-critical"
    };

    private string StatusLabel => ConnectionState switch
    {
        "connected" => "Peer Connection: connected",
        "degraded" => "Peer Connection: degraded",
        _ => "Peer Connection: connecting"
    };

    private string BitratePoints => _metrics.BuildPolyline(
        WebRtcSeriesMetric.Bitrate,
        ChartWidth,
        ChartHeight,
        _metrics.GetWindowMax(WebRtcSeriesMetric.Bitrate, 3200d));

    private string PacketLossPoints => _metrics.BuildPolyline(
        WebRtcSeriesMetric.PacketLoss,
        ChartWidth,
        ChartHeight,
        _metrics.GetWindowMax(WebRtcSeriesMetric.PacketLoss, 2.5d));

    private string JitterPoints => _metrics.BuildPolyline(
        WebRtcSeriesMetric.Jitter,
        ChartWidth,
        ChartHeight,
        _metrics.GetWindowMax(WebRtcSeriesMetric.Jitter, 35d));

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var profile = await MockStreams.GetWebRtcTrackProfileAsync("balanced");
            TrackProfile = profile.Profile;
            TrackCount = profile.Tracks.Count;
        }
        catch
        {
            TrackProfile = "offline";
            TrackCount = 0;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _dotNetReference = DotNetObjectReference.Create(this);

        try
        {
            await JS.InvokeVoidAsync("cvDashboard.startWebRtcProbe", VideoId, _dotNetReference);
        }
        catch (JSException)
        {
            ConnectionState = "degraded";
            await InvokeAsync(StateHasChanged);
        }
    }

    [JSInvokable]
    public Task UpdateWebRtcProbeStats(WebRtcProbeStatsSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        return InvokeAsync(() =>
        {
            LastSequence = Math.Max(0, sample.Sequence);
            ConnectionState = string.IsNullOrWhiteSpace(sample.ConnectionState) ? "degraded" : sample.ConnectionState;

            BitrateKbps = Math.Max(0d, sample.BitrateKbps);
            PacketLossPercent = Math.Max(0d, sample.PacketLossPercent);
            JitterMs = Math.Max(0d, sample.JitterMs);
            RoundTripTimeMs = Math.Max(0d, sample.RoundTripTimeMs);
            FramesPerSecond = Math.Max(0d, sample.FramesPerSecond);

            _metrics.Push(sample);
            NetworkHealth = _metrics.ComputeHealthScore(sample);

            StateHasChanged();
        });
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("cvDashboard.stopWebRtcProbe", VideoId);
        }
        catch (JSException)
        {
            // Ignore teardown races during route transitions.
        }
        catch (InvalidOperationException)
        {
            // Ignore teardown when JS runtime is unavailable.
        }

        _dotNetReference?.Dispose();
    }
}
