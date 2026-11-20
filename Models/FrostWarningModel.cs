namespace VigdalsMorningsguide.Models;

/// <summary>
/// Eit null-felt tyder at denne kjelda ikkje har påvist minusgrader.
/// </summary>
public sealed class FrostWarningModel
{
    public double? CurrentTemperatureCelsius { get; init; }
    public DateTimeOffset? FirstForecastBelowZeroAtUtc { get; init; }
    public double? MinimumForecastTemperatureCelsius { get; init; }

    public string? ForecastLocationName { get; init; }

    public bool ForecastIsNearbyProxy { get; init; }
}
