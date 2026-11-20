using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VigdalsMorningsguide.Options;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Text.Json;
using VigdalsMorningsguide.Models;
using VigdalsMorningsguide.Services;

namespace VigdalsMorningsguide.Controllers;

public sealed class MorningController : Controller
{
    private static readonly TimeZoneInfo NorwegianTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    private readonly FrostService _frostService;
    private readonly FrostStationService _stationService;
    private readonly MetForecastService _metForecastService;
    private readonly MorningForecastService _morningForecastService;
    private readonly ShellyService _shellyService;
    private readonly ShellyHistoryService _shellyHistoryService;
    private readonly FrostWarningService _frostWarningService;
    private readonly DegreeDayCalculationService _degreeDayCalculationService;
    private readonly FrostOptions _frostOptions;
    private readonly ILogger<MorningController> _logger;

    public MorningController(
        FrostService frostService,
        FrostStationService stationService,
        MetForecastService metForecastService,
        MorningForecastService morningForecastService,
        ShellyService shellyService,
        ShellyHistoryService shellyHistoryService,
        FrostWarningService frostWarningService,
        DegreeDayCalculationService degreeDayCalculationService,
        IOptions<FrostOptions> frostOptions,
        ILogger<MorningController> logger)
    {
        _frostService =
            frostService;

        _stationService =
            stationService;

        _metForecastService =
            metForecastService;

        _morningForecastService =
            morningForecastService;

        _shellyService =
            shellyService;

        _shellyHistoryService =
            shellyHistoryService;

        _frostWarningService =
            frostWarningService;

        _degreeDayCalculationService = degreeDayCalculationService;
        _frostOptions = frostOptions.Value;

        _logger =
            logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var nowNorwegian =
            TimeZoneInfo.ConvertTime(
                DateTimeOffset.UtcNow,
                NorwegianTimeZone);

        var defaultTime =
            nowNorwegian.DateTime.AddDays(-2);

        var model =
            new MorningPageViewModel
            {
                Input =
                    new MorningInputModel
                    {
                        HungDate =
                            DateOnly.FromDateTime(
                                defaultTime),

                        HungTime =
                            TimeOnly.FromDateTime(
                                defaultTime),

                        SelectedSourceId =
                            TemperatureSourceCatalog
                                .ShellySourceId,

                        TargetDegreeDays =
                            60,

                        HasBeenRefrigerated =
                            false,

                        RefrigeratedFromDate =
                            null
                    }
            };

        PopulateTemperatureSourceOptions(
            model);

        try
        {
            if (TemperatureSourceCatalog.IsShelly(model.Input.SelectedSourceId))
            {
                await PopulateShellyMeasurementAsync(model, cancellationToken);
            }

            model.FrostWarning =
                await _frostWarningService.GetWarningAsync(
                    model.Input.SelectedSourceId,
                    model.ShellyMeasurement,
                    cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }

        return View(
            model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        MorningPageViewModel model,
        CancellationToken cancellationToken)
    {
        PopulateTemperatureSourceOptions(
            model);

        try
        {
            /*
             * Shelly er tilleggsinformasjon og blir henta
             * uavhengig av sjølve døgngradeutrekninga.
             *
             * Me gjer dette før valideringssjekken slik at
             * Shelly-status framleis kan visast dersom skjemaet
             * inneheld ugyldige verdiar.
             */
            if (TemperatureSourceCatalog.IsShelly(model.Input.SelectedSourceId))
            {
                await PopulateShellyMeasurementAsync(model, cancellationToken);
            }

            model.FrostWarning =
                await _frostWarningService.GetWarningAsync(
                    model.Input.SelectedSourceId,
                    model.ShellyMeasurement,
                    cancellationToken);

            /*
             * Kjøleskapsdatoen er berre obligatorisk dersom
             * brukaren har oppgitt at kjøtet er lagt i kjøleskap.
             *
             * Denne valideringa må derfor gjerast manuelt,
             * sidan RefrigeratedFromDate i modellen er nullable.
             */
            ValidateRefrigerationInput(
                model);

            var isRefrigerator = TemperatureSourceCatalog.IsRefrigerator(
                model.Input.SelectedSourceId);

            if (isRefrigerator || model.Input.HasBeenRefrigerated)
            {
                var temperature = model.Input.RefrigeratorTemperatureCelsius;
                if (temperature is null || !double.IsFinite(temperature.Value) ||
                    temperature <= 0 || temperature > 15)
                {
                    ModelState.AddModelError(
                        "Input.RefrigeratorTemperatureCelsius",
                        "Temperaturen må vere over 0 og høgst 15 °C.");
                }
            }

            if (!ModelState.IsValid)
            {
                return View(
                    model);
            }

            var hungAt =
                model.Input.GetHungAt();

            var refrigeratedAt =
                model.Input.GetRefrigeratedAt();

            // Servervaliderte verdi blir nytta av alle temperaturkjeldene.
            var refrigeratorTemperature =
                model.Input.RefrigeratorTemperatureCelsius ?? 4.0;

            WeatherStationModel? station =
                null;

            var usesShelly =
                TemperatureSourceCatalog.IsShelly(
                    model.Input.SelectedSourceId);

            if (isRefrigerator)
            {
                // Heile perioden i kjøleskap. Ingen eksterne temperaturkall.
                model.Result = _degreeDayCalculationService.Calculate(
                    hungAt,
                    model.Input.TargetDegreeDays,
                    new TemperatureSourceModel
                    {
                        SourceId = TemperatureSourceCatalog.RefrigeratorSourceId,
                        Name = "Kjøleskap",
                    },
                    hungAt,
                    [],
                    measurementIntervalMinutes: 10,
                    minimumCoveragePercent: 100,
                    maximumDaysBack: _frostOptions.MaximumDaysBack,
                    refrigeratorTemperatureCelsius:
                        refrigeratorTemperature);
            }
            else if (usesShelly)
            {
                model.Result =
                    await _shellyHistoryService.CalculateAsync(
                        hungAt,
                        model.Input.TargetDegreeDays,
                        refrigeratedAt,
                        cancellationToken,
                        refrigeratorTemperature);
            }
            else
            {
                var selectedStation =
                    WeatherStationCatalog.Find(
                        model.Input.SelectedSourceId);

                if (selectedStation is null)
                {
                    ModelState.AddModelError(
                        "Input.SelectedSourceId",
                        "Den valde temperaturkjelda finst ikkje.");

                    return View(
                        model);
                }

                station =
                    await _stationService.ResolveStationAsync(
                        selectedStation,
                        hungAt,
                        cancellationToken);

                if (station is null)
                {
                    ModelState.AddModelError(
                        "Input.SelectedSourceId",
                        "Målestasjonen har ikkje gyldige " +
                        "temperaturmålingar for perioden.");

                    return View(
                        model);
                }

                model.Result =
                    await _frostService.CalculateAsync(
                        hungAt,
                        model.Input.TargetDegreeDays,
                        station,
                        refrigeratedAt,
                        cancellationToken,
                        refrigeratorTemperature);
            }

            if (!model.Result.TargetReached)
            {
                /*
                 * Når kjøtet ligg i kjøleskap kjenner me
                 * temperaturen framover og skal derfor ikkje
                 * bruke MET-prognosen.
                 *
                 * I staden reknar me vidare med den faste
                 * kjøleskapstemperaturen.
                 */
                if (model.Result.RefrigeratorTemperatureCelsius
                    is double refrigeratorTemperature)
                {
                    model.Forecast =
                        _morningForecastService
                            .CalculateAtConstantTemperature(
                                model.Result,
                                refrigeratorTemperature);
                }
                else if (usesShelly)
                {
                    PopulateShellyForecast(
                        model);
                }
                else
                {
                    await PopulateForecastAsync(
                        model,
                        station!,
                        cancellationToken);
                }
            }

            return View(
                model);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (OperationCanceledException exception)
        {
            if (TemperatureSourceCatalog.IsShelly(
                    model.Input.SelectedSourceId))
            {
                _logger.LogError(
                    "Tidsavbrot ved henting av " +
                    "temperaturdata frå Shelly.");
            }
            else
            {
                _logger.LogError(
                    exception,
                    "Tidsavbrot ved henting av temperaturdata frå " +
                    "{SelectedSourceId}.",
                    TemperatureSourceCatalog.GetSafeSourceIdForLogging(
                        model.Input.SelectedSourceId));
            }

            ModelState.AddModelError(
                string.Empty,
                "Temperaturkjelda brukte for lang tid på å svare.");

            return View(
                model);
        }
        catch (ArgumentException exception)
        {
            _logger.LogWarning(
                exception,
                "Ugyldige verdiar i mørningsskjemaet.");

            ModelState.AddModelError(
                string.Empty,
                exception.Message);

            return View(
                model);
        }
        catch (ShellyHistoryUnavailableException exception)
        {
            _logger.LogWarning(
                exception,
                "Shelly manglar temperaturhistorikk for perioden.");

            ModelState.AddModelError(
                "Input.SelectedSourceId",
                exception.Message);

            return View(
                model);
        }
        catch (HttpRequestException exception)
        {
            if (TemperatureSourceCatalog.IsShelly(
                    model.Input.SelectedSourceId))
            {
                /*
                 * Shelly brukar auth_key i URL-en. Me loggar
                 * derfor ikkje HttpRequestException-objektet,
                 * sidan enkelte transportfeil kan innehalde URI.
                 */
                _logger.LogError(
                    "Klarte ikkje å hente temperaturdata frå " +
                    "Shelly. HTTP-status: {StatusCode}.",
                    exception.StatusCode is null
                        ? "ukjend"
                        : ((int)exception.StatusCode.Value).ToString());
            }
            else
            {
                _logger.LogError(
                    exception,
                    "Klarte ikkje å hente temperaturdata frå " +
                    "{SelectedSourceId}.",
                    TemperatureSourceCatalog.GetSafeSourceIdForLogging(
                        model.Input.SelectedSourceId));
            }

            ModelState.AddModelError(
                string.Empty,
                "Klarte ikkje å hente temperaturdata frå " +
                "den valde kjelda.");

            return View(
                model);
        }
        catch (JsonException exception)
        {
            _logger.LogError(
                exception,
                "Klarte ikkje å tolke temperaturdata frå " +
                "{SelectedSourceId}.",
                TemperatureSourceCatalog.GetSafeSourceIdForLogging(
                    model.Input.SelectedSourceId));

            ModelState.AddModelError(
                string.Empty,
                "Temperaturkjelda returnerte data som ikkje " +
                "kunne tolkast.");

            return View(
                model);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Uventa feil ved utrekning av døgngrader.");

            ModelState.AddModelError(
                string.Empty,
                "Det oppstod ein uventa feil.");

            return View(
                model);
        }
    }

    private async Task PopulateShellyMeasurementAsync(
        MorningPageViewModel model,
        CancellationToken cancellationToken)
    {
        try
        {
            model.ShellyMeasurement =
                await _shellyService
                    .GetCurrentMeasurementAsync(
                        cancellationToken);

            if (model.ShellyMeasurement is null)
            {
                model.ShellyStatusMessage =
                    "Shelly har ikkje ei gyldig temperatur- " +
                    "eller luftfuktmåling akkurat no.";
            }
            else
            {
                model.ShellyStatusMessage =
                    null;
            }
        }
        catch (HttpRequestException exception)
        {
            /*
             * Shelly er eit tillegg.
             *
             * Feil mot Shelly Cloud skal ikkje hindre
             * brukaren i å bruke mørningskalkulatoren.
             */
            _logger.LogWarning(
                "Klarte ikkje å hente måling frå Shelly Cloud. " +
                "HTTP-status: {StatusCode}.",
                exception.StatusCode is null
                    ? "ukjend"
                    : ((int)exception.StatusCode.Value).ToString());

            model.ShellyStatusMessage =
                "Klarte ikkje å hente siste måling frå " +
                "Shelly akkurat no.";
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(
                exception,
                "Klarte ikkje å tolke Shelly-responsen.");

            model.ShellyStatusMessage =
                "Shelly returnerte ei måling som ikkje " +
                "kunne tolkast.";
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            /*
             * Dette er typisk timeout frå HttpClient,
             * ikkje at brukaren avbraut HTTP-requesten.
             */
            _logger.LogWarning(
                "Tidsavbrot ved kall mot Shelly Cloud.");

            model.ShellyStatusMessage =
                "Shelly Cloud brukte for lang tid på å svare.";
        }
    }

    private async Task PopulateForecastAsync(
        MorningPageViewModel model,
        WeatherStationModel station,
        CancellationToken cancellationToken)
    {
        if (model.Result is null ||
            model.Result.TargetReached)
        {
            return;
        }

        try
        {
            var forecastPoints =
                await _metForecastService
                    .GetTemperatureForecastAsync(
                        station,
                        cancellationToken);

            model.Forecast =
                _morningForecastService.Calculate(
                    model.Result,
                    forecastPoints);
        }
        catch (HttpRequestException exception)
        {
            /*
             * Prognosen er eit tillegg.
             *
             * Feil hos Locationforecast skal ikkje gjere
             * at brukaren mistar den gyldige Frost-utrekninga.
             */
            _logger.LogWarning(
                exception,
                "Klarte ikkje å hente " +
                "temperaturprognose frå MET.");
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(
                exception,
                "Klarte ikkje å tolke " +
                "temperaturprognosen frå MET.");
        }
    }

    private void PopulateShellyForecast(
        MorningPageViewModel model)
    {
        if (model.Result is null ||
            model.Result.TargetReached ||
            model.ShellyMeasurement is not { IsStale: false } measurement ||
            measurement.TemperatureCelsius is not double temperature ||
            !double.IsFinite(temperature) ||
            temperature <= 0)
        {
            return;
        }

        model.Forecast =
            _morningForecastService
                .CalculateAtConstantTemperature(
                    model.Result,
                    temperature);

        if (model.Forecast is not null)
        {
            model.ShellyForecastTemperatureCelsius =
                temperature;
        }
    }

    private void ValidateRefrigerationInput(
        MorningPageViewModel model)
    {
        // Eiga kjøleskapkjelde dekkjer heile perioden og treng ikkje overgangsdato.
        if (TemperatureSourceCatalog.IsRefrigerator(model.Input.SelectedSourceId))
        {
            model.Input.HasBeenRefrigerated = false;
            model.Input.RefrigeratedFromDate = null;
            model.Input.RefrigeratedFromTime = null;
            ModelState.Remove("Input.RefrigeratedFromDate");
            ModelState.Remove("Input.RefrigeratedFromTime");
            return;
        }

        /*
         * Dersom brukaren ikkje har kryssa av for kjøleskap,
         * skal ein eventuell gammal verdi frå skjemaet ikkje
         * få påverke utrekninga.
         */
        if (!model.Input.HasBeenRefrigerated)
        {
            model.Input.RefrigeratedFromDate =
                null;

            model.Input.RefrigeratedFromTime =
                null;

            return;
        }

        if (!model.Input.RefrigeratedFromDate.HasValue)
        {
            ModelState.AddModelError(
                "Input.RefrigeratedFromDate",
                "Vel datoen kjøtet vart lagt i kjøleskap.");

        }

        if (!model.Input.RefrigeratedFromTime.HasValue)
        {
            ModelState.AddModelError(
                "Input.RefrigeratedFromTime",
                "Vel klokkeslettet kjøtet vart lagt i kjøleskap.");
        }

        if (!model.Input.RefrigeratedFromDate.HasValue ||
            !model.Input.RefrigeratedFromTime.HasValue)
        {
            return;
        }

        var refrigeratedAt =
            DateTime.SpecifyKind(
                model.Input.RefrigeratedFromDate.Value.ToDateTime(
                    model.Input.RefrigeratedFromTime.Value),
                DateTimeKind.Unspecified);

        var hungAt =
            model.Input.GetHungAt();

        var nowNorwegian =
            TimeZoneInfo.ConvertTime(
                DateTimeOffset.UtcNow,
                NorwegianTimeZone);

        var nowLocal =
            DateTime.SpecifyKind(
                nowNorwegian.DateTime,
                DateTimeKind.Unspecified);

        if (refrigeratedAt < hungAt)
        {
            ModelState.AddModelError(
                "Input.RefrigeratedFromTime",
                "Kjøleskapstidspunktet kan ikkje vere før " +
                "opphengstidspunktet.");
        }

        if (refrigeratedAt > nowLocal)
        {
            ModelState.AddModelError(
                "Input.RefrigeratedFromTime",
                "Kjøleskapstidspunktet kan ikkje vere fram i tid.");
        }

        if (NorwegianTimeZone.IsInvalidTime(
                refrigeratedAt))
        {
            ModelState.AddModelError(
                "Input.RefrigeratedFromTime",
                "Tidspunktet finst ikkje på grunn av overgang " +
                "til sommartid.");
        }

        if (NorwegianTimeZone.IsAmbiguousTime(
                refrigeratedAt))
        {
            ModelState.AddModelError(
                "Input.RefrigeratedFromTime",
                "Tidspunktet er tvitydig på grunn av overgang " +
                "frå sommartid. Vel eit anna klokkeslett.");
        }
    }
    private void PopulateTemperatureSourceOptions(
        MorningPageViewModel model)
    {
        var localGroup =
            new SelectListGroup
            {
                Name = "Lokal målar"
            };

        var frostGroup =
            new SelectListGroup
            {
                Name = "Meteorologiske målestasjonar"
            };

        var stationOptions =
            WeatherStationCatalog.Stations
                .Select(
                    station =>
                        new SelectListItem
                        {
                            Value =
                                station.SourceId,

                            Text =
                                station.DisplayName,

                            Group =
                                frostGroup
                        })
                .ToList();

        stationOptions.Insert(
            0,
            new SelectListItem
            {
                Value =
                    TemperatureSourceCatalog.ShellySourceId,

                Text =
                    _shellyHistoryService.DisplayName,

                Group =
                    localGroup
            });

        stationOptions.Insert(
            1,
            new SelectListItem
            {
                Value = TemperatureSourceCatalog.RefrigeratorSourceId,
                Text = "Kjøleskap (vel temperatur)",
                Group = localGroup
            });

        model.TemperatureSourceOptions =
            stationOptions;
    }

    [HttpGet]
    public async Task<IActionResult> FrostWarning(
        string? sourceId,
        CancellationToken cancellationToken)
    {
        Response.Headers["Cache-Control"] = "no-store";

        if (!TemperatureSourceCatalog.IsShelly(sourceId) &&
            !TemperatureSourceCatalog.IsRefrigerator(sourceId) &&
            WeatherStationCatalog.Find(sourceId) is null)
        {
            return BadRequest();
        }

        ShellyMeasurementModel? measurement = null;

        if (TemperatureSourceCatalog.IsShelly(sourceId))
        {
            try
            {
                measurement = await _shellyService.GetCurrentMeasurementAsync(
                    cancellationToken);
            }
            catch (HttpRequestException) { }
            catch (JsonException) { }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            { }
        }

        var warning = await _frostWarningService.GetWarningAsync(
            sourceId, measurement, cancellationToken);

        return PartialView("_FrostWarning", warning);
    }

    [HttpGet]
    public IActionResult Guide()
    {
        return View();
    }
}
