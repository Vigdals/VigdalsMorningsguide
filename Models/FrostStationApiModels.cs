using System.Text.Json.Serialization;

namespace VigdalsMorningsguide.Models;

public sealed class FrostTimeSeriesResponse
{
    [JsonPropertyName("data")]
    public List<FrostTimeSeriesModel> Data { get; init; } = [];
}

public sealed class FrostTimeSeriesModel
{
    [JsonPropertyName("sourceId")]
    public string SourceId { get; init; } =
        string.Empty;

    [JsonPropertyName("elementId")]
    public string ElementId { get; init; } =
        string.Empty;

    [JsonPropertyName("timeOffset")]
    public string TimeOffset { get; init; } =
        string.Empty;

    [JsonPropertyName("timeResolution")]
    public string TimeResolution { get; init; } =
        string.Empty;

    [JsonPropertyName("timeSeriesId")]
    public int TimeSeriesId { get; init; }

    [JsonPropertyName("validFrom")]
    public DateTimeOffset ValidFrom { get; init; }

    [JsonPropertyName("validTo")]
    public DateTimeOffset? ValidTo { get; init; }

    [JsonPropertyName("level")]
    public FrostLevelModel? Level { get; init; }
}

public sealed class FrostLevelModel
{
    [JsonPropertyName("levelType")]
    public string? LevelType { get; init; }

    [JsonPropertyName("unit")]
    public string? Unit { get; init; }

    [JsonPropertyName("value")]
    public double? Value { get; init; }
}