using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using VigdalsMorningsguide.Models;

namespace VigdalsMorningsguide.Services;

/// <summary>
/// Varslar om ferske minusgrader inne i skålen (Shelly) eller utandørs
/// frost i MET-prognosen for Loftesnes dei neste 48 timane.
/// </summary>
public sealed class FrostWarningService
{
    private static readonly TimeSpan ForecastHorizon = TimeSpan.FromHours(48);
    private static readonly TimeSpan ForecastCacheLifetime = TimeSpan.FromMinutes(30);

    private readonly MetForecastService _metForecastService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<FrostWarningService> _logger;

    public FrostWarningService(
        MetForecastService metForecastService,
        IMemoryCache cache,
        ILogger<FrostWarningService> logger)
    {
        _metForecastService = metForecastService;
        _cache = cache;
        _logger = logger;
    }

    public async Task<FrostWarningModel?> GetWarningAsync(
        string? selectedSourceId,
        ShellyMeasurementModel? measurement,
        CancellationToken cancellationToken = default)
    {
        if (TemperatureSourceCatalog.IsRefrigerator(selectedSourceId))
        {
            return null;
        }

        var usesShelly = TemperatureSourceCatalog.IsShelly(selectedSourceId);
        var station = usesShelly
            ? WeatherStationCatalog.Find(WeatherStationCatalog.DefaultSourceId)
            : WeatherStationCatalog.Find(selectedSourceId);

        if (station is null)
        {
            return null;
        }

        var forecastPoints = await GetForecastAsync(station, cancellationToken);

        return Evaluate(
            usesShelly ? measurement : null,
            forecastPoints,
            DateTimeOffset.UtcNow,
            station.Name,
            usesShelly);
    }

    private async Task<IReadOnlyList<ForecastTemperaturePoint>> GetForecastAsync(
        WeatherStationOptionModel station,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"FrostWarning:{station.SourceId}";

        if (_cache.TryGetValue(
                cacheKey,
                out IReadOnlyList<ForecastTemperaturePoint>? cached) &&
            cached is not null)
        {
            return cached;
        }

        var forecastLocation =
            new WeatherStationModel
            {
                SourceId = station.SourceId,
                Name = station.Name,
                Latitude = station.Latitude,
                Longitude = station.Longitude,
                MetresAboveSeaLevel = station.MetresAboveSeaLevel,
                DistanceKilometres = 0
            };

        try
        {
            var forecast =
                await _metForecastService.GetTemperatureForecastAsync(
                    forecastLocation,
                    cancellationToken);

            if (forecast.Count > 0)
            {
                _cache.Set(
                    cacheKey,
                    forecast,
                    ForecastCacheLifetime);
            }

            return forecast;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(
                "Klarte ikkje å hente MET-prognose for frostvarsel. HTTP: {StatusCode}.",
                exception.StatusCode);
        }
        catch (JsonException)
        {
            _logger.LogWarning(
                "MET-prognosen for frostvarsel kunne ikkje tolkast.");
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Tidsavbrot ved MET-prognose for frostvarsel.");
        }

        // MET-feil skal ikkje skjule eit gyldig frostvarsel frå Shelly.
        return [];
    }

    public static FrostWarningModel? Evaluate(
        ShellyMeasurementModel? measurement,
        IReadOnlyList<ForecastTemperaturePoint> forecastPoints,
        DateTimeOffset now,
        string? forecastLocationName = null,
        bool forecastIsNearbyProxy = false)
    {
        ArgumentNullException.ThrowIfNull(forecastPoints);

        var nowUtc = now.ToUniversalTime();
        double? currentTemperature = null;

        // Målinga må vere fersk. Toler to minutt klokkeskilnad.
        if (measurement?.MeasuredAt is DateTimeOffset measuredAt &&
            measurement.TemperatureCelsius is double temperature &&
            double.IsFinite(temperature) &&
            temperature < 0 &&
            measurement.StaleAfterMinutes > 0)
        {
            var age = nowUtc - measuredAt.ToUniversalTime();

            if (age >= TimeSpan.FromMinutes(-2) &&
                age <= TimeSpan.FromMinutes(measurement.StaleAfterMinutes))
            {
                currentTemperature = temperature;
            }
        }

        var freezingForecast =
            forecastPoints
                .Where(point =>
                    double.IsFinite(point.Temperature) &&
                    point.Temperature < 0 &&
                    point.Time.ToUniversalTime() >= nowUtc &&
                    point.Time.ToUniversalTime() <= nowUtc.Add(ForecastHorizon))
                .OrderBy(point => point.Time)
                .ToList();

        if (currentTemperature is null && freezingForecast.Count == 0)
        {
            return null;
        }

        return new FrostWarningModel
        {
            CurrentTemperatureCelsius = currentTemperature,
            FirstForecastBelowZeroAtUtc = freezingForecast.Count > 0
                ? freezingForecast[0].Time.ToUniversalTime()
                : null,
            MinimumForecastTemperatureCelsius = freezingForecast.Count > 0
                ? freezingForecast.Min(point => point.Temperature)
                : null,
            ForecastLocationName = forecastLocationName,
            ForecastIsNearbyProxy = forecastIsNearbyProxy
        };
    }
}
