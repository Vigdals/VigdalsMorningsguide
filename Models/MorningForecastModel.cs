namespace VigdalsMorningsguide.Models;

public sealed class MorningForecastModel
{
    public DateTimeOffset ProjectionFrom { get; init; }

    public DateTimeOffset ProjectionTo { get; init; }

    public DateTimeOffset? EstimatedTargetAt { get; init; }

    public double StartingDegreeDays { get; init; }

    public double ForecastDegreeDays { get; init; }

    public double TargetDegreeDays { get; init; }

    public int ForecastPointCount { get; init; }

    public double ProjectedTotalDegreeDays =>
        StartingDegreeDays +
        ForecastDegreeDays;

    public double RemainingDegreeDays =>
        Math.Max(
            0,
            TargetDegreeDays -
            ProjectedTotalDegreeDays);

    public bool TargetReachedWithinForecast =>
        EstimatedTargetAt.HasValue;

    public TimeSpan? EstimatedTimeRemaining =>
        EstimatedTargetAt.HasValue
            ? EstimatedTargetAt.Value -
              ProjectionFrom
            : null;
}