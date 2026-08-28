namespace VigdalsMorningsguide.Options;

public sealed class ShellyOptions
{
    public const string SectionName =
        "Shelly";

    public string BaseUrl { get; init; } =
        string.Empty;

    public string DeviceId { get; init; } =
        string.Empty;

    public string AuthKey { get; init; } =
        string.Empty;

    public string DisplayName { get; init; } =
        "Skålen i Sogndal";

    public int CacheSeconds { get; init; } =
        30;

    public int StaleAfterMinutes { get; init; } =
        30;

    public double MinimumCoveragePercent { get; init; } =
        70.0;

    public int MaximumDaysBack { get; init; } =
        90;
}
