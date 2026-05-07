using System.Globalization;
using System.Text;

namespace CvWeb.Client.Services;

public enum WebRtcSeriesMetric
{
    Bitrate,
    PacketLoss,
    Jitter,
    FramesPerSecond
}

public sealed class WebRtcProbeMetricsEngine
{
    private readonly int _capacity;
    private readonly double[] _bitrateWindow;
    private readonly double[] _packetLossWindow;
    private readonly double[] _jitterWindow;
    private readonly double[] _fpsWindow;

    private int _nextIndex;
    private int _count;

    public WebRtcProbeMetricsEngine(int capacity = 120)
    {
        if (capacity < 16)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 16.");
        }

        _capacity = capacity;
        _bitrateWindow = new double[_capacity];
        _packetLossWindow = new double[_capacity];
        _jitterWindow = new double[_capacity];
        _fpsWindow = new double[_capacity];
    }

    public int Count => _count;

    public int Capacity => _capacity;

    public void Push(WebRtcProbeStatsSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        var index = _nextIndex;

        _bitrateWindow.AsSpan()[index] = Math.Max(0d, sample.BitrateKbps);
        _packetLossWindow.AsSpan()[index] = Math.Max(0d, sample.PacketLossPercent);
        _jitterWindow.AsSpan()[index] = Math.Max(0d, sample.JitterMs);
        _fpsWindow.AsSpan()[index] = Math.Max(0d, sample.FramesPerSecond);

        _nextIndex = (_nextIndex + 1) % _capacity;
        if (_count < _capacity)
        {
            _count += 1;
        }
    }

    public double ComputeHealthScore(WebRtcProbeStatsSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        var bitrateScore = Math.Min(100d, (Math.Max(0d, sample.BitrateKbps) / 2800d) * 100d);
        var lossPenalty = Math.Min(45d, Math.Max(0d, sample.PacketLossPercent) * 22d);
        var jitterPenalty = Math.Min(35d, Math.Max(0d, sample.JitterMs) * 1.15d);
        var rttPenalty = Math.Min(20d, Math.Max(0d, sample.RoundTripTimeMs) * 0.08d);

        return Math.Clamp(bitrateScore - lossPenalty - jitterPenalty - rttPenalty, 0d, 100d);
    }

    public double GetWindowMax(WebRtcSeriesMetric metric, double fallback)
    {
        var safeFallback = Math.Max(0.001d, fallback);
        if (_count == 0)
        {
            return safeFallback;
        }

        var span = GetSeries(metric);
        var start = _count == _capacity ? _nextIndex : 0;

        var max = safeFallback;
        for (var index = 0; index < _count; index += 1)
        {
            var value = span[(start + index) % _capacity];
            if (value > max)
            {
                max = value;
            }
        }

        return max;
    }

    public string BuildPolyline(WebRtcSeriesMetric metric, double width, double height, double scaleMax)
    {
        if (_count == 0)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"0,{height:0.##} {width:0.##},{height:0.##}");
        }

        var span = GetSeries(metric);
        var start = _count == _capacity ? _nextIndex : 0;
        var safeScaleMax = Math.Max(0.001d, scaleMax);
        var step = _count == 1 ? 0d : width / (_count - 1);

        var builder = new StringBuilder(_count * 14);
        for (var index = 0; index < _count; index += 1)
        {
            var value = span[(start + index) % _capacity];
            var normalized = Math.Clamp(value / safeScaleMax, 0d, 1d);
            var x = index * step;
            var y = height - (normalized * height);

            if (index > 0)
            {
                builder.Append(' ');
            }

            builder.Append(x.ToString("0.##", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(y.ToString("0.##", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private ReadOnlySpan<double> GetSeries(WebRtcSeriesMetric metric)
    {
        return metric switch
        {
            WebRtcSeriesMetric.Bitrate => _bitrateWindow,
            WebRtcSeriesMetric.PacketLoss => _packetLossWindow,
            WebRtcSeriesMetric.Jitter => _jitterWindow,
            WebRtcSeriesMetric.FramesPerSecond => _fpsWindow,
            _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unsupported series metric.")
        };
    }
}
