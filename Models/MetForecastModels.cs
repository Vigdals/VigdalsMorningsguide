using System.Text.Json.Serialization;

namespace VigdalsMorningsguide.Models;

public sealed class MetForecastResponse
{
    [JsonPropertyName("properties")]
    public MetForecastProperties Properties { get; init; } =
        new();
}

public sealed class MetForecastProperties
{
    [JsonPropertyName("timeseries")]
    public List<MetForecastTimeSeriesPoint> TimeSeries { get; init; } =
        [];
}

public sealed class MetForecastTimeSeriesPoint
{
    [JsonPropertyName("time")]
    public DateTimeOffset Time { get; init; }

    [JsonPropertyName("data")]
    public MetForecastData Data { get; init; } =
        new();
}

public sealed class MetForecastData
{
    [JsonPropertyName("instant")]
    public MetForecastInstant Instant { get; init; } =
        new();
}

public sealed class MetForecastInstant
{
    [JsonPropertyName("details")]
    public MetForecastInstantDetails Details { get; init; } =
        new();
}

public sealed class MetForecastInstantDetails
{
    [JsonPropertyName("air_temperature")]
    public double? AirTemperature { get; init; }
}

public sealed class ForecastTemperaturePoint
{
    public DateTimeOffset Time { get; init; }

    public double Temperature { get; init; }
}