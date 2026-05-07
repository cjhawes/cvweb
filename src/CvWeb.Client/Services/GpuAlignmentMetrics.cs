namespace CvWeb.Client.Services;

public static class GpuAlignmentMetrics
{
    public static double CalculateDriftPercent(int changedBytes, int comparedBytes)
    {
        if (comparedBytes <= 0)
        {
            return 0;
        }

        var safeCompared = Math.Max(1, comparedBytes);
        var safeChanged = Math.Clamp(changedBytes, 0, safeCompared);
        return safeChanged * 100d / safeCompared;
    }

    public static string GetSeverityClass(double driftPercent)
    {
        if (driftPercent >= 8d)
        {
            return "metric-chip-critical";
        }

        if (driftPercent >= 3d)
        {
            return "metric-chip-warning";
        }

        return "metric-chip-ok";
    }

    public static string GetHealthState(double driftPercent)
    {
        if (driftPercent >= 8d)
        {
            return "critical";
        }

        if (driftPercent >= 3d)
        {
            return "degraded";
        }

        return "nominal";
    }
}
