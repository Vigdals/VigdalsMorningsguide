using System.Text.Json.Serialization;

namespace VigdalsMorningsguide.Models;

public sealed class ShellyGetDevicesRequest
{
    [JsonPropertyName("ids")]
    public string[] Ids { get; init; } =
        [];

    [JsonPropertyName("select")]
    public string[] Select { get; init; } =
        [];

    [JsonPropertyName("pick")]
    public ShellyGetDevicesPick Pick { get; init; } =
        new();
}

public sealed class ShellyGetDevicesPick
{
    [JsonPropertyName("status")]
    public string[] Status { get; init; } =
        [];
}

public sealed class ShellyCloudDeviceModel
{
    [JsonPropertyName("id")]
    public string Id { get; init; } =
        string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } =
        string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; init; } =
        string.Empty;

    [JsonPropertyName("gen")]
    public string Generation { get; init; } =
        string.Empty;

    [JsonPropertyName("online")]
    public int Online { get; init; }

    [JsonPropertyName("status")]
    public ShellyCloudStatusModel? Status { get; init; }
}

public sealed class ShellyCloudStatusModel
{
    [JsonPropertyName("ts")]
    public double? Timestamp { get; init; }

    [JsonPropertyName("temperature:0")]
    public ShellyTemperatureStatusModel? Temperature { get; init; }

    [JsonPropertyName("humidity:0")]
    public ShellyHumidityStatusModel? Humidity { get; init; }

    [JsonPropertyName("devicepower:0")]
    public ShellyDevicePowerStatusModel? DevicePower { get; init; }

    [JsonPropertyName("sys")]
    public ShellySystemStatusModel? System { get; init; }
}

public sealed class ShellyTemperatureStatusModel
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("tC")]
    public double? TemperatureCelsius { get; init; }

    [JsonPropertyName("tF")]
    public double? TemperatureFahrenheit { get; init; }
}

public sealed class ShellyHumidityStatusModel
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("rh")]
    public double? RelativeHumidity { get; init; }
}

public sealed class ShellyDevicePowerStatusModel
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("battery")]
    public ShellyBatteryStatusModel? Battery { get; init; }

    [JsonPropertyName("external")]
    public ShellyExternalPowerStatusModel? External { get; init; }
}

public sealed class ShellyBatteryStatusModel
{
    [JsonPropertyName("V")]
    public double? Voltage { get; init; }

    [JsonPropertyName("percent")]
    public double? Percent { get; init; }
}

public sealed class ShellyExternalPowerStatusModel
{
    [JsonPropertyName("present")]
    public bool Present { get; init; }
}

public sealed class ShellySystemStatusModel
{
    [JsonPropertyName("wakeup_period")]
    public int? WakeupPeriodSeconds { get; init; }
}

public sealed class ShellyWeatherStatisticsResponse
{
    [JsonPropertyName("timezone")]
    public string? TimeZone { get; init; }

    [JsonPropertyName("interval")]
    public string? Interval { get; init; }

    [JsonPropertyName("history_interval")]
    public string? HistoryInterval { get; init; }

    [JsonPropertyName("history")]
    public List<ShellyWeatherHistoryEntry>? History { get; init; }
}

public sealed class ShellyWeatherHistoryEntry
{
    [JsonPropertyName("datetime")]
    public string Timestamp { get; init; } =
        string.Empty;

    [JsonPropertyName("available")]
    public bool? IsAvailable { get; init; }

    [JsonPropertyName("min_temperature")]
    public double? MinimumTemperatureCelsius { get; init; }

    [JsonPropertyName("max_temperature")]
    public double? MaximumTemperatureCelsius { get; init; }

    [JsonPropertyName("avg_temperature")]
    public double? AverageTemperatureCelsius { get; init; }

    [JsonPropertyName("temperature")]
    public double? TemperatureCelsius { get; init; }

    [JsonPropertyName("humidity")]
    public double? RelativeHumidity { get; init; }

    [JsonPropertyName("missing")]
    public bool? IsMissing { get; init; }

    public double? MeanTemperatureCelsius
    {
        get
        {
            if (AverageTemperatureCelsius is double average &&
                double.IsFinite(average))
            {
                return average;
            }

            if (TemperatureCelsius is double temperature &&
                double.IsFinite(temperature))
            {
                return temperature;
            }

            if (MinimumTemperatureCelsius is double minimum &&
                MaximumTemperatureCelsius is double maximum &&
                double.IsFinite(minimum) &&
                double.IsFinite(maximum))
            {
                return (minimum + maximum) / 2.0;
            }

            return null;
        }
    }

    public bool IsTemperatureEstimated =>
        !(AverageTemperatureCelsius is double average &&
          double.IsFinite(average)) &&
        !(TemperatureCelsius is double temperature &&
          double.IsFinite(temperature)) &&
        MinimumTemperatureCelsius is double minimum &&
        MaximumTemperatureCelsius is double maximum &&
        double.IsFinite(minimum) &&
        double.IsFinite(maximum);
}

public sealed class ShellyMeasurementModel
{
    public double? TemperatureCelsius { get; init; }

    public double? RelativeHumidity { get; init; }

    public DateTimeOffset? MeasuredAt { get; init; }

    public bool? ExternalPowerPresent { get; init; }

    public double? BatteryPercent { get; init; }

    public int? WakeupPeriodSeconds { get; init; }

    public int StaleAfterMinutes { get; init; }

    public TimeSpan? MeasurementAge
    {
        get
        {
            if (!MeasuredAt.HasValue)
            {
                return null;
            }

            var age =
                DateTimeOffset.UtcNow -
                MeasuredAt.Value.ToUniversalTime();

            return age < TimeSpan.Zero
                ? TimeSpan.Zero
                : age;
        }
    }

    public bool IsStale =>
        MeasurementAge is TimeSpan age &&
        age > TimeSpan.FromMinutes(
            StaleAfterMinutes);
}
