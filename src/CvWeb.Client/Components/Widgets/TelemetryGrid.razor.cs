using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CvWeb.Client.Components.Widgets;

/// <summary>
/// Renders high-frequency synthetic telemetry grid statistics from JS worker sessions.
/// </summary>
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

    /// <summary>
    /// Applies telemetry grid summary statistics pushed from JavaScript.
    /// </summary>
    /// <param name="sensorCount">The number of sensors in the grid.</param>
    /// <param name="ingestRateHz">The observed ingest rate in hertz.</param>
    /// <param name="renderRateFps">The observed render frame rate.</param>
    /// <param name="droppedFrames">The count of dropped frames.</param>
    /// <param name="alertSensors">The number of sensors in alert state.</param>
    /// <param name="cpuAveragePercent">The average CPU percentage.</param>
    /// <param name="packetLossAveragePercent">The average packet-loss percentage.</param>
    /// <param name="streamState">The stream state label.</param>
    /// <param name="sequence">The latest worker sequence number.</param>
    /// <returns>A task that completes after state has been updated.</returns>
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

    /// <summary>
    /// Stops telemetry grid rendering and releases JS interop references.
    /// </summary>
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
