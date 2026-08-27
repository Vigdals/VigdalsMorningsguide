using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace VigdalsMorningsguide.Models;

public sealed class MorningPageViewModel
{
    public MorningInputModel Input { get; set; } =
        new();

    [BindNever]
    public MorningResultModel? Result { get; set; }

    [BindNever]
    public MorningForecastModel? Forecast { get; set; }

    [BindNever]
    public ShellyMeasurementModel? ShellyMeasurement { get; set; }

    [BindNever]
    public string? ShellyStatusMessage { get; set; }

    [BindNever]
    public double? ShellyForecastTemperatureCelsius { get; set; }

    [BindNever]
    public IReadOnlyList<SelectListItem> TemperatureSourceOptions { get; set; } =
        [];
}
