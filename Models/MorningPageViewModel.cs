using Microsoft.AspNetCore.Mvc.Rendering;

namespace VigdalsMorningsguide.Models;

public sealed class MorningPageViewModel
{
    public MorningInputModel Input { get; set; } =
        new();

    public MorningResultModel? Result { get; set; }

    public MorningForecastModel? Forecast { get; set; }

    public ShellyMeasurementModel? ShellyMeasurement { get; set; }

    public string? ShellyStatusMessage { get; set; }

    public IReadOnlyList<SelectListItem> StationOptions { get; set; } =
        [];
}