using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VigdalsMorningsguide.Models;
using VigdalsMorningsguide.Options;

namespace VigdalsMorningsguide.Services;

public sealed class FrostService
{
    private static readonly TimeZoneInfo NorwegianTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    private readonly HttpClient _httpClient;
    private readonly FrostOptions _options;
    private readonly DegreeDayCalculationService
        _degreeDayCalculationService;
    private readonly ILogger<FrostService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public FrostService(
        HttpClient httpClient,
        IOptions<FrostOptions> options,
        DegreeDayCalculationService degreeDayCalculationService,
        ILogger<FrostService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _degreeDayCalculationService =
            degreeDayCalculationService;
        _logger = logger;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<MorningResultModel> CalculateAsync(
        DateTime hungAt,
        double targetDegreeDays,
        WeatherStationModel station,
        DateTime? refrigeratedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(station);

        ValidateConfiguration();
        ValidateStation(station);

        if (targetDegreeDays is < 1 or > 300)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetDegreeDays),
                "Målet må vere mellom 1 og 300 døgngrader.");
        }

        /*
         * datetime-local frå nettlesaren inneheld ikkje tidssone.
         * Me tolkar verdien som norsk lokal tid.
         */
        var hungAtLocal =
            DateTime.SpecifyKind(
                hungAt,
                DateTimeKind.Unspecified);

        var nowUtc =
            DateTimeOffset.UtcNow;

        var nowLocalOffset =
            TimeZoneInfo.ConvertTime(
                nowUtc,
                NorwegianTimeZone);

        var nowLocal =
            DateTime.SpecifyKind(
                nowLocalOffset.DateTime,
                DateTimeKind.Unspecified);

        ValidateHungAt(
            hungAtLocal,
            nowLocal);

        DateTime? refrigeratedAtLocal = null;
        DateTimeOffset? refrigeratedAtUtc = null;

        if (refrigeratedAt.HasValue)
        {
            refrigeratedAtLocal =
                DateTime.SpecifyKind(
                    refrigeratedAt.Value,
                    DateTimeKind.Unspecified);

            ValidateRefrigeratedAt(
                refrigeratedAtLocal.Value,
                hungAtLocal,
                nowLocal);

            refrigeratedAtUtc =
                ConvertLocalToUtc(
                    refrigeratedAtLocal.Value);
        }

        var hungAtUtc =
            ConvertLocalToUtc(
                hungAtLocal);

        /*
         * Me hentar éi måling før opphengstidspunktet slik at
         * me kan rekne intervallet frå nøyaktig opphengstid.
         *
         * Dersom kjøtet er lagt i kjøleskap, treng me berre
         * Frost-data fram til kjøleskapstidspunktet.
         */
        var queryFromUtc =
            hungAtUtc.AddMinutes(
                -_options.MeasurementIntervalMinutes);

        var observationEndUtc =
            refrigeratedAtUtc.HasValue &&
            refrigeratedAtUtc.Value < nowUtc
                ? refrigeratedAtUtc.Value
                : nowUtc;

        var queryToUtc =
            observationEndUtc.AddMinutes(
                _options.MeasurementIntervalMinutes);

        var referenceTime =
            $"{FormatUtc(queryFromUtc)}/" +
            $"{FormatUtc(queryToUtc)}";

        var requestUri =
            BuildRequestUri(
                referenceTime,
                station);

        _logger.LogInformation(
            "Hentar temperaturdata frå {SourceId} ({SourceName}). " +
            "Oppheng: {HungAt}. Utrekning: {CalculatedAt}. " +
            "Kjøleskap frå: {RefrigeratedAt}.",
            station.SourceId,
            station.Name,
            hungAtLocal.ToString(
                "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture),
            nowLocal.ToString(
                "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture),
            refrigeratedAtLocal?.ToString(
                "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture)
            ?? "ikkje registrert");

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
                "Frost returnerte HTTP {StatusCode}: {Reason}",
                (int)response.StatusCode,
                reason);

            throw new HttpRequestException(
                $"Frost returnerte HTTP " +
                $"{(int)response.StatusCode}: {reason}",
                inner: null,
                response.StatusCode);
        }

        var frostResponse =
            JsonSerializer.Deserialize<FrostObservationResponse>(
                json,
                _jsonOptions)
            ?? throw new JsonException(
                "Frost returnerte eit tomt eller ugyldig JSON-svar.");

        var measurements =
            ExtractMeasurements(
                frostResponse,
                station.ElementId);

        return _degreeDayCalculationService.Calculate(
            hungAtLocal,
            targetDegreeDays,
            new TemperatureSourceModel
            {
                SourceId =
                    station.SourceId,

                Name =
                    station.Name,

                DistanceKilometres =
                    station.DistanceKilometres,

                Latitude =
                    station.Latitude,

                Longitude =
                    station.Longitude,

                MetresAboveSeaLevel =
                    station.MetresAboveSeaLevel
            },
            refrigeratedAtLocal,
            measurements,
            _options.MeasurementIntervalMinutes,
            _options.MaximumAcceptedGapMinutes,
            _options.MinimumCoveragePercent,
            _options.MaximumDaysBack,
            nowUtc);
    }

    private static List<TemperatureMeasurementModel> ExtractMeasurements(
        FrostObservationResponse frostResponse,
        string elementId)
    {
        return frostResponse.Data
            .SelectMany(dataPoint =>
                dataPoint.Observations
                    .Where(observation =>
                        string.Equals(
                            observation.ElementId,
                            elementId,
                            StringComparison.Ordinal))
                    .Select(observation =>
                        new TemperatureMeasurementModel(
                            dataPoint.ReferenceTime.ToUniversalTime(),
                            observation.Value,
                            observation.QualityCode)))
            /*
             * Frost kan i enkelte tilfelle returnere fleire
             * observasjonar for same tidspunkt.
             */
            .GroupBy(measurement =>
                measurement.UtcTimestamp)
            .Select(group =>
                group.First())
            .OrderBy(measurement =>
                measurement.UtcTimestamp)
            .ToList();
    }

    private static string BuildRequestUri(
        string referenceTime,
        WeatherStationModel station)
    {
        var requestUri =
            "observations/v0.jsonld" +
            $"?sources={Encode(station.SourceId)}" +
            $"&referencetime={Encode(referenceTime)}" +
            $"&elements={Encode(station.ElementId)}" +
            $"&timeoffsets={Encode(station.TimeOffset)}" +
            $"&timeresolutions={Encode(station.TimeResolution)}" +
            $"&timeseriesids={station.TimeSeriesId}";

        if (station.Level.HasValue)
        {
            requestUri +=
                $"&levels={station.Level.Value.ToString(
                    "0.0",
                    CultureInfo.InvariantCulture)}";
        }

        return requestUri;
    }

    private static void ValidateStation(
        WeatherStationModel station)
    {
        if (string.IsNullOrWhiteSpace(
                station.SourceId))
        {
            throw new ArgumentException(
                "Den valde målestasjonen manglar kjelde-ID.",
                nameof(station));
        }

        if (string.IsNullOrWhiteSpace(
                station.Name))
        {
            throw new ArgumentException(
                "Den valde målestasjonen manglar namn.",
                nameof(station));
        }

        if (string.IsNullOrWhiteSpace(
                station.ElementId))
        {
            throw new ArgumentException(
                "Den valde målestasjonen manglar måleelement.",
                nameof(station));
        }

        if (string.IsNullOrWhiteSpace(
                station.TimeResolution))
        {
            throw new ArgumentException(
                "Den valde målestasjonen manglar tidsoppløysing.",
                nameof(station));
        }

        if (string.IsNullOrWhiteSpace(
                station.TimeOffset))
        {
            throw new ArgumentException(
                "Den valde målestasjonen manglar tidsforskyving.",
                nameof(station));
        }
    }

    private static void ValidateRefrigeratedAt(
        DateTime refrigeratedAtLocal,
        DateTime hungAtLocal,
        DateTime nowLocal)
    {
        if (refrigeratedAtLocal < hungAtLocal)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refrigeratedAtLocal),
                "Kjøleskapsdatoen kan ikkje vere før " +
                "opphengstidspunktet.");
        }

        if (refrigeratedAtLocal > nowLocal)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refrigeratedAtLocal),
                "Kjøleskapsdatoen kan ikkje vere fram i tid.");
        }

        if (NorwegianTimeZone.IsInvalidTime(
                refrigeratedAtLocal))
        {
            throw new ArgumentException(
                "Kjøleskapstidspunktet finst ikkje på grunn av " +
                "overgang til sommartid.",
                nameof(refrigeratedAtLocal));
        }

        if (NorwegianTimeZone.IsAmbiguousTime(
                refrigeratedAtLocal))
        {
            throw new ArgumentException(
                "Kjøleskapstidspunktet er tvitydig på grunn av " +
                "overgang frå sommartid.",
                nameof(refrigeratedAtLocal));
        }
    }

    private void ValidateHungAt(
        DateTime hungAtLocal,
        DateTime nowLocal)
    {
        if (hungAtLocal > nowLocal)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hungAtLocal),
                "Opphengstidspunktet kan ikkje vere fram i tid.");
        }

        var earliestAllowed =
            nowLocal.AddDays(
                -_options.MaximumDaysBack);

        if (hungAtLocal < earliestAllowed)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hungAtLocal),
                $"Du kan maksimalt hente " +
                $"{_options.MaximumDaysBack} døgn bakover.");
        }

        if (NorwegianTimeZone.IsInvalidTime(
                hungAtLocal))
        {
            throw new ArgumentException(
                "Tidspunktet finst ikkje på grunn av overgang " +
                "til sommartid.",
                nameof(hungAtLocal));
        }

        if (NorwegianTimeZone.IsAmbiguousTime(
                hungAtLocal))
        {
            throw new ArgumentException(
                "Tidspunktet er tvitydig på grunn av overgang " +
                "frå sommartid. Vel eit anna klokkeslett.",
                nameof(hungAtLocal));
        }
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

        if (_options.MinimumCoveragePercent
            is < 0 or > 100)
        {
            throw new InvalidOperationException(
                "Frost:MinimumCoveragePercent må vere " +
                "mellom 0 og 100.");
        }

        if (_options.MeasurementIntervalMinutes <= 0)
        {
            throw new InvalidOperationException(
                "Frost:MeasurementIntervalMinutes må vere " +
                "større enn null.");
        }

        if (_options.MaximumAcceptedGapMinutes <
            _options.MeasurementIntervalMinutes)
        {
            throw new InvalidOperationException(
                "Frost:MaximumAcceptedGapMinutes kan ikkje vere " +
                "mindre enn måleintervallet.");
        }

        if (_options.MaximumDaysBack <= 0)
        {
            throw new InvalidOperationException(
                "Frost:MaximumDaysBack må vere større enn null.");
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

    private static AuthenticationHeaderValue
        CreateAuthorizationHeader(
            string clientId)
    {
        /*
         * Frost brukar klient-ID som Basic Auth-brukarnamn.
         * Passordet er tomt.
         */
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

    private static DateTimeOffset ConvertLocalToUtc(
        DateTime localDateTime)
    {
        var unspecified =
            DateTime.SpecifyKind(
                localDateTime,
                DateTimeKind.Unspecified);

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

    private static string Encode(
        string value)
    {
        return Uri.EscapeDataString(
            value);
    }
}
