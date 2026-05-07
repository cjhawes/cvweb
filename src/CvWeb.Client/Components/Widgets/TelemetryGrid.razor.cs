using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CvWeb.Client.Components.Widgets;

public sealed partial class TelemetryGrid : IAsyncDisposable
{
    private readonly string CanvasId = $"telemetry-grid-{Guid.NewGuid():N}";
    private DotNetObjectReference<TelemetryGrid>? _dotNetReference;

    private int SensorCount { get; set; } = 1024;
    private double IngestRateHz { get; set; }
    private double RenderRateFps { get; set; }
    private int DroppedFrames { get; set; }
    private int AlertSensors { get; set; }
    private double CpuAveragePercent { get; set; }
    private double PacketLossAveragePercent { get; set; }
    private long LastSequence { get; set; }
    private string StreamState { get; set; } = "warming";

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
        "connected" => "Telemetry worker active at 60Hz",
        "degraded" => "Telemetry worker active with frame drops",
        "waiting" => "Waiting for telemetry worker feed",
        _ => "Telemetry grid warming up"
    };

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _dotNetReference = DotNetObjectReference.Create(this);

        try
        {
            await JS.InvokeVoidAsync("cvDashboard.startTelemetryGrid", CanvasId, _dotNetReference, 32, 32);
        }
        catch (JSException)
        {
            StreamState = "degraded";
            await InvokeAsync(StateHasChanged);
        }
    }

    [JSInvokable]
    public Task UpdateTelemetryGridStats(
        int sensorCount,
        double ingestRateHz,
        double renderRateFps,
        int droppedFrames,
        int alertSensors,
        double cpuAveragePercent,
        double packetLossAveragePercent,
        string streamState,
        long sequence)
    {
        SensorCount = Math.Max(0, sensorCount);
        IngestRateHz = Math.Max(0, ingestRateHz);
        RenderRateFps = Math.Max(0, renderRateFps);
        DroppedFrames = Math.Max(0, droppedFrames);
        AlertSensors = Math.Max(0, alertSensors);
        CpuAveragePercent = Math.Max(0, cpuAveragePercent);
        PacketLossAveragePercent = Math.Max(0, packetLossAveragePercent);
        StreamState = string.IsNullOrWhiteSpace(streamState) ? "degraded" : streamState;
        LastSequence = Math.Max(0, sequence);

        return InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("cvDashboard.stopTelemetryGrid", CanvasId);
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
