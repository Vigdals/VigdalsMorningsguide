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
    private const double RefrigeratorTemperatureCelsius = 4.0;

    private static readonly TimeZoneInfo NorwegianTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    private readonly HttpClient _httpClient;
    private readonly FrostOptions _options;
    private readonly ILogger<FrostService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public FrostService(
        HttpClient httpClient,
        IOptions<FrostOptions> options,
        ILogger<FrostService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
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

        return BuildResult(
            measurements,
            hungAtLocal,
            nowLocal,
            hungAtUtc,
            nowUtc,
            targetDegreeDays,
            station,
            refrigeratedAtLocal);
    }

    private static List<TemperatureMeasurement> ExtractMeasurements(
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
                    {
                        var localTimestamp =
                            TimeZoneInfo.ConvertTime(
                                dataPoint.ReferenceTime,
                                NorwegianTimeZone);

                        return new TemperatureMeasurement(
                            dataPoint.ReferenceTime.ToUniversalTime(),
                            localTimestamp,
                            observation.Value,
                            observation.QualityCode);
                    }))
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

    private MorningResultModel BuildResult(
        IReadOnlyList<TemperatureMeasurement> measurements,
        DateTime hungAtLocal,
        DateTime calculatedAtLocal,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        double targetDegreeDays,
        WeatherStationModel station,
        DateTime? refrigeratedAtLocal)
    {
        var dailyResults =
            new List<MorningDayModel>();

        var accumulatedDegreeDays =
            0.0;

        var includedWeightedTemperatureHours =
            0.0;

        var includedCoveredHours =
            0.0;

        var totalCoveredHours =
            0.0;

        var firstDate =
            DateOnly.FromDateTime(
                hungAtLocal);

        var lastDate =
            DateOnly.FromDateTime(
                calculatedAtLocal);

        for (
            var date = firstDate;
            date <= lastDate;
            date = date.AddDays(1))
        {
            var dayStartLocal =
                DateTime.SpecifyKind(
                    date.ToDateTime(
                        TimeOnly.MinValue),
                    DateTimeKind.Unspecified);

            var nextDayStartLocal =
                DateTime.SpecifyKind(
                    date
                        .AddDays(1)
                        .ToDateTime(
                            TimeOnly.MinValue),
                    DateTimeKind.Unspecified);

            var segmentStartLocal =
                hungAtLocal > dayStartLocal
                    ? hungAtLocal
                    : dayStartLocal;

            var segmentEndLocal =
                calculatedAtLocal < nextDayStartLocal
                    ? calculatedAtLocal
                    : nextDayStartLocal;

            if (segmentEndLocal <= segmentStartLocal)
            {
                continue;
            }

            var segmentStartUtc =
                ConvertLocalToUtc(
                    segmentStartLocal);

            var segmentEndUtc =
                ConvertLocalToUtc(
                    segmentEndLocal);

            var segmentDurationHours =
                (segmentEndUtc - segmentStartUtc)
                .TotalHours;

            /*
             * Ein kalenderdag kan bestå av to periodar:
             *
             * 1. Utandørsperioden, rekna frå Frost.
             * 2. Kjøleskapsperioden, rekna med fast 4 °C.
             *
             * Dette er særleg viktig på sjølve dagen kjøtet
             * blir lagt i kjøleskap.
             */
            var outdoorStartLocal =
                segmentStartLocal;

            var outdoorEndLocal =
                segmentEndLocal;

            if (refrigeratedAtLocal.HasValue &&
                refrigeratedAtLocal.Value < outdoorEndLocal)
            {
                outdoorEndLocal =
                    refrigeratedAtLocal.Value;
            }

            if (outdoorEndLocal < outdoorStartLocal)
            {
                outdoorEndLocal =
                    outdoorStartLocal;
            }

            DateTime? refrigeratorStartLocal =
                null;

            if (refrigeratedAtLocal.HasValue &&
                refrigeratedAtLocal.Value < segmentEndLocal)
            {
                refrigeratorStartLocal =
                    refrigeratedAtLocal.Value > segmentStartLocal
                        ? refrigeratedAtLocal.Value
                        : segmentStartLocal;
            }

            /*
             * Først reknar me delen av dagen som var utandørs.
             */
            var outdoorDegreeDays =
                0.0;

            var outdoorWeightedTemperatureHours =
                0.0;

            var outdoorCoveredHours =
                0.0;

            var outdoorDurationHours =
                0.0;

            var outdoorIncluded =
                true;

            var observationCount =
                0;

            var expectedObservationCount =
                0;

            var qualityCodes =
                new List<int>();

            if (outdoorEndLocal > outdoorStartLocal)
            {
                var outdoorStartUtc =
                    ConvertLocalToUtc(
                        outdoorStartLocal);

                var outdoorEndUtc =
                    ConvertLocalToUtc(
                        outdoorEndLocal);

                outdoorDurationHours =
                    (outdoorEndUtc - outdoorStartUtc)
                    .TotalHours;

                var integration =
                    IntegrateMeasurements(
                        measurements,
                        outdoorStartUtc,
                        outdoorEndUtc);

                var outdoorCoveragePercent =
                    outdoorDurationHours <= 0
                        ? 0
                        : Math.Min(
                            100,
                            integration.CoveredHours /
                            outdoorDurationHours *
                            100);

                outdoorIncluded =
                    integration.CoveredHours > 0 &&
                    outdoorCoveragePercent >=
                    _options.MinimumCoveragePercent;

                totalCoveredHours +=
                    integration.CoveredHours;

                if (outdoorIncluded)
                {
                    outdoorDegreeDays =
                        integration.DegreeDays;

                    outdoorWeightedTemperatureHours =
                        integration.WeightedTemperatureHours;

                    outdoorCoveredHours =
                        integration.CoveredHours;

                    includedWeightedTemperatureHours +=
                        integration.WeightedTemperatureHours;

                    includedCoveredHours +=
                        integration.CoveredHours;
                }

                observationCount =
                    measurements.Count(measurement =>
                        measurement.UtcTimestamp >= outdoorStartUtc &&
                        measurement.UtcTimestamp < outdoorEndUtc);

                expectedObservationCount =
                    CalculateExpectedObservationCount(
                        outdoorStartUtc,
                        outdoorEndUtc);

                qualityCodes =
                    measurements
                        .Where(measurement =>
                            measurement.UtcTimestamp >= outdoorStartUtc &&
                            measurement.UtcTimestamp < outdoorEndUtc &&
                            measurement.QualityCode.HasValue)
                        .Select(measurement =>
                            measurement.QualityCode!.Value)
                        .Distinct()
                        .OrderBy(code =>
                            code)
                        .ToList();
            }

            /*
             * Deretter reknar me eventuell kjøleskapsdel.
             */
            var refrigeratorDegreeDays =
                0.0;

            var refrigeratorWeightedTemperatureHours =
                0.0;

            var refrigeratorHours =
                0.0;

            if (refrigeratorStartLocal.HasValue)
            {
                var refrigeratorStartUtc =
                    ConvertLocalToUtc(
                        refrigeratorStartLocal.Value);

                refrigeratorHours =
                    (segmentEndUtc - refrigeratorStartUtc)
                    .TotalHours;

                if (refrigeratorHours > 0)
                {
                    refrigeratorDegreeDays =
                        RefrigeratorTemperatureCelsius *
                        refrigeratorHours /
                        24.0;

                    refrigeratorWeightedTemperatureHours =
                        RefrigeratorTemperatureCelsius *
                        refrigeratorHours;

                    /*
                     * Kjøleskapstemperaturen er definert som kjend,
                     * så denne perioden har full dekning.
                     */
                    totalCoveredHours +=
                        refrigeratorHours;

                    includedWeightedTemperatureHours +=
                        refrigeratorWeightedTemperatureHours;

                    includedCoveredHours +=
                        refrigeratorHours;
                }
            }

            /*
             * Summen for dagen består av begge periodane.
             */
            var degreeDays =
                outdoorDegreeDays +
                refrigeratorDegreeDays;

            accumulatedDegreeDays +=
                degreeDays;

            var dayCoveredHours =
                outdoorCoveredHours +
                refrigeratorHours;

            var dayWeightedTemperatureHours =
                outdoorWeightedTemperatureHours +
                refrigeratorWeightedTemperatureHours;

            double? meanTemperature =
                dayCoveredHours <= 0
                    ? null
                    : dayWeightedTemperatureHours /
                      dayCoveredHours;

            var coveragePercent =
                segmentDurationHours <= 0
                    ? 0
                    : Math.Min(
                        100,
                        (
                            outdoorCoveredHours +
                            refrigeratorHours
                        ) /
                        segmentDurationHours *
                        100);

            /*
             * Dersom dagen hadde ein utandørsperiode som mangla
             * tilstrekkelege Frost-data, markerer me dagen som
             * ufullstendig sjølv om kjøleskapsdelen er gyldig.
             *
             * Kjøleskapsdelen blir likevel rekna med.
             */
            var includedInTotal =
                outdoorDurationHours <= 0 ||
                outdoorIncluded;

            dailyResults.Add(
                new MorningDayModel
                {
                    Date =
                        date,

                    PeriodStart =
                        segmentStartLocal,

                    PeriodEnd =
                        segmentEndLocal,

                    MeanTemperature =
                        meanTemperature,

                    ObservationCount =
                        observationCount,

                    ExpectedObservationCount =
                        expectedObservationCount,

                    CoveragePercent =
                        coveragePercent,

                    IncludedInTotal =
                        includedInTotal,

                    UsesRefrigeratorTemperature =
                        refrigeratorHours > 0,

                    DegreeDays =
                        degreeDays,

                    AccumulatedDegreeDays =
                        accumulatedDegreeDays,

                    QualityCodes =
                        qualityCodes
                });
        }

        var totalPeriodHours =
            (periodEndUtc - periodStartUtc)
            .TotalHours;

        var totalCoveragePercent =
            totalPeriodHours <= 0
                ? 0
                : Math.Min(
                    100,
                    totalCoveredHours /
                    totalPeriodHours *
                    100);

        double? averageTemperature =
            includedCoveredHours <= 0
                ? null
                : includedWeightedTemperatureHours /
                  includedCoveredHours;

        var observationCountInPeriod =
            measurements.Count(measurement =>
                measurement.UtcTimestamp >= periodStartUtc &&
                measurement.UtcTimestamp <= periodEndUtc);

        return new MorningResultModel
        {
            HungAt =
                hungAtLocal,

            CalculatedAt =
                calculatedAtLocal,

            CalculatedAtUtc =
                periodEndUtc,

            RefrigeratedAt =
                refrigeratedAtLocal,

            RefrigeratorTemperatureCelsius =
                refrigeratedAtLocal.HasValue
                    ? RefrigeratorTemperatureCelsius
                    : null,

            SourceId =
                station.SourceId,

            SourceName =
                station.Name,

            StationDistanceKilometres =
                station.DistanceKilometres,

            StationLatitude =
                station.Latitude,

            StationLongitude =
                station.Longitude,

            StationMetresAboveSeaLevel =
                station.MetresAboveSeaLevel,

            TargetDegreeDays =
                targetDegreeDays,

            TotalDegreeDays =
                accumulatedDegreeDays,

            AverageTemperature =
                averageTemperature,

            ObservationCount =
                observationCountInPeriod,

            CoveragePercent =
                totalCoveragePercent,

            Days =
                dailyResults
        };
    }

    private IntegrationResult IntegrateMeasurements(
        IReadOnlyList<TemperatureMeasurement> measurements,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd)
    {
        if (measurements.Count == 0 ||
            periodEnd <= periodStart)
        {
            return IntegrationResult.Empty;
        }

        var totalDegreeDays =
            0.0;

        var weightedTemperatureHours =
            0.0;

        var coveredHours =
            0.0;

        for (
            var index = 0;
            index < measurements.Count;
            index++)
        {
            var current =
                measurements[index];

            /*
             * Ei temperaturmåling blir rekna som gjeldande fram
             * til neste måling.
             *
             * For siste måling brukar me normalt måleintervallet.
             */
            var nextTimestamp =
                index + 1 < measurements.Count
                    ? measurements[index + 1].UtcTimestamp
                    : current.UtcTimestamp.AddMinutes(
                        _options.MeasurementIntervalMinutes);

            var gapMinutes =
                (nextTimestamp - current.UtcTimestamp)
                .TotalMinutes;

            /*
             * Eit stort hol i datasettet skal ikkje tolkast som
             * at same temperatur gjaldt gjennom heile holet.
             */
            if (gapMinutes <= 0 ||
                gapMinutes >
                _options.MaximumAcceptedGapMinutes)
            {
                continue;
            }

            var intervalStart =
                current.UtcTimestamp > periodStart
                    ? current.UtcTimestamp
                    : periodStart;

            var intervalEnd =
                nextTimestamp < periodEnd
                    ? nextTimestamp
                    : periodEnd;

            if (intervalEnd <= intervalStart)
            {
                continue;
            }

            var intervalHours =
                (intervalEnd - intervalStart)
                .TotalHours;

            weightedTemperatureHours +=
                current.Temperature *
                intervalHours;

            totalDegreeDays +=
                Math.Max(
                    0,
                    current.Temperature) *
                intervalHours /
                24.0;

            coveredHours +=
                intervalHours;
        }

        return new IntegrationResult(
            totalDegreeDays,
            weightedTemperatureHours,
            coveredHours);
    }

    private int CalculateExpectedObservationCount(
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd)
    {
        var totalMinutes =
            (periodEnd - periodStart)
            .TotalMinutes;

        if (totalMinutes <= 0)
        {
            return 0;
        }

        return (int)Math.Ceiling(
            totalMinutes /
            _options.MeasurementIntervalMinutes);
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

    private sealed record TemperatureMeasurement(
        DateTimeOffset UtcTimestamp,
        DateTimeOffset LocalTimestamp,
        double Temperature,
        int? QualityCode);

    private sealed record IntegrationResult(
        double DegreeDays,
        double WeightedTemperatureHours,
        double CoveredHours)
    {
        public static IntegrationResult Empty { get; } =
            new(
                DegreeDays: 0,
                WeightedTemperatureHours: 0,
                CoveredHours: 0);
    }
}