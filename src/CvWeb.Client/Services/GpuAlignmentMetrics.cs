namespace CvWeb.Client.Services;

/// <summary>
/// Provides deterministic threshold and severity helpers for GPU alignment results.
/// </summary>
public static class GpuAlignmentMetrics
{
    /// <summary>
    /// Calculates drift as a percentage of changed bytes over compared bytes.
    /// </summary>
    /// <param name="changedBytes">The number of changed bytes.</param>
    /// <param name="comparedBytes">The number of compared bytes.</param>
    /// <returns>A value from 0 to 100 representing drift percentage.</returns>
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

    /// <summary>
    /// Maps drift percentage to a metric chip severity CSS class.
    /// </summary>
    /// <param name="driftPercent">The drift percentage to classify.</param>
    /// <returns>The CSS class representing severity.</returns>
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

    /// <summary>
    /// Maps drift percentage to a textual health state.
    /// </summary>
    /// <param name="driftPercent">The drift percentage to classify.</param>
    /// <returns>The health state label.</returns>
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
