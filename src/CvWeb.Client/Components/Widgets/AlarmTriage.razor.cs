using System.Threading.Channels;
using CvWeb.Client.Services;
using Microsoft.AspNetCore.Components;

namespace CvWeb.Client.Components.Widgets;

public partial class AlarmTriage : IAsyncDisposable
{
    private readonly List<PriorityAlert> _alerts = [];
    private readonly CancellationTokenSource _streamCts = new();
    private readonly AlarmTriageEngine _triageEngine = new();

    private Task? _streamTask;

    [Inject]
    private IMockStreamService MockStreams { get; set; } = default!;

    private IReadOnlyList<PriorityAlert> Alerts => _alerts;

    private double LatestScore { get; set; }

    private int BufferedClusters { get; set; }

    private int SuppressedEvents { get; set; }

    private int PublishedAlerts { get; set; }

    private string DebounceLabel => "1.2s trailing window";

    private string RateLimitLabel => "2 alerts/sec (burst 4)";

    private string ModelLabel => ModelState switch
    {
        "active" => "Local triage engine active",
        "degraded" => "Local triage engine degraded",
        _ => "Local triage engine warming up"
    };

    private string StatusClass => ModelState switch
    {
        "active" => "status-ok",
        "degraded" => "status-warning",
        _ => "status-critical"
    };

    private string ModelState { get; set; } = "warming up";

    protected override void OnInitialized()
    {
        var jsonReader = MockStreams.SubscribeTelemetryJson(_streamCts.Token);
        _streamTask = ConsumeTelemetryJsonAsync(jsonReader, _streamCts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _streamCts.Cancel();

        if (_streamTask is not null)
        {
            try
            {
                await _streamTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during component teardown.
            }
        }

        _streamCts.Dispose();
    }

    private async Task ConsumeTelemetryJsonAsync(ChannelReader<TelemetryJsonSample> jsonReader, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var sample in jsonReader.ReadAllAsync(cancellationToken))
            {
                var result = _triageEngine.ProcessRawTelemetry(sample.PayloadJson, sample.ReceivedAt);

                await InvokeAsync(() =>
                {
                    LatestScore = result.LatestScore;
                    BufferedClusters = result.BufferedClusters;
                    SuppressedEvents = result.SuppressedEvents;
                    PublishedAlerts = result.PublishedAlerts;
                    ModelState = "active";

                    foreach (var alert in result.EmittedAlerts)
                    {
                        _alerts.Insert(0, alert);
                        if (_alerts.Count > 8)
                        {
                            _alerts.RemoveAt(_alerts.Count - 1);
                        }
                    }

                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during component teardown.
        }
        catch
        {
            await InvokeAsync(() =>
            {
                ModelState = "degraded";
                StateHasChanged();
            });
        }
    }
}
