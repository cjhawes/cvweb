using CvWeb.Client.Services;

namespace CvWeb.Client.Tests;

public sealed class WebRtcProbeMetricsTests
{
    [Fact]
    public void ComputeHealthScore_DropsWhenLossAndJitterIncrease()
    {
        var engine = new WebRtcProbeMetricsEngine();

        var healthy = engine.ComputeHealthScore(new WebRtcProbeStatsSample
        {
            BitrateKbps = 2600,
            PacketLossPercent = 0.05,
            JitterMs = 2.1,
            RoundTripTimeMs = 22,
            FramesPerSecond = 30,
            ConnectionState = "connected"
        });

        var degraded = engine.ComputeHealthScore(new WebRtcProbeStatsSample
        {
            BitrateKbps = 860,
            PacketLossPercent = 2.4,
            JitterMs = 24.3,
            RoundTripTimeMs = 170,
            FramesPerSecond = 18,
            ConnectionState = "degraded"
        });

        Assert.True(healthy > degraded);
        Assert.InRange(healthy, 0, 100);
        Assert.InRange(degraded, 0, 100);
    }

    [Fact]
    public void Push_EnforcesConfiguredCapacity()
    {
        var engine = new WebRtcProbeMetricsEngine(capacity: 16);

        for (var index = 0; index < 24; index += 1)
        {
            engine.Push(new WebRtcProbeStatsSample
            {
                Sequence = index,
                BitrateKbps = 600 + (index * 32),
                PacketLossPercent = index % 3,
                JitterMs = 2 + index,
                RoundTripTimeMs = 10 + index,
                FramesPerSecond = 24 + (index % 5),
                ConnectionState = "connected"
            });
        }

        Assert.Equal(16, engine.Capacity);
        Assert.Equal(16, engine.Count);

        var bitrateMax = engine.GetWindowMax(WebRtcSeriesMetric.Bitrate, 1000);
        Assert.True(bitrateMax >= 1200);

        var points = engine.BuildPolyline(WebRtcSeriesMetric.Bitrate, 320, 92, bitrateMax);
        var pointCount = points.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.Equal(16, pointCount);
    }
}
