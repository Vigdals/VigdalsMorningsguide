using VigdalsMorningsguide.Models;
using VigdalsMorningsguide.Services;

var tests = new (string Name, Action Run)[]
{
    ("24 timar ved 10 C gir 10 døgngrader", ConstantDay),
    ("Del av døgn blir vekta med faktisk tid", PartialDay),
    ("Minusgrader gir ikkje negative døgngrader", NegativeTemperature),
    ("Kjøleskapsdagen blir delt ved klokkeslettet", SplitRefrigeratorDay),
    ("Målehol fyller berre nominelt intervall", MeasurementGap),
    ("Delvis dekning blir vist, men ikkje summert", PartialCoverage),
    ("Kjøleskap frå start gir fast 4 C", RefrigeratorFromStart),
    ("Sommartidsdøgn brukar 23 faktiske timar", SpringDstDay),
    ("Vintertidsdøgn brukar 25 faktiske timar", AutumnDstDay),
    ("Dagleg måleintervall toler 25-timarsdøgn", DailyMeasurementAcrossDst),
    ("Midnatt lagar periodar utan overlapp", MidnightBoundary),
    ("Estimerte Shelly-temperaturar blir merkte", EstimatedTemperature),
    ("Shelly krev to endepunkt for estimert middel", ShellyMeanTemperature)
};

var failures = new List<string>();

foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"OK   {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add(test.Name);
        Console.Error.WriteLine($"FEIL {test.Name}");
        Console.Error.WriteLine($"     {exception.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} testar bestått.");

if (failures.Count > 0)
{
    Environment.ExitCode = 1;
}

return;

static void ConstantDay()
{
    var start = Local(2026, 8, 1);
    var end = Local(2026, 8, 2);
    var result = Calculate(
        start,
        end,
        measurements:
        [
            Measurement(start, 10)
        ],
        measurementIntervalMinutes: 24 * 60);

    Near(10, result.TotalDegreeDays);
    Near(10, result.AverageTemperature!.Value);
    Near(100, result.CoveragePercent);
}

static void PartialDay()
{
    var start = Local(2026, 8, 1);
    var end = Local(2026, 8, 1, 6);
    var result = Calculate(
        start,
        end,
        measurements:
        [
            Measurement(start, 12)
        ],
        measurementIntervalMinutes: 6 * 60);

    Near(3, result.TotalDegreeDays);
}

static void NegativeTemperature()
{
    var start = Local(2026, 8, 1);
    var end = Local(2026, 8, 2);
    var result = Calculate(
        start,
        end,
        measurements:
        [
            Measurement(start, -5)
        ],
        measurementIntervalMinutes: 24 * 60);

    Near(0, result.TotalDegreeDays);
    Near(-5, result.AverageTemperature!.Value);
}

static void SplitRefrigeratorDay()
{
    var start = Local(2026, 8, 21, 23, 36);
    var refrigeratedAt = Local(2026, 8, 22, 23);
    var end = Local(2026, 8, 27, 23, 37);
    const double transitionDayTemperature =
        (17.55 * 24 - 4) / 23;

    var measurements = new List<TemperatureMeasurementModel>();

    for (var cursor = start; cursor < refrigeratedAt; cursor = cursor.AddMinutes(1))
    {
        measurements.Add(
            Measurement(
                cursor,
                cursor.Date == start.Date
                    ? 15
                    : transitionDayTemperature));
    }

    var result = Calculate(
        start,
        end,
        refrigeratedAt,
        measurements,
        measurementIntervalMinutes: 1);

    Near(37.7361111111, result.TotalDegreeDays, 0.000001);

    var transitionDay =
        result.Days.Single(day =>
            day.Date == new DateOnly(2026, 8, 22));

    Equal(2, transitionDay.Periods.Count);
    Near(17.3833333333, transitionDay.Periods[0].DegreeDays, 0.000001);
    Near(0.1666666667, transitionDay.Periods[1].DegreeDays, 0.000001);
    Near(17.55, transitionDay.DegreeDays, 0.000001);
    Near(
        transitionDay.DegreeDays,
        transitionDay.Periods.Sum(period => period.DegreeDays),
        0.000001);
    True(!transitionDay.Periods[0].UsesRefrigeratorTemperature);
    True(transitionDay.Periods[1].UsesRefrigeratorTemperature);
    Equal(refrigeratedAt, transitionDay.Periods[1].PeriodStart);
}

static void MeasurementGap()
{
    foreach (var gapMinutes in new[] { 90, 91 })
    {
        var start = Local(2026, 8, 1);
        var end = start.AddMinutes(gapMinutes);
        var result = Calculate(
            start,
            end,
            measurements:
            [
                Measurement(start, 12),
                Measurement(end, 12)
            ],
            measurementIntervalMinutes: 10,
            minimumCoveragePercent: 0);

        Near(
            10.0 / gapMinutes * 100,
            result.Days[0].Periods[0].CoveragePercent);
        Near(12 * (10.0 / 60) / 24, result.TotalDegreeDays);
    }
}

static void PartialCoverage()
{
    var start = Local(2026, 8, 1);
    var end = start.AddMinutes(90);
    var result = Calculate(
        start,
        end,
        measurements:
        [
            Measurement(start, 12),
            Measurement(end, 12)
        ],
        measurementIntervalMinutes: 10,
        minimumCoveragePercent: 70);

    var period = result.Days[0].Periods[0];
    Near(0, result.TotalDegreeDays);
    Near(12, period.MeanTemperature!.Value);
    Near(100.0 / 9, period.CoveragePercent);
    True(!period.IncludedInTotal);
    Equal(1, result.IncompleteMeasuredPeriodCount);
    True(result.EstimatedHoursRemaining is null);
}

