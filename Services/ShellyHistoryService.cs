using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VigdalsMorningsguide.Models;
using VigdalsMorningsguide.Options;

namespace VigdalsMorningsguide.Services;

public sealed class ShellyHistoryService
{
    private const int DefaultMeasurementIntervalMinutes =
        60;

    private static readonly TimeZoneInfo NorwegianTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById(
            "Europe/Oslo");

    private readonly HttpClient _httpClient;
    private readonly ShellyOptions _options;
    private readonly DegreeDayCalculationService
        _degreeDayCalculationService;
    private readonly ShellyCloudRequestGate _requestGate;
    private readonly ILogger<ShellyHistoryService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public string DisplayName =>
        _options.DisplayName;

    public ShellyHistoryService(
        HttpClient httpClient,
        IOptions<ShellyOptions> options,
        DegreeDayCalculationService degreeDayCalculationService,
        ShellyCloudRequestGate requestGate,
        ILogger<ShellyHistoryService> logger)
    {
        _httpClient =
            httpClient;

        _options =
            options.Value;

        _degreeDayCalculationService =
            degreeDayCalculationService;

        _requestGate =
            requestGate;

        _logger =
            logger;

        _jsonOptions =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
    }

    public async Task<MorningResultModel> CalculateAsync(
        DateTime hungAt,
        double targetDegreeDays,
        DateTime? refrigeratedAt,
        CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();

        var nowUtc =
            DateTimeOffset.UtcNow;

        DegreeDayCalculationService.ValidateRequest(
            hungAt,
            targetDegreeDays,
            refrigeratedAt,
            _options.MaximumDaysBack,
            nowUtc);

        var hungAtUtc =
            DegreeDayCalculationService
                .ConvertNorwegianLocalToUtc(
                    hungAt);

        DateTimeOffset? refrigeratedAtUtc =
            refrigeratedAt.HasValue
                ? DegreeDayCalculationService
                    .ConvertNorwegianLocalToUtc(
                        refrigeratedAt.Value)
                : null;

        var observationEndUtc =
            refrigeratedAtUtc.HasValue &&
            refrigeratedAtUtc.Value < nowUtc
                ? refrigeratedAtUtc.Value
                : nowUtc;

        var queryFromUtc =
            hungAtUtc.AddHours(-1);

        var queryToUtc =
            observationEndUtc;

        /*
         * Shelly kan avvise den uferdige bøtta for inneverande
         * minutt. Eitt minutt tilbake gir ei avslutta sluttramme,
         * medan kalkulatoren framleis reknar fram til faktisk no.
         */
        if (queryToUtc > nowUtc.AddMinutes(-1))
        {
            queryToUtc =
                nowUtc.AddMinutes(-1);
        }

        if (queryToUtc <= queryFromUtc)
        {
            queryFromUtc =
                queryToUtc.AddHours(-1);
        }

        var requestUri =
            BuildRequestUri(
                queryFromUtc,
                queryToUtc);

        _logger.LogInformation(
            "Hentar Shelly-temperaturhistorikk. " +
            "Frå {DateFrom} til {DateTo}.",
            queryFromUtc,
            queryToUtc);

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                requestUri);

