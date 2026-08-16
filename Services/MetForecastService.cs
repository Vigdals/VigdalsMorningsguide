using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;
using VigdalsMorningsguide.Models;
using VigdalsMorningsguide.Options;

namespace VigdalsMorningsguide.Services
{
    public sealed class MetForecastService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<MetForecastService> _logger;
        private readonly MetForecastOptions _options;
        private readonly JsonSerializerOptions _jsonOptions;

        public MetForecastService(
        HttpClient httpClient,
        IOptions<MetForecastOptions> options,
        ILogger<MetForecastService> logger)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }

        public async Task<IReadOnlyList<ForecastTemperaturePoint>>
            GetTemperatureForecastAsync(
                WeatherStationModel station,
                CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(station);

            var requestUri = BuildRequestUri(station);

            _logger.LogInformation(
                "Hentar temperaturprognose for {SourceId} ({Name}). " +
                "Posisjon: {Latitude}, {Longitude}, høgd: {Altitude}.",
                station.SourceId,
                station.Name,
                station.Latitude,
                station.Longitude,
                station.MetresAboveSeaLevel);

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    requestUri);

            using var response =
                await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "MET Locationforecast returnerte HTTP {StatusCode}.",
                    (int)response.StatusCode);

                throw new HttpRequestException(
                    $"MET Locationforecast returnerte HTTP " +
                    $"{(int)response.StatusCode}.",
                    inner: null,
                    response.StatusCode);
            }

            var forecastResponse = JsonSerializer.Deserialize<MetForecastResponse>(
                    json,
                    _jsonOptions) ?? throw new JsonException("Kunne ikkje deserialisere MET Locationforecast JSON-respons.");

            return forecastResponse.Properties.TimeSeries.Where(point => point.Data.Instant.Details.AirTemperature.HasValue)
                .Select(point =>
                new ForecastTemperaturePoint
                {
                    Time =
                        point.Time,

                    Temperature =
                        point.Data.Instant.Details.AirTemperature!.Value
                }).OrderBy(point => point.Time).ToList();
        }

        private static string BuildRequestUri(
            WeatherStationModel station)
        {
            var latitude =
                station.Latitude.ToString(
                    "0.#####",
                    CultureInfo.InvariantCulture);

            var longitude =
                station.Longitude.ToString(
                    "0.#####",
                    CultureInfo.InvariantCulture);

            var requestUri =
                "compact" +
                $"?lat={Uri.EscapeDataString(latitude)}" +
                $"&lon={Uri.EscapeDataString(longitude)}";

            if (station.MetresAboveSeaLevel.HasValue)
            {
                var altitude =
                    (int)Math.Round(
                        station.MetresAboveSeaLevel.Value,
                        MidpointRounding.AwayFromZero);

                requestUri +=
                    $"&altitude={altitude}";
            }

            return requestUri;
        }

        private void ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(
                    _options.BaseUrl))
            {
                throw new InvalidOperationException(
                    "MetForecast:BaseUrl manglar.");
            }

            if (string.IsNullOrWhiteSpace(
                    _options.UserAgent))
            {
                throw new InvalidOperationException(
                    "MetForecast:UserAgent manglar.");
            }

            if (_httpClient.BaseAddress is null)
            {
                throw new InvalidOperationException(
                    "BaseAddress manglar på MET-klienten.");
            }
        }
    }
}
