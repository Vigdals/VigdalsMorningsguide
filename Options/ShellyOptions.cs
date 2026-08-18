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

    public int CacheSeconds { get; init; } =
        30;

    public int StaleAfterMinutes { get; init; } =
        30;
}