        await using var requestLease =
            await _requestGate.EnterAsync(
                cancellationToken);

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
                "Shelly Cloud returnerte HTTP {StatusCode} " +
                "for temperaturhistorikk.",
                (int)response.StatusCode);

            throw new HttpRequestException(
                "Shelly Cloud returnerte HTTP " +
                $"{(int)response.StatusCode} for temperaturhistorikk.",
                inner: null,
                response.StatusCode);
        }

        ThrowIfApplicationError(
            json);

        var statistics =
            DeserializeStatistics(
                json);

        var measurements =
            (statistics.History ?? [])
                .Where(entry =>
                    entry.IsAvailable is not false &&
                    entry.IsMissing is not true &&
                    entry.MeanTemperatureCelsius.HasValue)
                .Select(entry =>
                    TryCreateMeasurement(
                        entry,
                        statistics.TimeZone))
                .Where(measurement =>
                    measurement is not null)
                .Select(measurement =>
                    measurement!)
                .GroupBy(measurement =>
                    measurement.UtcTimestamp)
                .Select(group =>
                    group.First())
                .OrderBy(measurement =>
                    measurement.UtcTimestamp)
                .ToList();

        if (measurements.Count == 0)
        {
            throw new ShellyHistoryUnavailableException(
                "Shelly har ingen gyldige temperaturmålingar " +
                "for den valde perioden.");
        }

        var measurementIntervalMinutes =
            ResolveMeasurementIntervalMinutes(
                statistics.HistoryInterval ??
                statistics.Interval,
                measurements);

        var maximumAcceptedGapMinutes =
            Math.Max(
                _options.MaximumAcceptedGapMinutes,
                measurementIntervalMinutes * 3);

        _logger.LogInformation(
            "Henta {MeasurementCount} Shelly-målingar med " +
            "estimert intervall {IntervalMinutes} minutt.",
            measurements.Count,
            measurementIntervalMinutes);

        return _degreeDayCalculationService.Calculate(
            hungAt,
            targetDegreeDays,
            new TemperatureSourceModel
            {
                SourceId =
                    TemperatureSourceCatalog.ShellySourceId,

                Name =
                    _options.DisplayName,

                DistanceKilometres =
                    0,

                Latitude =
                    0,

                Longitude =
                    0,

                MetresAboveSeaLevel =
                    null
            },
            refrigeratedAt,
            measurements,
            measurementIntervalMinutes,
            maximumAcceptedGapMinutes,
            _options.MinimumCoveragePercent,
            _options.MaximumDaysBack,
            nowUtc);
    }

    private string BuildRequestUri(
        DateTimeOffset dateFromUtc,
        DateTimeOffset dateToUtc)
    {
        /*
         * Shelly dokumenterer ikkje statistikk-endepunktet som
         * ein stabil del av Cloud Control API-et. Hald derfor
         * kontrakten avgrensa til denne tenesta og valider svaret
         * strengt, slik at endringar gir synleg feil i staden for
         * feil døgngradeutrekning.
         */
        return
            "statistics/sensor/values" +
            $"?id={Encode(_options.DeviceId)}" +
            "&channel=0" +
            "&date_range=custom" +
            $"&date_from={Encode(FormatShellyLocal(dateFromUtc))}" +
            $"&date_to={Encode(FormatShellyLocal(dateToUtc))}" +
            $"&auth_key={Encode(_options.AuthKey)}";
    }

    private static TemperatureMeasurementModel? TryCreateMeasurement(
        ShellyWeatherHistoryEntry entry,
        string? responseTimeZone)
    {
        if (!TryParseShellyTimestamp(
                entry.Timestamp,
                responseTimeZone,
                out var timestampUtc) ||
            timestampUtc <= DateTimeOffset.UnixEpoch ||
            entry.MeanTemperatureCelsius is not double temperature)
        {
            return null;
        }

        return new TemperatureMeasurementModel(
            timestampUtc,
            temperature);
    }

    private static bool TryParseShellyTimestamp(
        string value,
        string? responseTimeZone,
        out DateTimeOffset timestampUtc)
    {
        timestampUtc =
            default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (DateTimeOffset.TryParseExact(
                value,
                [
                    "yyyy-MM-dd'T'HH:mm:ssK",
                    "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"
                ],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var timestampWithOffset))
        {
            timestampUtc =
                timestampWithOffset.ToUniversalTime();

            return true;
        }

        if (!DateTime.TryParseExact(
                value,
                [
                    "yyyy-MM-dd HH:mm:ss",
                    "yyyy-MM-dd'T'HH:mm:ss"
                ],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var localTimestamp))
        {
            return false;
        }

        localTimestamp =
            DateTime.SpecifyKind(
                localTimestamp,
                DateTimeKind.Unspecified);

        var timeZone =
            ResolveTimeZone(
                responseTimeZone);

        if (timeZone.IsInvalidTime(
                localTimestamp))
        {
            return false;
        }

        var offset =
            timeZone.GetUtcOffset(
                localTimestamp);

        timestampUtc =
            new DateTimeOffset(
                localTimestamp,
                offset)
            .ToUniversalTime();

        return true;
    }

    private static TimeZoneInfo ResolveTimeZone(
        string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(
                    timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                // Bruk norsk tid som trygg reserve for denne målaren.
            }
            catch (InvalidTimeZoneException)
            {
                // Bruk norsk tid som trygg reserve for denne målaren.
            }
        }

        return NorwegianTimeZone;
    }

    private static int ResolveMeasurementIntervalMinutes(
        string? interval,
        IReadOnlyList<TemperatureMeasurementModel> measurements)
    {
        var observedGaps =
            measurements
                .Zip(
                    measurements.Skip(1),
                    (current, next) =>
                        (next.UtcTimestamp - current.UtcTimestamp)
                        .TotalMinutes)
                .Where(minutes =>
                    double.IsFinite(minutes) &&
                    minutes > 0 &&
                    minutes <= 7 * 24 * 60)
                .OrderBy(minutes =>
                    minutes)
                .ToList();

        if (observedGaps.Count >= 2)
        {
            var middleIndex =
                observedGaps.Count / 2;

            var median =
                observedGaps.Count % 2 == 0
                    ? (
                        observedGaps[middleIndex - 1] +
                        observedGaps[middleIndex]
                      ) / 2.0
                    : observedGaps[middleIndex];

            return Math.Max(
                1,
                (int)Math.Round(
                    median,
                    MidpointRounding.AwayFromZero));
        }

        return (interval ?? string.Empty)
            .Trim()
            .ToLowerInvariant() switch
        {
            "minute" => 1,
            "hour" => 60,
            "day" => 24 * 60,
            _ => DefaultMeasurementIntervalMinutes
        };
    }

    private static void ThrowIfApplicationError(
        string json)
    {
        using var document =
            JsonDocument.Parse(
                json);

        var root =
            document.RootElement;

        if (root.ValueKind is not JsonValueKind.Object ||
            !root.TryGetProperty(
                "isok",
                out var isOkElement) ||
            isOkElement.ValueKind is not JsonValueKind.False)
        {
            return;
        }

        throw new HttpRequestException(
            "Shelly Cloud avviste førespurnaden om " +
            "temperaturhistorikk.");
    }

    private ShellyWeatherStatisticsResponse DeserializeStatistics(
        string json)
    {
        using var document =
            JsonDocument.Parse(
                json);

        var root =
            document.RootElement;

        if (root.ValueKind is not JsonValueKind.Object)
        {
            throw new JsonException(
                "Shelly Cloud returnerte eit ugyldig " +
                "svar for temperaturhistorikk.");
        }

        /*
         * Shelly Cloud har minst to responsvariantar i bruk:
         * statistikkobjektet direkte på toppnivå, eller pakka inn
         * som { isok: true, data: { ... } }.
         */
        var payload =
            TryGetPropertyIgnoreCase(
                root,
                "data",
                out var dataElement) &&
            dataElement.ValueKind is JsonValueKind.Object
                ? dataElement
                : root;

        var statistics =
            payload.Deserialize<
                ShellyWeatherStatisticsResponse>(
                _jsonOptions)
            ?? throw new JsonException(
                "Shelly Cloud returnerte eit tomt eller " +
                "ugyldig svar for temperaturhistorikk.");

        if (statistics.History is null ||
            statistics.History.Count == 0)
        {
            _logger.LogWarning(
                "Shelly-statistikksvaret inneheld ingen " +
                "historikkpunkt. Rotfelt: {RootFields}. " +
                "Datafelt: {PayloadFields}.",
                GetPropertyNames(root),
                GetPropertyNames(payload));
        }

        return statistics;
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                value =
                    property.Value;

                return true;
            }
        }

        value =
            default;

        return false;
    }

    private static string GetPropertyNames(
        JsonElement element)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            return "<ikkje objekt>";
        }

        return string.Join(
            ", ",
            element
                .EnumerateObject()
                .Select(property =>
                    property.Name));
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

        if (string.IsNullOrWhiteSpace(
                _options.DisplayName))
        {
            throw new InvalidOperationException(
                "Shelly:DisplayName manglar.");
        }

        if (_httpClient.BaseAddress is null)
        {
            throw new InvalidOperationException(
                "BaseAddress manglar på Shelly-klienten.");
        }
    }

    private static string FormatShellyLocal(
        DateTimeOffset value)
    {
        return TimeZoneInfo
            .ConvertTime(
                value,
                NorwegianTimeZone)
            .ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture);
    }

    private static string Encode(
        string value)
    {
        return Uri.EscapeDataString(
            value);
    }
}

public sealed class ShellyHistoryUnavailableException : Exception
{
    public ShellyHistoryUnavailableException(
        string message)
        : base(message)
    {
    }
}
