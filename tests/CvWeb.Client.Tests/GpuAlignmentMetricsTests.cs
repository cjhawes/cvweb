using CvWeb.Client.Services;

namespace CvWeb.Client.Tests;

public sealed class GpuAlignmentMetricsTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(5, 0, 0)]
    [InlineData(-1, 100, 0)]
    [InlineData(50, 200, 25)]
    [InlineData(300, 200, 100)]
    public void CalculateDriftPercent_ReturnsExpectedPercent(int changedBytes, int comparedBytes, double expectedPercent)
    {
        var driftPercent = GpuAlignmentMetrics.CalculateDriftPercent(changedBytes, comparedBytes);

        Assert.Equal(expectedPercent, driftPercent, 3);
    }

    [Theory]
    [InlineData(0.2, "metric-chip-ok", "nominal")]
    [InlineData(3.0, "metric-chip-warning", "degraded")]
    [InlineData(8.0, "metric-chip-critical", "critical")]
    public void SeverityMappings_FollowThresholds(double driftPercent, string expectedCssClass, string expectedHealthState)
    {
        Assert.Equal(expectedCssClass, GpuAlignmentMetrics.GetSeverityClass(driftPercent));
        Assert.Equal(expectedHealthState, GpuAlignmentMetrics.GetHealthState(driftPercent));
    }
}
