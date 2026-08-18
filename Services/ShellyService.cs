using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using VigdalsMorningsguide.Models;
using VigdalsMorningsguide.Options;

namespace VigdalsMorningsguide.Services;

public sealed class ShellyService
{
    private static readonly TimeZoneInfo NorwegianTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById(
            "Europe/Oslo");

    private static readonly SemaphoreSlim RequestLock =
        new(
            1,
            1);

    private readonly HttpClient _httpClient;
    private readonly ShellyOptions _options;
    private readonly ILogger<ShellyService> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly JsonSerializerOptions _jsonOptions;

    public ShellyService(
        HttpClient httpClient,
        IOptions<ShellyOptions> options,
        ILogger<ShellyService> logger,
        IMemoryCache memoryCache)
    {
        _httpClient =
            httpClient;

        _options =
            options.Value;

        _logger =
            logger;

        _memoryCache =
            memoryCache;

        _jsonOptions =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
    }

    public async Task<ShellyMeasurementModel?>
        GetCurrentMeasurementAsync(
            CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();

        var cacheKey =
            $"ShellyMeasurement:{_options.DeviceId}";

        if (_memoryCache.TryGetValue<ShellyMeasurementModel>(
                cacheKey,
                out var cachedMeasurement) &&
            cachedMeasurement is not null)
        {
            return cachedMeasurement;
        }

        await RequestLock.WaitAsync(
            cancellationToken);

        try
        {
            /*
             * Ein annan request kan ha fylt cachen
             * medan me venta på låsen.
             */
            if (_memoryCache.TryGetValue<ShellyMeasurementModel>(
                    cacheKey,
                    out cachedMeasurement) &&
                cachedMeasurement is not null)
            {
                return cachedMeasurement;
            }

            var measurement =
                await FetchMeasurementAsync(
                    cancellationToken);

            if (measurement is not null)
            {
                _memoryCache.Set(
                    cacheKey,
                    measurement,
                    TimeSpan.FromSeconds(
                        _options.CacheSeconds));
            }

            return measurement;
        }
        finally
        {
            RequestLock.Release();
        }
    }

    private async Task<ShellyMeasurementModel?>
        FetchMeasurementAsync(
            CancellationToken cancellationToken)
    {
        var requestBody =
            new ShellyGetDevicesRequest
            {
                Ids =
                [
                    _options.DeviceId
                ],

                Select =
                [
                    "status"
                ],

                Pick =
                    new ShellyGetDevicesPick
                    {
                        Status =
                        [
                            "temperature:0",
                            "humidity:0",
                            "devicepower:0",
                            "sys",
                            "ts"
                        ]
                    }
            };

        var requestUri =
            BuildRequestUri();

        _logger.LogInformation(
            "Hentar siste Shelly-måling frå Shelly Cloud.");

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                requestUri)
            {
                Content =
                    JsonContent.Create(
                        requestBody)
            };

        using var response =
            await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        var json =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Shelly Cloud returnerte HTTP {StatusCode}.",
                (int)response.StatusCode);

            throw new HttpRequestException(
                $"Shelly Cloud returnerte HTTP " +
                $"{(int)response.StatusCode}.",
                inner: null,
                response.StatusCode);
        }

        var devices =
            JsonSerializer.Deserialize<
                List<ShellyCloudDeviceModel>>(
                json,
                _jsonOptions)
            ?? throw new JsonException(
                "Shelly Cloud returnerte eit tomt " +
                "eller ugyldig JSON-svar.");

        var device =
            devices.FirstOrDefault(device =>
                string.Equals(
                    device.Id,
                    _options.DeviceId,
                    StringComparison.OrdinalIgnoreCase));

        if (device is null)
        {
            throw new JsonException(
                "Shelly Cloud returnerte ikkje " +
                "den konfigurerte eininga.");
        }

        if (device.Status is null)
        {
            throw new JsonException(
                "Shelly Cloud returnerte ingen status " +
                "for den konfigurerte eininga.");
        }

        var temperature =
            device.Status
                .Temperature?
                .TemperatureCelsius;

        var humidity =
            device.Status
                .Humidity?
                .RelativeHumidity;

        if (!temperature.HasValue &&
            !humidity.HasValue)
        {
            _logger.LogWarning(
                "Shelly-statusen inneheld verken " +
                "temperatur eller luftfukt.");

            return null;
        }

        var measuredAt =
            ConvertTimestamp(
                device.Status.Timestamp);

        var measurement =
            new ShellyMeasurementModel
            {
                TemperatureCelsius =
                    temperature,

                RelativeHumidity =
                    humidity,

                MeasuredAt =
                    measuredAt,

                ExternalPowerPresent =
                    device.Status
                        .DevicePower?
                        .External?
                        .Present,

                BatteryPercent =
                    device.Status
                        .DevicePower?
                        .Battery?
                        .Percent,

                WakeupPeriodSeconds =
                    device.Status
                        .System?
                        .WakeupPeriodSeconds,

                StaleAfterMinutes =
                    _options.StaleAfterMinutes
            };

        _logger.LogInformation(
            "Henta Shelly-måling. " +
            "Temperatur: {TemperatureCelsius}, " +
            "luftfukt: {RelativeHumidity}, " +
            "måletid: {MeasuredAt}.",
            measurement.TemperatureCelsius,
            measurement.RelativeHumidity,
            measurement.MeasuredAt);

        return measurement;
    }

    private string BuildRequestUri()
    {
        return
            "v2/devices/api/get" +
            "?auth_key=" +
            Uri.EscapeDataString(
                _options.AuthKey);
    }

    private static DateTimeOffset? ConvertTimestamp(
        double? timestamp)
    {
        if (timestamp is not double value ||
            !double.IsFinite(value) ||
            value <= 0)
        {
            return null;
        }

        var utc =
            DateTimeOffset.UnixEpoch.AddSeconds(
                value);

        return TimeZoneInfo.ConvertTime(
            utc,
            NorwegianTimeZone);
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(
                _options.AuthKey))
        {
            throw new InvalidOperationException(
                "Shelly:AuthKey manglar.");
        }

        if (string.IsNullOrWhiteSpace(
                _options.DeviceId))
        {
            throw new InvalidOperationException(
                "Shelly:DeviceId manglar.");
        }

        if (_httpClient.BaseAddress is null)
        {
            throw new InvalidOperationException(
                "BaseAddress manglar på Shelly-klienten.");
        }
    }
}