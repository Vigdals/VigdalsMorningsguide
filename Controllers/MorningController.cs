using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
    private readonly ILogger<MorningController> _logger;

    public MorningController(
        FrostService frostService,
        FrostStationService stationService,
        MetForecastService metForecastService,
        MorningForecastService morningForecastService,
        ShellyService shellyService,
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
                            WeatherStationCatalog
                                .DefaultSourceId,

                        TargetDegreeDays =
                            80,

                        HasBeenRefrigerated =
                            false,

                        RefrigeratedFromDate =
                            null
                    }
            };

        PopulateStationOptions(
            model);

        try
        {
            await PopulateShellyMeasurementAsync(
                model,
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
        PopulateStationOptions(
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
            await PopulateShellyMeasurementAsync(
                model,
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

            if (!ModelState.IsValid)
            {
                return View(
                    model);
            }

            var selectedStation =
                WeatherStationCatalog.Find(
                    model.Input.SelectedSourceId);

            if (selectedStation is null)
            {
                ModelState.AddModelError(
                    "Input.SelectedSourceId",
                    "Den valde målestasjonen finst ikkje.");

                return View(
                    model);
            }

            var hungAt =
                model.Input.GetHungAt();

            var refrigeratedAt =
                model.Input.GetRefrigeratedAt();

            var station =
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

            /*
             * FrostService brukar målte temperaturar fram til
             * refrigeratedAt.
             *
             * Dersom refrigeratedAt er sett, blir temperaturen
             * rekna som fast 4 °C frå dette tidspunktet.
             */
            model.Result =
                await _frostService.CalculateAsync(
                    hungAt,
                    model.Input.TargetDegreeDays,
                    station,
                    refrigeratedAt,
                    cancellationToken);

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
                else
                {
                    await PopulateForecastAsync(
                        model,
                        station,
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
        catch (HttpRequestException exception)
        {
            /*
             * HttpRequestException frå MET og Shelly blir
             * handtert i dei respektive hjelpefunksjonane.
             *
             * Ei feil som kjem heilt hit er derfor normalt
             * frå Frost-kjeda.
             */
            _logger.LogError(
                exception,
                "Klarte ikkje å hente data frå Frost.");

            ModelState.AddModelError(
                string.Empty,
                "Klarte ikkje å hente temperaturdata frå Frost.");

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
                exception,
                "Klarte ikkje å hente måling frå Shelly Cloud.");

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
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            /*
             * Dette er typisk timeout frå HttpClient,
             * ikkje at brukaren avbraut HTTP-requesten.
             */
            _logger.LogWarning(
                exception,
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

    private void ValidateRefrigerationInput(
        MorningPageViewModel model)
    {
        /*
         * Dersom brukaren ikkje har kryssa av for kjøleskap,
         * skal ein eventuell gammal verdi frå skjemaet ikkje
         * få påverke utrekninga.
         */
        if (!model.Input.HasBeenRefrigerated)
        {
            model.Input.RefrigeratedFromDate =
                null;

            return;
        }

        if (!model.Input.RefrigeratedFromDate.HasValue)
        {
            ModelState.AddModelError(
                "Input.RefrigeratedFromDate",
                "Vel datoen kjøtet vart lagt i kjøleskap.");

            return;
        }

        if (model.Input.RefrigeratedFromDate.Value <
            model.Input.HungDate)
        {
            ModelState.AddModelError(
                "Input.RefrigeratedFromDate",
                "Kjøleskapsdatoen kan ikkje vere før " +
                "opphengsdatoen.");
        }

        var nowNorwegian =
            TimeZoneInfo.ConvertTime(
                DateTimeOffset.UtcNow,
                NorwegianTimeZone);

        var today =
            DateOnly.FromDateTime(
                nowNorwegian.DateTime);

        if (model.Input.RefrigeratedFromDate.Value >
            today)
        {
            ModelState.AddModelError(
                "Input.RefrigeratedFromDate",
                "Kjøleskapsdatoen kan ikkje vere fram i tid.");
        }
    }
    private static void PopulateStationOptions(
        MorningPageViewModel model)
    {
        model.StationOptions =
            WeatherStationCatalog.Stations
                .Select(
                    station =>
                        new SelectListItem
                        {
                            Value =
                                station.SourceId,

                            Text =
                                station.DisplayName
                        })
                .ToList();
    }
}