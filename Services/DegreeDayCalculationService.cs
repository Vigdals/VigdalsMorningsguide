using VigdalsMorningsguide.Models;

namespace VigdalsMorningsguide.Services;

public sealed class DegreeDayCalculationService
{
    private const double RefrigeratorTemperatureCelsius =
        4.0;

    private static readonly TimeZoneInfo NorwegianTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById(
            "Europe/Oslo");

    public MorningResultModel Calculate(
        DateTime hungAt,
        double targetDegreeDays,
        TemperatureSourceModel source,
        DateTime? refrigeratedAt,
        IReadOnlyList<TemperatureMeasurementModel> measurements,
        int measurementIntervalMinutes,
        int maximumAcceptedGapMinutes,
        double minimumCoveragePercent,
        int maximumDaysBack,
        DateTimeOffset? calculatedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(
            source);

        ArgumentNullException.ThrowIfNull(
            measurements);

        ValidateSettings(
            measurementIntervalMinutes,
            maximumAcceptedGapMinutes,
            minimumCoveragePercent,
            maximumDaysBack);

        ValidateSource(
            source);

        var nowUtc =
            (calculatedAtUtc ?? DateTimeOffset.UtcNow)
                .ToUniversalTime();

        ValidateRequest(
            hungAt,
            targetDegreeDays,
            refrigeratedAt,
            maximumDaysBack,
            nowUtc);

        var hungAtLocal =
            DateTime.SpecifyKind(
                hungAt,
                DateTimeKind.Unspecified);

        var nowLocalOffset =
            TimeZoneInfo.ConvertTime(
                nowUtc,
                NorwegianTimeZone);

        var nowLocal =
            DateTime.SpecifyKind(
                nowLocalOffset.DateTime,
                DateTimeKind.Unspecified);

        DateTime? refrigeratedAtLocal =
            null;

        if (refrigeratedAt.HasValue)
        {
            refrigeratedAtLocal =
                DateTime.SpecifyKind(
                    refrigeratedAt.Value,
                    DateTimeKind.Unspecified);

        }

        var hungAtUtc =
            ConvertLocalToUtc(
                hungAtLocal);

        var orderedMeasurements =
            measurements
                .Where(measurement =>
                    double.IsFinite(
                        measurement.Temperature))
                .GroupBy(measurement =>
                    measurement.UtcTimestamp
                        .ToUniversalTime())
                .Select(group =>
                    group.First() with
                    {
                        UtcTimestamp =
                            group.Key
                    })
                .OrderBy(measurement =>
                    measurement.UtcTimestamp)
                .ToList();

        return BuildResult(
            orderedMeasurements,
            hungAtLocal,
            nowLocal,
            hungAtUtc,
            nowUtc,
            targetDegreeDays,
            source,
            refrigeratedAtLocal,
            measurementIntervalMinutes,
            maximumAcceptedGapMinutes,
            minimumCoveragePercent);
    }

