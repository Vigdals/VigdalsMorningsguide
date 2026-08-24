namespace VigdalsMorningsguide.Models;

public sealed class MorningResultModel
{
    public DateTime HungAt { get; init; }

    public DateTime CalculatedAt { get; init; }

    public DateTimeOffset CalculatedAtUtc { get; init; }
    public DateTime? RefrigeratedAt { get; init; }
    public double? RefrigeratorTemperatureCelsius { get; init; }
    public string SourceId { get; init; } =
        string.Empty;

    public string SourceName { get; init; } =
        string.Empty;

    public double StationDistanceKilometres { get; init; }

    public double StationLatitude { get; init; }

    public double StationLongitude { get; init; }

    public double? StationMetresAboveSeaLevel { get; init; }

    public double TargetDegreeDays { get; init; }

    public double TotalDegreeDays { get; init; }

    public double? AverageTemperature { get; init; }

    public int ObservationCount { get; init; }

    public double CoveragePercent { get; init; }

    public IReadOnlyList<MorningDayModel> Days { get; init; } =
        [];

    public TimeSpan ElapsedTime =>
        CalculatedAt > HungAt
            ? CalculatedAt - HungAt
            : TimeSpan.Zero;

    public int ElapsedWholeDays =>
        (int)ElapsedTime.TotalDays;

    public int ElapsedRemainingHours =>
        ElapsedTime.Hours;

    public int ElapsedRemainingMinutes =>
        ElapsedTime.Minutes;

    public double RemainingDegreeDays =>
        Math.Max(
            0,
            TargetDegreeDays - TotalDegreeDays);

    public double ProgressPercent =>
        TargetDegreeDays <= 0
            ? 0
            : Math.Min(
                100,
                TotalDegreeDays /
                TargetDegreeDays *
                100);

    public bool TargetReached =>
        TotalDegreeDays >= TargetDegreeDays;

    public double? EstimatedHoursRemaining =>
        !TargetReached &&
        AverageTemperature is > 0
            ? RemainingDegreeDays /
              AverageTemperature.Value *
              24
            : null;

    public int IncludedDayCount =>
        Days.Count(day =>
            day.IncludedInTotal);

    public int ExcludedDayCount =>
        Days.Count(day =>
            !day.IncludedInTotal);
}

public sealed class MorningDayModel
{
    public DateOnly Date { get; init; }

    public DateTime PeriodStart { get; init; }

    public DateTime PeriodEnd { get; init; }

    public double? MeanTemperature { get; init; }

    public int ObservationCount { get; init; }

    public int ExpectedObservationCount { get; init; }

    public double CoveragePercent { get; init; }

    public bool IncludedInTotal { get; init; }

    public bool UsesRefrigeratorTemperature { get; init; }

    public double DegreeDays { get; init; }

    public double AccumulatedDegreeDays { get; init; }

    public IReadOnlyList<int> QualityCodes { get; init; } =
        [];
}