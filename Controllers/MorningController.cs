using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using VigdalsMorningsguide.Models;
using VigdalsMorningsguide.Services;
using System.Text.Json;

namespace VigdalsMorningsguide.Controllers;

public sealed class MorningController : Controller
{
    private readonly FrostService _frostService;
    private readonly FrostStationService _stationService;
    private readonly MetForecastService _metForecastService;
    private readonly MorningForecastService _morningForecastService;
    private readonly ILogger<MorningController> _logger;

    public MorningController(
        FrostService frostService,
        FrostStationService stationService,
        MetForecastService metForecastService,
        MorningForecastService morningForecastService,
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

        _logger =
            logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var defaultTime =
            DateTime.Now.AddDays(-2);

        var model =
            new MorningPageViewModel
            {
                Input = new MorningInputModel
                {
                    HungDate =
                        DateOnly.FromDateTime(defaultTime),

                    HungTime =
                        TimeOnly.FromDateTime(defaultTime),

                    SelectedSourceId =
                        WeatherStationCatalog.DefaultSourceId,

                    TargetDegreeDays =
                        80
                }
            };

        PopulateStationOptions(model);

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        MorningPageViewModel model,
        CancellationToken cancellationToken)
    {
        PopulateStationOptions(model);

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var selectedStation =
                WeatherStationCatalog.Find(
                    model.Input.SelectedSourceId);

            if (selectedStation is null)
            {
                ModelState.AddModelError(
                    "Input.SelectedSourceId",
                    "Den valde målestasjonen finst ikkje.");

                return View(model);
            }

            var hungAt =
                model.Input.GetHungAt();

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

                return View(model);
            }

            model.Result =
                await _frostService.CalculateAsync(
                    hungAt,
                    model.Input.TargetDegreeDays,
                    station,
                    cancellationToken);

            if (!model.Result.TargetReached)
            {
                await PopulateForecastAsync(
                    model,
                    station,
                    cancellationToken);
            }

            return View(model);
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

            return View(model);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(
                exception,
                "Klarte ikkje å hente data frå Frost.");

            ModelState.AddModelError(
                string.Empty,
                "Klarte ikkje å hente temperaturdata frå Frost.");

            return View(model);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Uventa feil ved utrekning av døgngrader.");

            ModelState.AddModelError(
                string.Empty,
                "Det oppstod ein uventa feil.");

            return View(model);
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
             * Feil hos Locationforecast skal ikkje gjere at
             * brukaren mistar den gyldige Frost-utrekninga.
             */
            _logger.LogWarning(
                exception,
                "Klarte ikkje å hente temperaturprognose frå MET.");
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(
                exception,
                "Klarte ikkje å tolke temperaturprognosen frå MET.");
        }
    }

    private static void PopulateStationOptions(
        MorningPageViewModel model)
    {
        model.StationOptions =
            WeatherStationCatalog.Stations
                .Select(station =>
                    new SelectListItem
                    {
                        Value = station.SourceId,
                        Text = station.DisplayName
                    })
                .ToList();
    }
}