    private static MorningResultModel BuildResult(
        IReadOnlyList<TemperatureMeasurementModel> measurements,
        DateTime hungAtLocal,
        DateTime calculatedAtLocal,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        double targetDegreeDays,
        TemperatureSourceModel source,
        DateTime? refrigeratedAtLocal,
        int measurementIntervalMinutes,
        int maximumAcceptedGapMinutes,
        double minimumCoveragePercent)
    {
        var dailyResults =
            new List<MorningDayModel>();

        var accumulatedDegreeDays =
            0.0;

        var includedWeightedTemperatureHours =
            0.0;

        var includedCoveredHours =
            0.0;

        var totalCoveredHours =
            0.0;

        var firstDate =
            DateOnly.FromDateTime(
                hungAtLocal);

        var lastDate =
            DateOnly.FromDateTime(
                calculatedAtLocal);

        for (
            var date = firstDate;
            date <= lastDate;
            date = date.AddDays(1))
        {
            var dayStartLocal =
                DateTime.SpecifyKind(
                    date.ToDateTime(
                        TimeOnly.MinValue),
                    DateTimeKind.Unspecified);

            var nextDayStartLocal =
                DateTime.SpecifyKind(
                    date
                        .AddDays(1)
                        .ToDateTime(
                            TimeOnly.MinValue),
                    DateTimeKind.Unspecified);

            var segmentStartLocal =
                hungAtLocal > dayStartLocal
                    ? hungAtLocal
                    : dayStartLocal;

            var segmentEndLocal =
                calculatedAtLocal < nextDayStartLocal
                    ? calculatedAtLocal
                    : nextDayStartLocal;

            if (segmentEndLocal <= segmentStartLocal)
            {
                continue;
            }

            var segmentStartUtc =
                ConvertLocalToUtc(
                    segmentStartLocal);

            var segmentEndUtc =
                ConvertLocalToUtc(
                    segmentEndLocal);

            var segmentDurationHours =
                (segmentEndUtc - segmentStartUtc)
                .TotalHours;

            var outdoorStartLocal =
                segmentStartLocal;

            var outdoorEndLocal =
                segmentEndLocal;

            if (refrigeratedAtLocal.HasValue &&
                refrigeratedAtLocal.Value < outdoorEndLocal)
            {
                outdoorEndLocal =
                    refrigeratedAtLocal.Value;
            }

            if (outdoorEndLocal < outdoorStartLocal)
            {
                outdoorEndLocal =
                    outdoorStartLocal;
            }

            DateTime? refrigeratorStartLocal =
                null;

            if (refrigeratedAtLocal.HasValue &&
                refrigeratedAtLocal.Value < segmentEndLocal)
            {
                refrigeratorStartLocal =
                    refrigeratedAtLocal.Value > segmentStartLocal
                        ? refrigeratedAtLocal.Value
                        : segmentStartLocal;
            }

            var outdoorDegreeDays =
                0.0;

            var outdoorWeightedTemperatureHours =
                0.0;

            var outdoorCoveredHours =
                0.0;

            var outdoorDurationHours =
                0.0;

            var outdoorIncluded =
                true;

            var observationCount =
                0;

            var expectedObservationCount =
                0;

            var qualityCodes =
                new List<int>();

            if (outdoorEndLocal > outdoorStartLocal)
            {
                var outdoorStartUtc =
                    ConvertLocalToUtc(
                        outdoorStartLocal);

                var outdoorEndUtc =
                    ConvertLocalToUtc(
                        outdoorEndLocal);

                outdoorDurationHours =
                    (outdoorEndUtc - outdoorStartUtc)
                    .TotalHours;

                var integration =
                    IntegrateMeasurements(
                        measurements,
                        outdoorStartUtc,
                        outdoorEndUtc,
                        measurementIntervalMinutes,
                        maximumAcceptedGapMinutes);

                var outdoorCoveragePercent =
                    outdoorDurationHours <= 0
                        ? 0
                        : Math.Min(
                            100,
                            integration.CoveredHours /
                            outdoorDurationHours *
                            100);

                outdoorIncluded =
                    integration.CoveredHours > 0 &&
                    outdoorCoveragePercent >=
                    minimumCoveragePercent;

                totalCoveredHours +=
                    integration.CoveredHours;

                if (outdoorIncluded)
                {
                    outdoorDegreeDays =
                        integration.DegreeDays;

                    outdoorWeightedTemperatureHours =
                        integration.WeightedTemperatureHours;

                    outdoorCoveredHours =
                        integration.CoveredHours;

                    includedWeightedTemperatureHours +=
                        integration.WeightedTemperatureHours;

                    includedCoveredHours +=
                        integration.CoveredHours;
                }

                observationCount =
                    measurements.Count(measurement =>
                        measurement.UtcTimestamp >= outdoorStartUtc &&
                        measurement.UtcTimestamp < outdoorEndUtc);

                expectedObservationCount =
                    CalculateExpectedObservationCount(
                        outdoorStartUtc,
                        outdoorEndUtc,
                        measurementIntervalMinutes);

                qualityCodes =
                    measurements
                        .Where(measurement =>
                            measurement.UtcTimestamp >= outdoorStartUtc &&
                            measurement.UtcTimestamp < outdoorEndUtc &&
                            measurement.QualityCode.HasValue)
                        .Select(measurement =>
                            measurement.QualityCode!.Value)
                        .Distinct()
                        .OrderBy(code =>
                            code)
                        .ToList();
            }

            var refrigeratorDegreeDays =
                0.0;

            var refrigeratorWeightedTemperatureHours =
                0.0;

            var refrigeratorHours =
                0.0;

            if (refrigeratorStartLocal.HasValue)
            {
                var refrigeratorStartUtc =
                    ConvertLocalToUtc(
                        refrigeratorStartLocal.Value);

                refrigeratorHours =
                    (segmentEndUtc - refrigeratorStartUtc)
                    .TotalHours;

                if (refrigeratorHours > 0)
                {
                    refrigeratorDegreeDays =
                        RefrigeratorTemperatureCelsius *
                        refrigeratorHours /
                        24.0;

                    refrigeratorWeightedTemperatureHours =
                        RefrigeratorTemperatureCelsius *
                        refrigeratorHours;

                    totalCoveredHours +=
                        refrigeratorHours;

                    includedWeightedTemperatureHours +=
                        refrigeratorWeightedTemperatureHours;

                    includedCoveredHours +=
                        refrigeratorHours;
                }
            }

            var degreeDays =
                outdoorDegreeDays +
                refrigeratorDegreeDays;

            var accumulatedBeforeDay =
                accumulatedDegreeDays;

            var dayPeriods =
                new List<MorningDayPeriodModel>(
                    capacity: 2);

            if (outdoorEndLocal > outdoorStartLocal)
            {
                dayPeriods.Add(
                    new MorningDayPeriodModel
                    {
                        PeriodStart =
                            outdoorStartLocal,

                        PeriodEnd =
                            outdoorEndLocal,

                        MeanTemperature =
                            outdoorCoveredHours <= 0
                                ? null
                                : outdoorWeightedTemperatureHours /
                                  outdoorCoveredHours,

                        IncludedInTotal =
                            outdoorIncluded,

                        UsesRefrigeratorTemperature =
                            false,

                        DegreeDays =
                            outdoorDegreeDays,

                        AccumulatedDegreeDays =
                            accumulatedBeforeDay +
                            outdoorDegreeDays
                    });
            }

            if (refrigeratorStartLocal.HasValue &&
                refrigeratorHours > 0)
            {
                dayPeriods.Add(
                    new MorningDayPeriodModel
                    {
                        PeriodStart =
                            refrigeratorStartLocal.Value,

                        PeriodEnd =
                            segmentEndLocal,

                        MeanTemperature =
                            RefrigeratorTemperatureCelsius,

                        IncludedInTotal =
                            true,

                        UsesRefrigeratorTemperature =
                            true,

                        DegreeDays =
                            refrigeratorDegreeDays,

                        AccumulatedDegreeDays =
                            accumulatedBeforeDay +
                            degreeDays
                    });
            }

            accumulatedDegreeDays =
                accumulatedBeforeDay +
                degreeDays;

            var dayCoveredHours =
                outdoorCoveredHours +
                refrigeratorHours;

            var dayWeightedTemperatureHours =
                outdoorWeightedTemperatureHours +
                refrigeratorWeightedTemperatureHours;

            double? meanTemperature =
                dayCoveredHours <= 0
                    ? null
                    : dayWeightedTemperatureHours /
                      dayCoveredHours;

            var coveragePercent =
                segmentDurationHours <= 0
                    ? 0
                    : Math.Min(
                        100,
                        (
                            outdoorCoveredHours +
                            refrigeratorHours
                        ) /
                        segmentDurationHours *
                        100);

            var includedInTotal =
                outdoorDurationHours <= 0 ||
                outdoorIncluded;

            dailyResults.Add(
                new MorningDayModel
                {
                    Date =
                        date,

                    PeriodStart =
                        segmentStartLocal,

                    PeriodEnd =
                        segmentEndLocal,

                    MeanTemperature =
                        meanTemperature,

                    ObservationCount =
                        observationCount,

                    ExpectedObservationCount =
                        expectedObservationCount,

                    CoveragePercent =
                        coveragePercent,

                    IncludedInTotal =
                        includedInTotal,

                    UsesRefrigeratorTemperature =
                        refrigeratorHours > 0,

                    DegreeDays =
                        degreeDays,

                    AccumulatedDegreeDays =
                        accumulatedDegreeDays,

                    QualityCodes =
                        qualityCodes,

                    Periods =
                        dayPeriods
                });
        }

        var totalPeriodHours =
            (periodEndUtc - periodStartUtc)
            .TotalHours;

        var totalCoveragePercent =
            totalPeriodHours <= 0
                ? 0
                : Math.Min(
                    100,
                    totalCoveredHours /
                    totalPeriodHours *
                    100);

        double? averageTemperature =
            includedCoveredHours <= 0
                ? null
                : includedWeightedTemperatureHours /
                  includedCoveredHours;

        var observationCountInPeriod =
            measurements.Count(measurement =>
                measurement.UtcTimestamp >= periodStartUtc &&
                measurement.UtcTimestamp <= periodEndUtc);

        return new MorningResultModel
        {
            HungAt =
                hungAtLocal,

            CalculatedAt =
                calculatedAtLocal,

            CalculatedAtUtc =
                periodEndUtc,

            RefrigeratedAt =
                refrigeratedAtLocal,

            RefrigeratorTemperatureCelsius =
                refrigeratedAtLocal.HasValue
                    ? RefrigeratorTemperatureCelsius
                    : null,

            SourceId =
                source.SourceId,

            SourceName =
                source.Name,

            StationDistanceKilometres =
                source.DistanceKilometres,

            StationLatitude =
                source.Latitude,

            StationLongitude =
                source.Longitude,

            StationMetresAboveSeaLevel =
                source.MetresAboveSeaLevel,

            TargetDegreeDays =
                targetDegreeDays,

            TotalDegreeDays =
                accumulatedDegreeDays,

            AverageTemperature =
                averageTemperature,

            ObservationCount =
                observationCountInPeriod,

            CoveragePercent =
                totalCoveragePercent,

            Days =
                dailyResults
        };
    }

