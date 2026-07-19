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
        CancellationToken cancellationToken = default)
    {

        if (targetDegreeDays is < 1 or > 300)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetDegreeDays),
                "Målet må vere mellom 1 og 300 døgngrader.");
        }
        ValidateConfiguration();

        /*
         * datetime-local frå nettlesaren inneheld ikkje tidssone.
         * Me tolkar verdien som norsk lokal tid.
         */
        var hungAtLocal = DateTime.SpecifyKind(
            hungAt,
            DateTimeKind.Unspecified);

        var nowUtc = DateTimeOffset.UtcNow;

        var nowLocalOffset = TimeZoneInfo.ConvertTime(
            nowUtc,
            NorwegianTimeZone);

        var nowLocal = DateTime.SpecifyKind(
            nowLocalOffset.DateTime,
            DateTimeKind.Unspecified);

        ValidateHungAt(
            hungAtLocal,
            nowLocal);

        var hungAtUtcDateTime =
            TimeZoneInfo.ConvertTimeToUtc(
                hungAtLocal,
                NorwegianTimeZone);

        var hungAtUtc = new DateTimeOffset(
            hungAtUtcDateTime,
            TimeSpan.Zero);

        /*
         * Me hentar éi måling før starten slik at me kan rekne
         * intervallet frå nøyaktig opphengstidspunkt.
         *
         * Me hentar også litt etter no. Frost vil berre returnere
         * verdiar som faktisk finst.
         */
        var queryFromUtc = hungAtUtc.AddMinutes(
            -_options.MeasurementIntervalMinutes);

        var queryToUtc = nowUtc.AddMinutes(
            _options.MeasurementIntervalMinutes);

        var referenceTime =
            $"{FormatUtc(queryFromUtc)}/" +
            $"{FormatUtc(queryToUtc)}";

        var requestUri =
            BuildRequestUri(referenceTime);

        _logger.LogInformation(
            "Hentar temperaturdata frå {SourceId}. " +
            "Oppheng: {HungAt}. Utrekning: {CalculatedAt}",
            _options.SourceId,
            hungAtLocal.ToString("yyyy-MM-dd HH:mm"),
            nowLocal.ToString("yyyy-MM-dd HH:mm"));

        using var request = new HttpRequestMessage(
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
            var reason = TryReadFrostError(json);

            _logger.LogWarning(
                "Frost returnerte HTTP {StatusCode}: {Reason}",
                (int)response.StatusCode,
                reason);

            throw new HttpRequestException(
                $"Frost returnerte HTTP " +
                $"{(int)response.StatusCode}: {reason}");
        }

        var frostResponse =
            JsonSerializer.Deserialize<FrostObservationResponse>(
                json,
                _jsonOptions)
            ?? throw new JsonException(
                "Frost returnerte eit tomt eller ugyldig JSON-svar.");

        var measurements =
            ExtractMeasurements(frostResponse);

        return BuildResult(
            measurements,
            hungAtLocal,
            nowLocal,
            hungAtUtc,
            nowUtc,
            targetDegreeDays);
    }

    private List<TemperatureMeasurement> ExtractMeasurements(
        FrostObservationResponse frostResponse)
    {
        return frostResponse.Data
            .SelectMany(dataPoint =>
                dataPoint.Observations
                    .Where(observation =>
                        string.Equals(
                            observation.ElementId,
                            _options.ElementId,
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
        double targetDegreeDays)
    {
        var dailyResults = new List<MorningDayModel>();

        var accumulatedDegreeDays = 0.0;
        var includedWeightedTemperatureHours = 0.0;
        var includedCoveredHours = 0.0;

        var firstDate =
            DateOnly.FromDateTime(hungAtLocal);

        var lastDate =
            DateOnly.FromDateTime(calculatedAtLocal);

        for (
            var date = firstDate;
            date <= lastDate;
            date = date.AddDays(1))
        {
            var dayStartLocal = DateTime.SpecifyKind(
                date.ToDateTime(TimeOnly.MinValue),
                DateTimeKind.Unspecified);

            var nextDayStartLocal = DateTime.SpecifyKind(
                date.AddDays(1).ToDateTime(TimeOnly.MinValue),
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
                ConvertLocalToUtc(segmentStartLocal);

            var segmentEndUtc =
                ConvertLocalToUtc(segmentEndLocal);

            var integration = IntegrateMeasurements(
                measurements,
                segmentStartUtc,
                segmentEndUtc);

            var segmentDurationHours =
                (segmentEndUtc - segmentStartUtc)
                .TotalHours;

            var coveragePercent =
                segmentDurationHours <= 0
                    ? 0
                    : Math.Min(
                        100,
                        integration.CoveredHours /
                        segmentDurationHours *
                        100);

            var includedInTotal =
                integration.CoveredHours > 0 &&
                coveragePercent >=
                _options.MinimumCoveragePercent;

            var degreeDays =
                includedInTotal
                    ? integration.DegreeDays
                    : 0;

            accumulatedDegreeDays += degreeDays;

            if (includedInTotal)
            {
                includedWeightedTemperatureHours +=
                    integration.WeightedTemperatureHours;

                includedCoveredHours +=
                    integration.CoveredHours;
            }

            double? meanTemperature =
                integration.CoveredHours <= 0
                    ? null
                    : integration.WeightedTemperatureHours /
                      integration.CoveredHours;

            var observationCount =
                measurements.Count(measurement =>
                    measurement.UtcTimestamp >= segmentStartUtc &&
                    measurement.UtcTimestamp < segmentEndUtc);

            var expectedObservationCount =
                CalculateExpectedObservationCount(
                    segmentStartUtc,
                    segmentEndUtc);

            var qualityCodes = measurements
                .Where(measurement =>
                    measurement.UtcTimestamp >= segmentStartUtc &&
                    measurement.UtcTimestamp < segmentEndUtc &&
                    measurement.QualityCode.HasValue)
                .Select(measurement =>
                    measurement.QualityCode!.Value)
                .Distinct()
                .OrderBy(code => code)
                .ToList();

            dailyResults.Add(
                new MorningDayModel
                {
                    Date = date,
                    PeriodStart = segmentStartLocal,
                    PeriodEnd = segmentEndLocal,
                    MeanTemperature = meanTemperature,
                    ObservationCount = observationCount,
                    ExpectedObservationCount =
                        expectedObservationCount,
                    CoveragePercent = coveragePercent,
                    IncludedInTotal = includedInTotal,
                    DegreeDays = degreeDays,
                    AccumulatedDegreeDays =
                        accumulatedDegreeDays,
                    QualityCodes = qualityCodes
                });
        }

        var totalPeriodIntegration =
            IntegrateMeasurements(
                measurements,
                periodStartUtc,
                periodEndUtc);

        var totalPeriodHours =
            (periodEndUtc - periodStartUtc)
            .TotalHours;

        var totalCoveragePercent =
            totalPeriodHours <= 0
                ? 0
                : Math.Min(
                    100,
                    totalPeriodIntegration.CoveredHours /
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
            HungAt = hungAtLocal,
            CalculatedAt = calculatedAtLocal,
            SourceId = _options.SourceId,
            SourceName = _options.SourceName,
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
            Days = dailyResults
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

        var totalDegreeDays = 0.0;
        var weightedTemperatureHours = 0.0;
        var coveredHours = 0.0;

        for (
            var index = 0;
            index < measurements.Count;
            index++)
        {
            var current = measurements[index];

            /*
             * Ei temperaturmåling blir rekna som gjeldande fram
             * til neste måling.
             *
             * For siste måling brukar me normalt måleintervall.
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
                Math.Max(0, current.Temperature) *
                intervalHours /
                24.0;

            coveredHours += intervalHours;
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

    private string BuildRequestUri(
        string referenceTime)
    {
        return
            "observations/v0.jsonld" +
            $"?sources={Encode(_options.SourceId)}" +
            $"&referencetime={Encode(referenceTime)}" +
            $"&elements={Encode(_options.ElementId)}" +
            $"&timeoffsets={Encode(_options.TimeOffset)}" +
            $"&timeresolutions={Encode(_options.TimeResolution)}" +
            $"&timeseriesids={_options.TimeSeriesId}" +
            $"&levels={_options.Level.ToString(
                "0.0",
                CultureInfo.InvariantCulture)}";
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

        if (string.IsNullOrWhiteSpace(
                _options.SourceId))
        {
            throw new InvalidOperationException(
                "Frost:SourceId manglar.");
        }

        if (string.IsNullOrWhiteSpace(
                _options.ElementId))
        {
            throw new InvalidOperationException(
                "Frost:ElementId manglar.");
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

            return string.IsNullOrWhiteSpace(
                error?.Error?.Reason)
                ? "Ukjend feil frå Frost."
                : error.Error.Reason;
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
        return Uri.EscapeDataString(value);
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