static void RefrigeratorFromStart()
{
    var start = Local(2026, 8, 1);
    var end = Local(2026, 8, 2);
    var result = Calculate(
        start,
        end,
        refrigeratedAt: start,
        measurements: [],
        measurementIntervalMinutes: 10);

    Near(4, result.TotalDegreeDays);
    Near(4, result.AverageTemperature!.Value);
    Near(100, result.CoveragePercent);
    True(result.Days[0].Periods.Single().UsesRefrigeratorTemperature);
}

static void SpringDstDay()
{
    var start = Local(2026, 3, 29);
    var end = Local(2026, 3, 30);
    var result = Calculate(
        start,
        end,
        refrigeratedAt: start,
        measurements: [],
        measurementIntervalMinutes: 10);

    Near(23, result.ElapsedTime.TotalHours);
    Near(4 * 23.0 / 24, result.TotalDegreeDays);
}

static void AutumnDstDay()
{
    var start = Local(2026, 10, 25);
    var end = Local(2026, 10, 26);
    var result = Calculate(
        start,
        end,
        refrigeratedAt: start,
        measurements: [],
        measurementIntervalMinutes: 10);

    Near(25, result.ElapsedTime.TotalHours);
    Near(4 * 25.0 / 24, result.TotalDegreeDays);
}

static void DailyMeasurementAcrossDst()
{
    var start = Local(2026, 10, 25);
    var end = Local(2026, 10, 26);
    var result = Calculate(
        start,
        end,
        measurements:
        [
            Measurement(start, 10),
            Measurement(end, 10)
        ],
        measurementIntervalMinutes: 24 * 60);

    Near(10 * 25.0 / 24, result.TotalDegreeDays);
    Near(100, result.CoveragePercent);
}

static void MidnightBoundary()
{
    var start = Local(2026, 8, 1, 23);
    var refrigeratedAt = Local(2026, 8, 2);
    var end = Local(2026, 8, 2, 1);
    var result = Calculate(
        start,
        end,
        refrigeratedAt,
        measurements:
        [
            Measurement(start, 12)
        ],
        measurementIntervalMinutes: 60);

    Equal(2, result.Days.Count);
    Equal(1, result.Days[0].Periods.Count);
    Equal(1, result.Days[1].Periods.Count);
    Equal(refrigeratedAt, result.Days[0].Periods[0].PeriodEnd);
    Equal(refrigeratedAt, result.Days[1].Periods[0].PeriodStart);
    Near(2.0 / 3, result.TotalDegreeDays);
}

static void EstimatedTemperature()
{
    var start = Local(2026, 8, 1);
    var end = Local(2026, 8, 2);
    var result = Calculate(
        start,
        end,
        measurements:
        [
            Measurement(start, 10, isEstimated: true)
        ],
        measurementIntervalMinutes: 24 * 60);

    True(result.UsesEstimatedTemperatures);
}

static void ShellyMeanTemperature()
{
    var minimumOnly = new ShellyWeatherHistoryEntry
    {
        MinimumTemperatureCelsius = 8
    };

    True(minimumOnly.MeanTemperatureCelsius is null);
    True(!minimumOnly.IsTemperatureEstimated);

    var range = new ShellyWeatherHistoryEntry
    {
        MinimumTemperatureCelsius = 8,
        MaximumTemperatureCelsius = 14
    };

    Near(11, range.MeanTemperatureCelsius!.Value);
    True(range.IsTemperatureEstimated);

    var average = new ShellyWeatherHistoryEntry
    {
        MinimumTemperatureCelsius = 8,
        MaximumTemperatureCelsius = 14,
        AverageTemperatureCelsius = 10.5
    };

    Near(10.5, average.MeanTemperatureCelsius!.Value);
    True(!average.IsTemperatureEstimated);
}

static MorningResultModel Calculate(
    DateTime start,
    DateTime end,
    DateTime? refrigeratedAt = null,
    IReadOnlyList<TemperatureMeasurementModel>? measurements = null,
    int measurementIntervalMinutes = 10,
    double minimumCoveragePercent = 70)
{
    return new DegreeDayCalculationService().Calculate(
        start,
        80,
        new TemperatureSourceModel
        {
            SourceId = "TEST",
            Name = "Testmålar",
            DistanceKilometres = 0,
            Latitude = 61,
            Longitude = 7
        },
        refrigeratedAt,
        measurements ?? [],
        measurementIntervalMinutes,
        minimumCoveragePercent,
        370,
        DegreeDayCalculationService.ConvertNorwegianLocalToUtc(end));
}

static TemperatureMeasurementModel Measurement(
    DateTime localTimestamp,
    double temperature,
    bool isEstimated = false)
{
    return new TemperatureMeasurementModel(
        DegreeDayCalculationService.ConvertNorwegianLocalToUtc(
            localTimestamp),
        temperature,
        IsEstimated: isEstimated);
}

static DateTime Local(
    int year,
    int month,
    int day,
    int hour = 0,
    int minute = 0)
{
    return new DateTime(
        year,
        month,
        day,
        hour,
        minute,
        0,
        DateTimeKind.Unspecified);
}

static void Near(
    double expected,
    double actual,
    double tolerance = 0.0000001)
{
    if (Math.Abs(expected - actual) > tolerance)
    {
        throw new InvalidOperationException(
            $"Venta {expected}, fekk {actual}.");
    }
}

static void Equal<T>(
    T expected,
    T actual)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"Venta {expected}, fekk {actual}.");
    }
}

static void True(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException(
            "Vilkåret var ikkje oppfylt.");
    }
}