    private static IntegrationResult IntegrateMeasurements(
        IReadOnlyList<TemperatureMeasurementModel> measurements,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        int measurementIntervalMinutes,
        int maximumAcceptedGapMinutes)
    {
        if (measurements.Count == 0 ||
            periodEnd <= periodStart)
        {
            return IntegrationResult.Empty;
        }

        var totalDegreeDays =
            0.0;

        var weightedTemperatureHours =
            0.0;

        var coveredHours =
            0.0;

        for (
            var index = 0;
            index < measurements.Count;
            index++)
        {
            var current =
                measurements[index];

            var nextTimestamp =
                index + 1 < measurements.Count
                    ? measurements[index + 1].UtcTimestamp
                    : current.UtcTimestamp.AddMinutes(
                        measurementIntervalMinutes);

            var gapMinutes =
                (nextTimestamp - current.UtcTimestamp)
                .TotalMinutes;

            if (gapMinutes <= 0 ||
                gapMinutes > maximumAcceptedGapMinutes)
            {
                continue;
            }

            var intervalStart =
                current.UtcTimestamp > periodStart
                    ? current.UtcTimestamp
                    : periodStart;

            var intervalEnd =
                nextTimestamp < periodEnd
                    ? nextTimestamp
                    : periodEnd;

            if (intervalEnd <= intervalStart)
            {
                continue;
            }

            var intervalHours =
                (intervalEnd - intervalStart)
                .TotalHours;

            weightedTemperatureHours +=
                current.Temperature *
                intervalHours;

            totalDegreeDays +=
                Math.Max(
                    0,
                    current.Temperature) *
                intervalHours /
                24.0;

            coveredHours +=
                intervalHours;
        }

        return new IntegrationResult(
            totalDegreeDays,
            weightedTemperatureHours,
            coveredHours);
    }

