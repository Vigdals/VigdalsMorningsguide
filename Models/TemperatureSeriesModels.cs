namespace VigdalsMorningsguide.Models;

public sealed record TemperatureMeasurementModel(
    DateTimeOffset UtcTimestamp,
    double Temperature,
    int? QualityCode = null,
    bool IsEstimated = false);

public sealed class TemperatureSourceModel
{
    public required string SourceId { get; init; }

    public required string Name { get; init; }

    public double DistanceKilometres { get; init; }

    public double Latitude { get; init; }

    public double Longitude { get; init; }

    public double? MetresAboveSeaLevel { get; init; }
}

public static class TemperatureSourceCatalog
{
    public const string ShellySourceId =
        "SHELLY_LOCAL";

    public const string RefrigeratorSourceId = "REFRIGERATOR";

    /// <summary>
    /// Returnerer berre ein ID frå kjende, kontrollerte kjelder.
    /// Ukjend brukarinput skal aldri skrivast direkte til loggen.
    /// </summary>
    public static string GetSafeSourceIdForLogging(string? sourceId)
    {
        if (IsShelly(sourceId))
        {
            return ShellySourceId;
        }

        if (IsRefrigerator(sourceId))
        {
            return RefrigeratorSourceId;
        }

        return WeatherStationCatalog.Find(sourceId)?.SourceId
            ?? "UNKNOWN";
    }

    public static bool IsRefrigerator(string? sourceId)
    {
        return string.Equals(
            sourceId,
            RefrigeratorSourceId,
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsShelly(
        string? sourceId)
    {
        return string.Equals(
            sourceId,
            ShellySourceId,
            StringComparison.OrdinalIgnoreCase);
    }
}
