using VigdalsMorningsguide.Models;

namespace VigdalsMorningsguide.Services;

public sealed class MorningForecastService
{
    private static readonly TimeZoneInfo NorwegianTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById(
            "Europe/Oslo");

    public MorningForecastModel? CalculateAtConstantTemperature(
        MorningResultModel result,
        double temperatureCelsius)
    {
        ArgumentNullException.ThrowIfNull(
            result);

        if (!double.IsFinite(
                temperatureCelsius) ||
            temperatureCelsius <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(temperatureCelsius),
                "Temperaturen må vere større enn 0 °C.");
        }

        if (result.TargetReached ||
            result.RemainingDegreeDays <= 0)
        {
            return null;
        }

        /*
         * Døgngrader =
         * temperatur × timar / 24.
         *
         * Dermed:
         * timar =
         * resterande døgngrader / temperatur × 24.
         */
        var hoursNeeded =
            result.RemainingDegreeDays /
            temperatureCelsius *
            24.0;

        var projectionFromUtc =
            result.CalculatedAtUtc
                .ToUniversalTime();

        var estimatedTargetUtc =
            projectionFromUtc.AddHours(
                hoursNeeded);

        var projectionFromLocal =
            TimeZoneInfo.ConvertTime(
                projectionFromUtc,
                NorwegianTimeZone);

        var estimatedTargetLocal =
            TimeZoneInfo.ConvertTime(
                estimatedTargetUtc,
                NorwegianTimeZone);

        return new MorningForecastModel
        {
            ProjectionFrom =
                projectionFromLocal,

            ProjectionTo =
                estimatedTargetLocal,

            EstimatedTargetAt =
                estimatedTargetLocal,

            StartingDegreeDays =
                result.TotalDegreeDays,

            ForecastDegreeDays =
                result.RemainingDegreeDays,

            TargetDegreeDays =
                result.TargetDegreeDays,

            ForecastPointCount =
                0
        };
    }

    public MorningForecastModel? Calculate(
        MorningResultModel result,
        IReadOnlyList<ForecastTemperaturePoint> forecastPoints)
    {
        ArgumentNullException.ThrowIfNull(
            result);

        ArgumentNullException.ThrowIfNull(
            forecastPoints);

        if (result.TargetReached ||
            result.RemainingDegreeDays <= 0)
        {
            return null;
        }

        var points =
            forecastPoints
                .Where(point =>
                    double.IsFinite(
                        point.Temperature))
                .GroupBy(point =>
                    point.Time.ToUniversalTime())
                .Select(group =>
                    group.First())
                .OrderBy(point =>
                    point.Time.UtcDateTime)
                .ToList();

        if (points.Count < 2)
        {
            return null;
        }

        var calculatedAtUtc =
            result.CalculatedAtUtc
                .ToUniversalTime();

        var remainingDegreeDays =
            result.RemainingDegreeDays;

        var forecastDegreeDays =
            0.0;

        DateTimeOffset? projectionFromUtc =
            null;

        DateTimeOffset? projectionToUtc =
            null;

        for (
            var index = 0;
            index < points.Count - 1;
            index++)
        {
            var current =
                points[index];

            var next =
                points[index + 1];

            var currentTimeUtc =
                current.Time
                    .ToUniversalTime();

            var nextTimeUtc =
                next.Time
                    .ToUniversalTime();

            if (nextTimeUtc <= currentTimeUtc)
            {
                continue;
            }

            /*
             * Prognosepunkt som ligg heilt før tidspunktet
             * Frost-utrekninga sluttar på, skal ikkje teljast.
             */
            if (nextTimeUtc <= calculatedAtUtc)
            {
                continue;
            }

            var intervalStartUtc =
                currentTimeUtc > calculatedAtUtc
                    ? currentTimeUtc
                    : calculatedAtUtc;

            var intervalEndUtc =
                nextTimeUtc;

            if (intervalEndUtc <= intervalStartUtc)
            {
                continue;
            }

            projectionFromUtc ??=
                intervalStartUtc;

            projectionToUtc =
                intervalEndUtc;

            /*
             * Same prinsipp som i FrostService:
             * negative temperaturar gir ikkje positive
             * døgngrader.
             */
            var temperature =
                Math.Max(
                    0,
                    current.Temperature);

            if (temperature <= 0)
            {
                continue;
            }

            var intervalHours =
                (intervalEndUtc -
                 intervalStartUtc)
                .TotalHours;

            var intervalDegreeDays =
                temperature *
                intervalHours /
                24.0;

            /*
             * Dersom målet blir nådd inne i dette intervallet,
             * reknar me ut nøyaktig kor langt inn i intervallet
             * det skjer.
             */
            if (intervalDegreeDays >=
                remainingDegreeDays)
            {
                var hoursNeeded =
                    remainingDegreeDays /
                    temperature *
                    24.0;

                var estimatedTargetUtc =
                    intervalStartUtc.AddHours(
                        hoursNeeded);

                forecastDegreeDays +=
                    remainingDegreeDays;

                var estimatedTargetLocal =
                    TimeZoneInfo.ConvertTime(
                        estimatedTargetUtc,
                        NorwegianTimeZone);

                return new MorningForecastModel
                {
                    ProjectionFrom =
                        TimeZoneInfo.ConvertTime(
                            projectionFromUtc.Value,
                            NorwegianTimeZone),

                    ProjectionTo =
                        estimatedTargetLocal,

                    EstimatedTargetAt =
                        estimatedTargetLocal,

                    StartingDegreeDays =
                        result.TotalDegreeDays,

                    ForecastDegreeDays =
                        forecastDegreeDays,

                    TargetDegreeDays =
                        result.TargetDegreeDays,

                    ForecastPointCount =
                        points.Count
                };
            }

            forecastDegreeDays +=
                intervalDegreeDays;

            remainingDegreeDays -=
                intervalDegreeDays;
        }

        if (!projectionFromUtc.HasValue ||
            !projectionToUtc.HasValue)
        {
            return null;
        }

        /*
         * Prognosen tok slutt før me nådde målet.
         * Det er viktig å ikkje dikte vidare etter siste
         * prognosepunkt.
         */
        return new MorningForecastModel
        {
            ProjectionFrom =
                TimeZoneInfo.ConvertTime(
                    projectionFromUtc.Value,
                    NorwegianTimeZone),

            ProjectionTo =
                TimeZoneInfo.ConvertTime(
                    projectionToUtc.Value,
                    NorwegianTimeZone),

            EstimatedTargetAt =
                null,

            StartingDegreeDays =
                result.TotalDegreeDays,

            ForecastDegreeDays =
                forecastDegreeDays,

            TargetDegreeDays =
                result.TargetDegreeDays,

            ForecastPointCount =
                points.Count
        };
    }
}