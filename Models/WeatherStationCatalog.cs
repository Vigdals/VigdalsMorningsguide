namespace VigdalsMorningsguide.Models;

public static class WeatherStationCatalog
{
    public const string DefaultSourceId =
        "SN55709";

    public static IReadOnlyList<WeatherStationOptionModel> Stations { get; } =
    [
        new()
        {
            SourceId = "SN55520",
            Name = "Indre Hafslo - Fv55",
            Municipality = "Luster",
            Latitude = 61.345,
            Longitude = 7.26233,
            MetresAboveSeaLevel = 249
        },
        new()
        {
            SourceId = "SN55430",
            Name = "Jostedalen – Mjølversgrendi",
            Municipality = "Luster",
            Latitude = 61.6486,
            Longitude = 7.2758,
            MetresAboveSeaLevel = 305
        },
        new()
        {
            SourceId = "SN55709",
            Name = "Loftesnes",
            Municipality = "Sogndal",
            Latitude = 61.2296003,
            Longitude = 7.1245731,
            MetresAboveSeaLevel = 4 
        }
        //,
        //new()
        //{
        //    SourceId = "SN55000",
        //    Name = "Luster – Ornes",
        //    Municipality = "Luster",
        //    Latitude = 61.299413,
        //    Longitude = 7.313199,
        //    MetresAboveSeaLevel = 4
        //}
    ];

    public static WeatherStationOptionModel? Find(
        string? sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return null;
        }

        return Stations.FirstOrDefault(
            station =>
                string.Equals(
                    station.SourceId,
                    sourceId,
                    StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class WeatherStationOptionModel
{
    public required string SourceId { get; init; }

    public required string Name { get; init; }

    public required string Municipality { get; init; }

    public double Latitude { get; init; }

    public double Longitude { get; init; }

    public double MetresAboveSeaLevel { get; init; }

    public string DisplayName =>
        $"{Name} ({Municipality}, " +
        $"{MetresAboveSeaLevel:0} moh.)";
}