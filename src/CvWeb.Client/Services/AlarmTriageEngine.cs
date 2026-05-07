using System.Globalization;
using System.Text.Json;

namespace CvWeb.Client.Services;

/// <summary>
/// Configures local alarm triage scoring, debounce, and rate-limiting behavior.
/// </summary>
public sealed record AlarmTriageOptions
{
    /// <summary>
    /// Gets the number of baseline samples used for anomaly normalization.
    /// </summary>
    public int BaselineWindowSize { get; init; } = 24;

    /// <summary>
    /// Gets the token refill rate for global alert emission.
    /// </summary>
    public double RateLimitPerSecond { get; init; } = 2.0;

    /// <summary>
    /// Gets the maximum burst capacity for alert emission.
    /// </summary>
    public int RateLimitBurst { get; init; } = 4;

    /// <summary>
    /// Gets the maximum number of clusters retained before stale pruning.
    /// </summary>
    public int MaxClusterCount { get; init; } = 128;

    /// <summary>
    /// Gets the trailing debounce window used to group bursty events.
    /// </summary>
    public TimeSpan DebounceWindow { get; init; } = TimeSpan.FromMilliseconds(1200);

    /// <summary>
    /// Gets the cooldown used to emit sustained bursts periodically.
    /// </summary>
    public TimeSpan ClusterCooldown { get; init; } = TimeSpan.FromMilliseconds(2000);
}

/// <summary>
/// Represents normalized priority levels produced by triage scoring.
/// </summary>
public enum AlertPriority
{
    /// <summary>
    /// No alert should be emitted.
    /// </summary>
    None = 0,

    /// <summary>
    /// Low-priority alert.
    /// </summary>
    P3 = 1,

    /// <summary>
    /// Medium-priority alert.
    /// </summary>
    P2 = 2,

    /// <summary>
    /// High-priority alert.
    /// </summary>
    P1 = 3
}

/// <summary>
/// Represents an emitted, grouped priority alert.
/// </summary>
/// <param name="FirstSeen">The first timestamp in the grouped burst.</param>
/// <param name="LastSeen">The latest timestamp in the grouped burst.</param>
/// <param name="Priority">The normalized priority label.</param>
/// <param name="PriorityClass">The CSS class representing severity.</param>
/// <param name="Node">The source node identifier.</param>
/// <param name="Category">The derived anomaly category.</param>
/// <param name="Summary">A short human-readable summary.</param>
/// <param name="GroupedCount">The number of merged events in this alert.</param>
/// <param name="Score">The highest score in the grouped burst.</param>
public sealed record PriorityAlert(
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    string Priority,
    string PriorityClass,
    string Node,
    string Category,
    string Summary,
    int GroupedCount,
    double Score);

/// <summary>
/// Represents the latest triage engine processing result.
/// </summary>
/// <param name="LatestScore">The latest computed risk score.</param>
/// <param name="BufferedClusters">The count of clusters waiting for emission.</param>
/// <param name="SuppressedEvents">The number of events suppressed by rate limiting.</param>
/// <param name="PublishedAlerts">The cumulative count of published alerts.</param>
/// <param name="EmittedAlerts">The alerts emitted during this processing call.</param>
public sealed record AlarmTriageResult(
    double LatestScore,
    int BufferedClusters,
    int SuppressedEvents,
    int PublishedAlerts,
    IReadOnlyList<PriorityAlert> EmittedAlerts);

/// <summary>
/// Performs deterministic browser-local telemetry triage and alert grouping.
/// </summary>
public sealed class AlarmTriageEngine
{
    private const double Epsilon = 0.0001;

    private readonly AlarmTriageOptions _options;
    private readonly Dictionary<string, ClusterState> _clusters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _lastPacketLossByNode = new(StringComparer.OrdinalIgnoreCase);
    private readonly double[] _baselineScores;

    private DateTimeOffset _lastTokenRefillAt = DateTimeOffset.MinValue;
    private double _tokens;

    private int _baselineCount;
    private int _baselineWriteIndex;
    private int _suppressedEvents;
    private int _publishedAlerts;
    private int _eventCounter;
    private double _latestScore;

    /// <summary>
    /// Initializes a new instance of the <see cref="AlarmTriageEngine"/> class.
    /// </summary>
    /// <param name="options">Optional overrides for triage behavior.</param>
    public AlarmTriageEngine(AlarmTriageOptions? options = null)
    {
        _options = options ?? new AlarmTriageOptions();

        if (_options.BaselineWindowSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "BaselineWindowSize must be greater than zero.");
        }

