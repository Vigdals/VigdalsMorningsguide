using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VigdalsMorningsguide.Models;
using VigdalsMorningsguide.Options;

namespace VigdalsMorningsguide.Services;

public sealed class FrostStationService
{
    private const string TemperatureElement =
        "air_temperature";

    private const string RequiredTimeResolution =
        "PT10M";

    private const string RequiredTimeOffset =
        "PT0H";

    private static readonly TimeZoneInfo NorwegianTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById(
            "Europe/Oslo");

    private readonly HttpClient _httpClient;
    private readonly FrostOptions _options;
    private readonly ILogger<FrostStationService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public FrostStationService(
        HttpClient httpClient,
        IOptions<FrostOptions> options,
        ILogger<FrostStationService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<WeatherStationModel?> ResolveStationAsync(
        WeatherStationOptionModel selectedStation,
        DateTime hungAtLocal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            selectedStation);

        ValidateConfiguration();

        var hungAtUtc =
            ConvertLocalToUtc(
                hungAtLocal);

        var nowUtc =
            DateTimeOffset.UtcNow;

        var timeSeries =
            await FindSuitableTimeSeriesAsync(
                selectedStation.SourceId,
                hungAtUtc,
                nowUtc,
                cancellationToken);

        if (timeSeries is null)
        {
            _logger.LogWarning(
                "Stasjon {SourceId} har ikkje ei gyldig " +
                "10-minuttsserie for temperatur i perioden.",
                selectedStation.SourceId);

            return null;
        }

        var station =
            new WeatherStationModel
            {
                SourceId =
                    timeSeries.SourceId,

                Name =
                    selectedStation.Name,

                Latitude =
                    selectedStation.Latitude,

                Longitude =
                    selectedStation.Longitude,

                DistanceKilometres =
                    0,

                MetresAboveSeaLevel =
                    selectedStation.MetresAboveSeaLevel,

                ElementId =
                    timeSeries.ElementId,

                TimeOffset =
                    timeSeries.TimeOffset,

                TimeResolution =
                    timeSeries.TimeResolution,

                TimeSeriesId =
                    timeSeries.TimeSeriesId,

                Level =
                    timeSeries.Level?.Value,

                ValidFrom =
                    timeSeries.ValidFrom,

                ValidTo =
                    timeSeries.ValidTo
            };

        _logger.LogInformation(
            "Valde temperaturstasjon {SourceId} ({Name}) " +
            "på {Height:0} moh. Serie: {Resolution}, " +
            "timeSeriesId {TimeSeriesId}.",
            station.SourceId,
            station.Name,
            station.MetresAboveSeaLevel,
            station.TimeResolution,
            station.TimeSeriesId);

        return station;
    }

    private async Task<FrostTimeSeriesModel?>
        FindSuitableTimeSeriesAsync(
            string sourceId,
            DateTimeOffset hungAtUtc,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
    {
        var baseSourceId =
            sourceId.Split(':')[0];

        var referenceTime =
            $"{FormatUtc(hungAtUtc)}/" +
            $"{FormatUtc(nowUtc)}";

        var requestUri =
            "observations/availableTimeSeries/v0.jsonld" +
            $"?sources={Encode(baseSourceId)}" +
            $"&referencetime={Encode(referenceTime)}" +
            $"&elements={Encode(TemperatureElement)}";

        FrostTimeSeriesResponse response;

        try
        {
            response =
                await SendAsync<FrostTimeSeriesResponse>(
                    requestUri,
                    cancellationToken);
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode is
                  HttpStatusCode.NotFound or
                  HttpStatusCode.BadRequest)
        {
            _logger.LogWarning(
                exception,
                "Fann ingen temperaturserie for {SourceId}.",
                baseSourceId);

            return null;
        }

        return response.Data
            .Where(series =>
                string.Equals(
                    series.ElementId,
                    TemperatureElement,
                    StringComparison.Ordinal))
            .Where(series =>
                string.Equals(
                    series.TimeResolution,
                    RequiredTimeResolution,
                    StringComparison.Ordinal))
            .Where(series =>
                string.Equals(
                    series.TimeOffset,
                    RequiredTimeOffset,
                    StringComparison.Ordinal))
            .Where(series =>
                series.ValidFrom <= hungAtUtc)
            .Where(series =>
                !series.ValidTo.HasValue ||
                series.ValidTo.Value >= nowUtc)
            .OrderBy(series =>
                SourcePriority(
                    series.SourceId))
            .ThenBy(series =>
                TimeSeriesPriority(
                    series.TimeSeriesId))
            .ThenBy(series =>
                LevelPriority(
                    series.Level))
            .FirstOrDefault();
    }

    private async Task<TResponse> SendAsync<TResponse>(
        string requestUri,
        CancellationToken cancellationToken)
    {
        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                requestUri);

        request.Headers.Authorization =
            CreateAuthorizationHeader(
                _options.ClientId);

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
            var reason =
                TryReadFrostError(
                    json);

            _logger.LogWarning(
                "Frost returnerte HTTP {StatusCode} " +
                "for {RequestUri}: {Reason}",
                (int)response.StatusCode,
                requestUri,
                reason);

            throw new HttpRequestException(
                $"Frost returnerte HTTP " +
                $"{(int)response.StatusCode}: {reason}",
                inner: null,
                response.StatusCode);
        }