    private static int CalculateExpectedObservationCount(
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        int measurementIntervalMinutes)
    {
        var totalMinutes =
            (periodEnd - periodStart)
            .TotalMinutes;

        if (totalMinutes <= 0)
        {
            return 0;
        }

        return (int)Math.Ceiling(
            totalMinutes /
            measurementIntervalMinutes);
    }

    private static void ValidateSettings(
        int measurementIntervalMinutes,
        int maximumAcceptedGapMinutes,
        double minimumCoveragePercent,
        int maximumDaysBack)
    {
        if (minimumCoveragePercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumCoveragePercent),
                "Minimum datadekning må vere mellom 0 og 100 prosent.");
        }

        if (measurementIntervalMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(measurementIntervalMinutes),
                "Måleintervallet må vere større enn null.");
        }

        if (maximumAcceptedGapMinutes <
            measurementIntervalMinutes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumAcceptedGapMinutes),
                "Største godtekne målehol kan ikkje vere " +
                "mindre enn måleintervallet.");
        }

        if (maximumDaysBack <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDaysBack),
                "Maksimal historikk må vere større enn null.");
        }
    }

    public static void ValidateRequest(
        DateTime hungAt,
        double targetDegreeDays,
        DateTime? refrigeratedAt,
        int maximumDaysBack,
        DateTimeOffset? calculatedAtUtc = null)
    {
        if (targetDegreeDays is < 1 or > 300)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetDegreeDays),
                "Målet må vere mellom 1 og 300 døgngrader.");
        }

        if (maximumDaysBack <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDaysBack),
                "Maksimal historikk må vere større enn null.");
        }

        var nowUtc =
            (calculatedAtUtc ?? DateTimeOffset.UtcNow)
                .ToUniversalTime();

        var nowLocalOffset =
            TimeZoneInfo.ConvertTime(
                nowUtc,
                NorwegianTimeZone);

        var nowLocal =
            DateTime.SpecifyKind(
                nowLocalOffset.DateTime,
                DateTimeKind.Unspecified);

        var hungAtLocal =
            DateTime.SpecifyKind(
                hungAt,
                DateTimeKind.Unspecified);

        ValidateHungAt(
            hungAtLocal,
            nowLocal,
            maximumDaysBack);

        if (refrigeratedAt.HasValue)
        {
            var refrigeratedAtLocal =
                DateTime.SpecifyKind(
                    refrigeratedAt.Value,
                    DateTimeKind.Unspecified);

            ValidateRefrigeratedAt(
                refrigeratedAtLocal,
                hungAtLocal,
                nowLocal);
        }
    }

    private static void ValidateSource(
        TemperatureSourceModel source)
    {
        if (string.IsNullOrWhiteSpace(
                source.SourceId))
        {
            throw new ArgumentException(
                "Temperaturkjelda manglar kjelde-ID.",
                nameof(source));
        }

        if (string.IsNullOrWhiteSpace(
                source.Name))
        {
            throw new ArgumentException(
                "Temperaturkjelda manglar namn.",
                nameof(source));
        }
    }

    private static void ValidateRefrigeratedAt(
        DateTime refrigeratedAtLocal,
        DateTime hungAtLocal,
        DateTime nowLocal)
    {
        if (refrigeratedAtLocal < hungAtLocal)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refrigeratedAtLocal),
                "Kjøleskapstidspunktet kan ikkje vere før " +
                "opphengstidspunktet.");
        }

        if (refrigeratedAtLocal > nowLocal)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refrigeratedAtLocal),
                "Kjøleskapstidspunktet kan ikkje vere fram i tid.");
        }

        if (NorwegianTimeZone.IsInvalidTime(
                refrigeratedAtLocal))
        {
            throw new ArgumentException(
                "Kjøleskapstidspunktet finst ikkje på grunn av " +
                "overgang til sommartid.",
                nameof(refrigeratedAtLocal));
        }

        if (NorwegianTimeZone.IsAmbiguousTime(
                refrigeratedAtLocal))
        {
            throw new ArgumentException(
                "Kjøleskapstidspunktet er tvitydig på grunn av " +
                "overgang frå sommartid.",
                nameof(refrigeratedAtLocal));
        }
    }

    private static void ValidateHungAt(
        DateTime hungAtLocal,
        DateTime nowLocal,
        int maximumDaysBack)
    {
        if (hungAtLocal > nowLocal)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hungAtLocal),
                "Opphengstidspunktet kan ikkje vere fram i tid.");
        }

        var earliestAllowed =
            nowLocal.AddDays(
                -maximumDaysBack);

        if (hungAtLocal < earliestAllowed)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hungAtLocal),
                $"Du kan maksimalt hente " +
                $"{maximumDaysBack} døgn bakover.");
        }

        if (NorwegianTimeZone.IsInvalidTime(
                hungAtLocal))
        {
            throw new ArgumentException(
                "Tidspunktet finst ikkje på grunn av overgang " +
                "til sommartid.",
                nameof(hungAtLocal));
        }

        if (NorwegianTimeZone.IsAmbiguousTime(
                hungAtLocal))
        {
            throw new ArgumentException(
                "Tidspunktet er tvitydig på grunn av overgang " +
                "frå sommartid. Vel eit anna klokkeslett.",
                nameof(hungAtLocal));
        }
    }

    public static DateTimeOffset ConvertNorwegianLocalToUtc(
        DateTime localDateTime)
    {
        return ConvertLocalToUtc(
            localDateTime);
    }

    private static DateTimeOffset ConvertLocalToUtc(
        DateTime localDateTime)
    {
        var unspecified =
            DateTime.SpecifyKind(
                localDateTime,
                DateTimeKind.Unspecified);

        var utc =
            TimeZoneInfo.ConvertTimeToUtc(
                unspecified,
                NorwegianTimeZone);

        return new DateTimeOffset(
            utc,
            TimeSpan.Zero);
    }

    private sealed record IntegrationResult(
        double DegreeDays,
        double WeightedTemperatureHours,
        double CoveredHours)
    {
        public static IntegrationResult Empty { get; } =
            new(
                DegreeDays: 0,
                WeightedTemperatureHours: 0,
                CoveredHours: 0);
    }
}
