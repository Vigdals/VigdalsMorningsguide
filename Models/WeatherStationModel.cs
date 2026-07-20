namespace VigdalsMorningsguide.Models;

public sealed class WeatherStationModel
{
    public required string SourceId { get; init; }

    public required string Name { get; init; }

    public double Latitude { get; init; }

    public double Longitude { get; init; }

    public double DistanceKilometres { get; init; }

    public double? MetresAboveSeaLevel { get; init; }

    public string ElementId { get; init; } =
        "air_temperature";

    public string TimeOffset { get; init; } =
        "PT0H";

    public string TimeResolution { get; init; } =
        "PT10M";

    public int TimeSeriesId { get; init; }

    public double? Level { get; init; }

    public DateTimeOffset ValidFrom { get; init; }

    public DateTimeOffset? ValidTo { get; init; }
}