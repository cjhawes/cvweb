using CvWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CvWeb.Client.Components.Widgets;

/// <summary>
/// Renders GPU-based 4K alignment metrics and mismatch heatmap status.
/// </summary>
public sealed partial class GpuAlignmentChecker
{
    private readonly string CanvasId = $"gpu-alignment-{Guid.NewGuid():N}";
    private DotNetObjectReference<GpuAlignmentChecker>? _dotNetReference;

    private int ChangedBytes { get; set; }
    private int ComparedBytes { get; set; }
    private int MismatchedPixels { get; set; }
    private double DriftPercent { get; set; }
    private double ElapsedMilliseconds { get; set; }
    private string BackendLabel { get; set; } = "initializing";
    private string ResolutionLabel { get; set; } = "loading...";
    private string StatusPhase { get; set; } = "initializing";
    private string StatusMessage { get; set; } = "Preparing GPU pipeline...";

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    private string StatusClass => StatusPhase switch
    {
        "ready" => "status-ok",
        "error" => "status-critical",
        _ => "status-warning"
    };

    private string StatusLabel => $"{StatusMessage} ({BackendLabel})";

    private string DriftClass => GpuAlignmentMetrics.GetSeverityClass(DriftPercent);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _dotNetReference = DotNetObjectReference.Create(this);

        try
        {
            await JS.InvokeVoidAsync(
                "cvDashboard.startGpuAlignmentChecker",
                CanvasId,
                _dotNetReference,
                "images/gpu-reference-a.svg",
                "images/gpu-reference-b.svg");
        }
        catch (JSException)
        {
            await UpdateGpuAlignmentFailure("GPU runtime is not available in this browser.");
        }
    }

    /// <summary>
    /// Applies GPU comparison metrics pushed from JavaScript interop.
    /// </summary>
    /// <param name="changedBytes">The number of changed bytes.</param>
    /// <param name="comparedBytes">The total compared bytes.</param>
    /// <param name="mismatchedPixels">The number of mismatched pixels.</param>
    /// <param name="elapsedMilliseconds">The elapsed compare time in milliseconds.</param>
    /// <param name="backend">The active backend name.</param>
    /// <param name="textureWidth">The compared texture width.</param>
    /// <param name="textureHeight">The compared texture height.</param>
    /// <returns>A task that completes after state has been updated.</returns>
    [JSInvokable]
    public Task UpdateGpuAlignmentResult(
        int changedBytes,
        int comparedBytes,
        int mismatchedPixels,
        double elapsedMilliseconds,
        string backend,
        int textureWidth,
        int textureHeight)
    {
        ChangedBytes = Math.Max(0, changedBytes);
        ComparedBytes = Math.Max(0, comparedBytes);
        MismatchedPixels = Math.Max(0, mismatchedPixels);
        DriftPercent = GpuAlignmentMetrics.CalculateDriftPercent(ChangedBytes, ComparedBytes);
        ElapsedMilliseconds = Math.Max(0, elapsedMilliseconds);

        BackendLabel = string.IsNullOrWhiteSpace(backend) ? "webgl2" : backend.Trim();
        ResolutionLabel = $"{Math.Max(1, textureWidth)}x{Math.Max(1, textureHeight)}";
        StatusPhase = "ready";
        StatusMessage = $"Byte compare complete: {GpuAlignmentMetrics.GetHealthState(DriftPercent)}";

        return InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Applies an interop failure state for GPU alignment initialization or execution.
    /// </summary>
    /// <param name="reason">The failure reason supplied by JavaScript interop.</param>
    /// <returns>A task that completes after state has been updated.</returns>
    [JSInvokable]
    public Task UpdateGpuAlignmentFailure(string reason)
    {
        BackendLabel = "unavailable";
        StatusPhase = "error";
        StatusMessage = string.IsNullOrWhiteSpace(reason) ? "GPU comparison failed." : reason;
        return InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Stops GPU interop resources and releases object references.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("cvDashboard.stopGpuAlignmentChecker", CanvasId);
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