        if (_options.RateLimitPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "RateLimitPerSecond must be greater than zero.");
        }

        if (_options.RateLimitBurst <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "RateLimitBurst must be greater than zero.");
        }

        _baselineScores = new double[_options.BaselineWindowSize];
        _tokens = _options.RateLimitBurst;
    }

    /// <summary>
    /// Processes one telemetry payload and returns grouped alert outputs.
    /// </summary>
    /// <param name="payloadJson">The raw telemetry payload JSON.</param>
    /// <param name="observedAt">The local receive time for the payload.</param>
    /// <returns>The triage processing result for this payload.</returns>
    public AlarmTriageResult ProcessRawTelemetry(string payloadJson, DateTimeOffset observedAt)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return BuildResult(Array.Empty<PriorityAlert>());
        }

        if (!TryParseFeature(payloadJson, observedAt, out var feature))
        {
            return BuildResult(Array.Empty<PriorityAlert>());
        }

        var score = ComputeScore(feature);
        _latestScore = score;

        var priority = ClassifyPriority(feature, score);
        if (priority == AlertPriority.None)
        {
            PruneIdleClusters(observedAt);
            return BuildResult(Array.Empty<PriorityAlert>());
        }

        var category = ClassifyCategory(feature);
        var trend = ClassifyTrend(feature.PacketLossDelta);
        var clusterKey = $"{feature.Node}|{category}|{trend}";

        if (!_clusters.TryGetValue(clusterKey, out var state))
        {
            state = new ClusterState(feature.Node, category, trend);
            _clusters[clusterKey] = state;
        }

        var emitted = new List<PriorityAlert>(capacity: 2);

        if (state.PendingCount > 0 && observedAt - state.LastEventAt >= _options.DebounceWindow)
        {
            _ = TryEmitCluster(state, observedAt, emitted);
        }

        state.Add(feature, score, priority, observedAt);

        var isEscalation = state.LastEmittedPriority != AlertPriority.None && priority > state.LastEmittedPriority;
        var sustainedBurstDue = state.PendingCount > 0 && observedAt - state.FirstPendingAt >= _options.ClusterCooldown;

        if (isEscalation || sustainedBurstDue)
        {
            _ = TryEmitCluster(state, observedAt, emitted);
        }

        _eventCounter += 1;
        if ((_eventCounter & 15) == 0)
        {
            PruneIdleClusters(observedAt);
        }

        return BuildResult(emitted);
    }

    private AlarmTriageResult BuildResult(IReadOnlyList<PriorityAlert> emitted)
    {
        var bufferedClusters = 0;
        foreach (var cluster in _clusters.Values)
        {
            if (cluster.PendingCount > 0)
            {
                bufferedClusters += 1;
            }
        }

        return new AlarmTriageResult(
            LatestScore: _latestScore,
            BufferedClusters: bufferedClusters,
            SuppressedEvents: _suppressedEvents,
            PublishedAlerts: _publishedAlerts,
            EmittedAlerts: emitted);
    }

    private bool TryEmitCluster(ClusterState state, DateTimeOffset observedAt, List<PriorityAlert> emitted)
    {
        if (state.PendingCount == 0)
        {
            return false;
        }

        if (!TryConsumeToken(observedAt))
        {
            if (state.SuppressionOpen)
            {
                _suppressedEvents += 1;
            }
            else
            {
                _suppressedEvents += state.PendingCount;
                state.SuppressionOpen = true;
            }

            return false;
        }

        state.SuppressionOpen = false;

        var priority = ToPriorityLabel(state.PendingPriority);
        var priorityClass = ToPriorityClass(state.PendingPriority);

        emitted.Add(new PriorityAlert(
            FirstSeen: state.FirstPendingAt,
            LastSeen: state.LastEventAt,
            Priority: priority,
            PriorityClass: priorityClass,
            Node: state.Node,
            Category: state.Category,
            Summary: BuildSummary(state),
            GroupedCount: state.PendingCount,
            Score: state.PendingMaxScore));

        _publishedAlerts += 1;

        state.LastEmittedAt = observedAt;
        state.LastEmittedPriority = state.PendingPriority;
        state.ResetPending();

        return true;
    }

    private bool TryConsumeToken(DateTimeOffset now)
    {
        if (_lastTokenRefillAt == DateTimeOffset.MinValue)
        {
            _lastTokenRefillAt = now;
        }

        var elapsedSeconds = (now - _lastTokenRefillAt).TotalSeconds;
        if (elapsedSeconds > 0)
        {
            _tokens = Math.Min(
                _options.RateLimitBurst,
                _tokens + elapsedSeconds * _options.RateLimitPerSecond);
            _lastTokenRefillAt = now;
        }

        if (_tokens < 1)
        {
            return false;
        }

        _tokens -= 1;
        return true;
    }

    private void PruneIdleClusters(DateTimeOffset now)
    {
        if (_clusters.Count <= _options.MaxClusterCount)
        {
            return;
        }

        var idleCutoff = now - TimeSpan.FromSeconds(30);
        var stale = _clusters
            .Where(pair => pair.Value.LastEventAt < idleCutoff)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var key in stale)
        {
            _clusters.Remove(key);
        }
    }

    private double ComputeScore(TelemetryFeature feature)
    {
        var cpu = Clamp01(feature.CpuLoadPercent / 100d);
        var memory = Clamp01(feature.MemoryLoadPercent / 100d);
        var packetLoss = Clamp01(feature.PacketLossPercent / 2.5d);
        var streams = Clamp01(feature.ActiveStreams / 50d);
        var delta = Clamp01(Math.Max(0, feature.PacketLossDelta) / 1.0d);

        var raw = (cpu * 0.30 + memory * 0.20 + packetLoss * 0.35 + streams * 0.10 + delta * 0.05) * 100d;

        AddToBaseline(raw);
        var (mean, stdDev) = CalculateBaseline();

        var z = Math.Max(0, (raw - mean) / (stdDev + Epsilon));
        var anomalyBoost = Math.Min(16, z * 8);

        return Clamp(raw + anomalyBoost, 0, 100);
    }

    private void AddToBaseline(double score)
    {
        _baselineScores[_baselineWriteIndex] = score;
        _baselineWriteIndex = (_baselineWriteIndex + 1) % _baselineScores.Length;

        if (_baselineCount < _baselineScores.Length)
        {
            _baselineCount += 1;
        }
    }

    private (double Mean, double StdDev) CalculateBaseline()
    {
        if (_baselineCount == 0)
        {
            return (0, 1);
        }

        var samples = _baselineScores.AsSpan(0, _baselineCount);

        var sum = 0d;
        foreach (var sample in samples)
        {
            sum += sample;
        }

        var mean = sum / _baselineCount;

        var variance = 0d;
        foreach (var sample in samples)
        {
            var delta = sample - mean;
            variance += delta * delta;
        }

        variance /= _baselineCount;
        return (mean, Math.Sqrt(variance));
    }

    private static AlertPriority ClassifyPriority(TelemetryFeature feature, double score)
    {
        var hardP1 = feature.PacketLossPercent >= 1.7 || feature.CpuLoadPercent >= 95 || feature.AlertLevel >= 2;
        if (hardP1 || score >= 85)
        {
            return AlertPriority.P1;
        }

        if (score >= 72 || (feature.AlertLevel >= 1 && score >= 65))
        {
            return AlertPriority.P2;
        }

        if (score >= 58)
        {
            return AlertPriority.P3;
        }

        return AlertPriority.None;
    }

    private static string ClassifyCategory(TelemetryFeature feature)
    {
        if (feature.PacketLossDelta >= 0.22 || feature.PacketLossPercent >= 1.25)
        {
            return "packet-loss-burst";
        }

        if (feature.CpuLoadPercent >= 88)
        {
            return "cpu-saturation";
        }

        if (feature.MemoryLoadPercent >= 90)
        {
            return "memory-pressure";
        }

        if (feature.ActiveStreams >= 40)
        {
            return "stream-contention";
        }

        return "composite-anomaly";
    }

    private static string ClassifyTrend(double packetLossDelta)
    {
        if (packetLossDelta > 0.14)
        {
            return "rising";
        }

        if (packetLossDelta < -0.14)
        {
            return "recovering";
        }

        return "steady";
    }

    private static string BuildSummary(ClusterState state)
    {
        return $"{state.Node} {state.Category} ({state.Trend}) | CPU {state.PendingCpu:0.0}% | Loss {state.PendingPacketLoss:0.00}% | grouped x{state.PendingCount}";
    }

    private static string ToPriorityLabel(AlertPriority priority)
    {
        return priority switch
        {
            AlertPriority.P1 => "P1",
            AlertPriority.P2 => "P2",
            AlertPriority.P3 => "P3",
            _ => "P0"
        };
    }

    private static string ToPriorityClass(AlertPriority priority)
    {
        return priority switch
        {
            AlertPriority.P1 => "alert-critical",
            AlertPriority.P2 => "alert-warning",
            _ => "alert-info"
        };
    }

    private bool TryParseFeature(string payloadJson, DateTimeOffset observedAt, out TelemetryFeature feature)
    {
        feature = default;

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;

            if (!TryGetString(root, "node", out var node) || string.IsNullOrWhiteSpace(node))
            {
                return false;
            }

            _ = TryGetDouble(root, "cpuLoadPercent", out var cpu);
            _ = TryGetDouble(root, "memoryLoadPercent", out var memory);
            _ = TryGetDouble(root, "packetLossPercent", out var packetLoss);
            _ = TryGetInt32(root, "activeStreams", out var activeStreams);
            _ = TryGetInt32(root, "alertLevel", out var alertLevel);
            _ = TryGetString(root, "timestamp", out var isoTimestamp);

            var timestamp = observedAt;
            if (!string.IsNullOrWhiteSpace(isoTimestamp) && DateTimeOffset.TryParse(isoTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedTimestamp))
            {
                timestamp = parsedTimestamp;
            }

            if (!_lastPacketLossByNode.TryGetValue(node, out var previousPacketLoss))
            {
                previousPacketLoss = packetLoss;
            }

            var packetLossDelta = packetLoss - previousPacketLoss;
            _lastPacketLossByNode[node] = packetLoss;

            feature = new TelemetryFeature(
                Node: node,
                Timestamp: timestamp,
                CpuLoadPercent: Clamp(cpu, 0, 100),
                MemoryLoadPercent: Clamp(memory, 0, 100),
                PacketLossPercent: Clamp(packetLoss, 0, 5),
                PacketLossDelta: Clamp(packetLossDelta, -5, 5),
                ActiveStreams: Math.Max(0, activeStreams),
                AlertLevel: Math.Clamp(alertLevel, 0, 3));

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetDouble(JsonElement root, string propertyName, out double value)
    {
        value = 0;

        if (!root.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number)
        {
            return property.TryGetDouble(out value);
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        return false;
    }

    private static bool TryGetInt32(JsonElement root, string propertyName, out int value)
    {
        value = 0;

        if (!root.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number)
        {
            return property.TryGetInt32(out value);
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        return false;
    }

    private static bool TryGetString(JsonElement root, string propertyName, out string? value)
    {
        value = null;

        if (!root.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }

    private static double Clamp01(double value)
    {
        return Clamp(value, 0, 1);
    }

    private readonly record struct TelemetryFeature(
        string Node,
        DateTimeOffset Timestamp,
        double CpuLoadPercent,
        double MemoryLoadPercent,
        double PacketLossPercent,
        double PacketLossDelta,
        int ActiveStreams,
        int AlertLevel);

    private sealed class ClusterState
    {
        internal ClusterState(string node, string category, string trend)
        {
            Node = node;
            Category = category;
            Trend = trend;
        }

        internal string Node { get; }

        internal string Category { get; }

        internal string Trend { get; }

        internal DateTimeOffset FirstPendingAt { get; private set; }

        internal DateTimeOffset LastEventAt { get; private set; }

        internal DateTimeOffset LastEmittedAt { get; set; }

        internal int PendingCount { get; private set; }

        internal double PendingMaxScore { get; private set; }

        internal double PendingCpu { get; private set; }

        internal double PendingPacketLoss { get; private set; }

        internal AlertPriority PendingPriority { get; private set; }

        internal AlertPriority LastEmittedPriority { get; set; }

        internal bool SuppressionOpen { get; set; }

        internal void Add(TelemetryFeature feature, double score, AlertPriority priority, DateTimeOffset observedAt)
        {
            if (PendingCount == 0)
            {
                FirstPendingAt = observedAt;
                PendingMaxScore = score;
                PendingPriority = priority;
                PendingCpu = feature.CpuLoadPercent;
                PendingPacketLoss = feature.PacketLossPercent;
            }
            else
            {
                if (score > PendingMaxScore)
                {
                    PendingMaxScore = score;
                    PendingCpu = feature.CpuLoadPercent;
                    PendingPacketLoss = feature.PacketLossPercent;
                }

                if (priority > PendingPriority)
                {
                    PendingPriority = priority;
                }
            }

            PendingCount += 1;
            LastEventAt = observedAt;
        }

        internal void ResetPending()
        {
            PendingCount = 0;
            PendingMaxScore = 0;
            PendingPriority = AlertPriority.None;
            PendingCpu = 0;
            PendingPacketLoss = 0;
        }
    }
}
