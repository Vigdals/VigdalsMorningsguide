namespace VigdalsMorningsguide.Models;

public sealed record TemperatureMeasurementModel(
    DateTimeOffset UtcTimestamp,
    double Temperature,
    int? QualityCode = null);

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

    public static bool IsShelly(
        string? sourceId)
    {
        return string.Equals(
            sourceId,
            ShellySourceId,
            StringComparison.OrdinalIgnoreCase);
    }
}
