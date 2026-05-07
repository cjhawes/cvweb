using CvWeb.Client.Services;

namespace CvWeb.Client.Tests;

public sealed class AlarmTriageEngineTests
{
    [Fact]
    public void ProcessRawTelemetry_EmitsGroupedAlert_WhenDebounceWindowClosesBurst()
    {
        var engine = new AlarmTriageEngine(new AlarmTriageOptions
        {
            DebounceWindow = TimeSpan.FromMilliseconds(600),
            ClusterCooldown = TimeSpan.FromSeconds(20),
            RateLimitPerSecond = 20,
            RateLimitBurst = 20
        });

        var baseline = DateTimeOffset.Parse("2026-05-07T11:00:00Z");

        _ = engine.ProcessRawTelemetry(CreatePayload("edge-a", 89, 76, 1.92, 44, 2), baseline);
        _ = engine.ProcessRawTelemetry(CreatePayload("edge-a", 91, 77, 1.97, 45, 2), baseline.AddMilliseconds(100));

        var result = engine.ProcessRawTelemetry(
            CreatePayload("edge-a", 90, 75, 1.90, 45, 2),
            baseline.AddMilliseconds(900));

        var alert = Assert.Single(result.EmittedAlerts);
        Assert.Equal(2, alert.GroupedCount);
        Assert.Equal("edge-a", alert.Node);
        Assert.Contains("grouped x2", alert.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProcessRawTelemetry_SuppressesWhenRateLimitTokensAreExhausted()
    {
        var engine = new AlarmTriageEngine(new AlarmTriageOptions
        {
            DebounceWindow = TimeSpan.Zero,
            ClusterCooldown = TimeSpan.Zero,
            RateLimitPerSecond = 1,
            RateLimitBurst = 1
        });

        var baseline = DateTimeOffset.Parse("2026-05-07T11:30:00Z");

        var first = engine.ProcessRawTelemetry(CreatePayload("edge-a", 93, 81, 2.1, 48, 2), baseline);
        var second = engine.ProcessRawTelemetry(CreatePayload("edge-b", 94, 82, 2.0, 49, 2), baseline.AddMilliseconds(10));

        Assert.Single(first.EmittedAlerts);
        Assert.Empty(second.EmittedAlerts);
        Assert.True(second.SuppressedEvents > 0);
    }

    [Fact]
    public void ProcessRawTelemetry_IgnoresMalformedJson()
    {
        var engine = new AlarmTriageEngine();

        var result = engine.ProcessRawTelemetry("{not-json", DateTimeOffset.UtcNow);

        Assert.Empty(result.EmittedAlerts);
        Assert.Equal(0, result.PublishedAlerts);
    }

    private static string CreatePayload(string node, double cpu, double memory, double packetLoss, int activeStreams, int alertLevel)
    {
        return $$"""
        {
          "node": "{{node}}",
          "timestamp": "2026-05-07T11:00:00Z",
          "cpuLoadPercent": {{cpu}},
          "memoryLoadPercent": {{memory}},
          "packetLossPercent": {{packetLoss}},
          "activeStreams": {{activeStreams}},
          "alertLevel": {{alertLevel}}
        }
        """;
    }
}
