using Microsoft.AspNetCore.Mvc;
using VigdalsMorningsguide.Models;
using VigdalsMorningsguide.Services;

namespace VigdalsMorningsguide.Controllers;

public sealed class MorningController : Controller
{
    private readonly FrostService _frostService;
    private readonly ILogger<MorningController> _logger;

    public MorningController(
        FrostService frostService,
        ILogger<MorningController> logger)
    {
        _frostService = frostService;
        _logger = logger;
    }

    [HttpGet]
    [HttpGet]
    public IActionResult Index()
    {
        var defaultTime = DateTime.Now.AddDays(-2);

        return View(
            new MorningPageViewModel
            {
                Input = new MorningInputModel
                {
                    HungDate = DateOnly.FromDateTime(defaultTime),
                    HungTime = new TimeOnly(
                        defaultTime.Hour,
                        defaultTime.Minute),
                    TargetDegreeDays = 80
                }
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        MorningPageViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var hungAt = model.Input.GetHungAt();

            model.Result = await _frostService.CalculateAsync(
                hungAt,
                model.Input.TargetDegreeDays,
                cancellationToken);

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
                "Ugyldig opphengstidspunkt.");

            ModelState.AddModelError(
                string.Empty,
                exception.Message);

            return View(model);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(
                exception,
                "Klarte ikkje å hente temperaturdata frå Frost.");

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
}