        return JsonSerializer.Deserialize<TResponse>(
                   json,
                   _jsonOptions)
               ?? throw new JsonException(
                   "Frost returnerte eit tomt eller ugyldig JSON-svar.");
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(
                _options.ClientId))
        {
            throw new InvalidOperationException(
                "Frost:ClientId manglar. " +
                "Legg han inn med dotnet user-secrets.");
        }

        if (_httpClient.BaseAddress is null)
        {
            throw new InvalidOperationException(
                "BaseAddress manglar på Frost-klienten.");
        }
    }

    private string TryReadFrostError(
        string json)
    {
        try
        {
            var error =
                JsonSerializer.Deserialize<FrostErrorResponse>(
                    json,
                    _jsonOptions);

            if (!string.IsNullOrWhiteSpace(
                    error?.Error?.Reason))
            {
                return error.Error.Reason;
            }

            if (!string.IsNullOrWhiteSpace(
                    error?.Error?.Message))
            {
                return error.Error.Message;
            }

            return "Ukjend feil frå Frost.";
        }
        catch (JsonException)
        {
            return "Klarte ikkje å tolke feilsvaret frå Frost.";
        }
    }

    private static int SourcePriority(
        string sourceId)
    {
        return sourceId.EndsWith(
            ":0",
            StringComparison.Ordinal)
            ? 0
            : 1;
    }

    private static int TimeSeriesPriority(
        int timeSeriesId)
    {
        return timeSeriesId == 0
            ? 0
            : 1;
    }

    private static int LevelPriority(
        FrostLevelModel? level)
    {
        if (level?.Value is null)
        {
            return 10;
        }

        return Math.Abs(
                   level.Value.Value - 2.0) <
               0.01
            ? 0
            : 1;
    }

    private static DateTimeOffset ConvertLocalToUtc(
        DateTime localDateTime)
    {
        var unspecified =
            DateTime.SpecifyKind(
                localDateTime,
                DateTimeKind.Unspecified);

        if (NorwegianTimeZone.IsInvalidTime(
                unspecified))
        {
            throw new ArgumentException(
                "Opphengstidspunktet finst ikkje på grunn av " +
                "overgang til sommartid.",
                nameof(localDateTime));
        }

        if (NorwegianTimeZone.IsAmbiguousTime(
                unspecified))
        {
            throw new ArgumentException(
                "Opphengstidspunktet er tvitydig på grunn av " +
                "overgang frå sommartid. Vel eit anna klokkeslett.",
                nameof(localDateTime));
        }

        var utc =
            TimeZoneInfo.ConvertTimeToUtc(
                unspecified,
                NorwegianTimeZone);

        return new DateTimeOffset(
            utc,
            TimeSpan.Zero);
    }

    private static string FormatUtc(
        DateTimeOffset value)
    {
        return value
            .ToUniversalTime()
            .ToString(
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture);
    }

    private static AuthenticationHeaderValue
        CreateAuthorizationHeader(
            string clientId)
    {
        var rawCredentials =
            $"{clientId}:";

        var encodedCredentials =
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(
                    rawCredentials));

        return new AuthenticationHeaderValue(
            "Basic",
            encodedCredentials);
    }

    private static string Encode(
        string value)
    {
        return Uri.EscapeDataString(
            value);
    }
}