namespace VigdalsMorningsguide.Options;

public sealed class FrostOptions
{
    public const string SectionName = "Frost";

    public string BaseUrl { get; init; } =
        "https://frost.met.no/";

    public string ClientId { get; init; } =
        string.Empty;

    public string SourceId { get; init; } =
        string.Empty;

    public string SourceName { get; init; } =
        string.Empty;

    public string ElementId { get; init; } =
        "air_temperature";

    public string TimeOffset { get; init; } =
        "PT0H";

    public string TimeResolution { get; init; } =
        "PT10M";

    public int TimeSeriesId { get; init; }

    public double Level { get; init; } = 2.0;

    public double TargetDegreeDays { get; init; } = 80.0;

    public double MinimumCoveragePercent { get; init; } = 90.0;

    public int MeasurementIntervalMinutes { get; init; } = 10;

    public int MaximumAcceptedGapMinutes { get; init; } = 20;

    public int MaximumDaysBack { get; init; } = 90;
}