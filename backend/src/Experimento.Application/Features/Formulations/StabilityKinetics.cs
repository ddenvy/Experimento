using System.Globalization;
using System.Text.Json.Serialization;

namespace Experimento.Application.Features.Formulations;

/// <summary>Насколько расчёт срока годности подтверждён введёнными данными.</summary>
public enum StabilityConfidence
{
    Low,
    Medium,
    High
}

/// <summary>Измерение: содержание вещества после выдержки при заданной температуре.</summary>
public sealed record StabilityMeasurement(double TemperatureCelsius, double TimeDays, double AssayPercent);

/// <summary>Оценка константы скорости деградации при одной температуре.</summary>
public record StabilityRateDto(
    double TemperatureCelsius,
    int Measurements,
    double RateConstantPerDay,
    double? RSquared,
    double? ShelfLifeDays);

/// <summary>
/// Расчёт срока годности по экспериментальным данным стабильности.
/// Confidence показывает, насколько результат подтверждён данными, а не тем,
/// насколько красиво выглядит число.
/// </summary>
public record StabilityAssessmentDto(
    Guid VersionId,
    double? ActivationEnergyKjPerMol,
    double? ShelfLifeDaysAt25C,
    string Summary,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] StabilityConfidence Confidence,
    IReadOnlyList<StabilityRateDto> Rates,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Assumptions);

/// <summary>
/// Кинетика деградации: реакция первого порядка даёт константу скорости из данных,
/// уравнение Аррениуса — перенос скорости на температуру хранения, t90 — срок годности
/// до 90% от начального содержания. Это расчёт по введённым данным: свет, влага и
/// кислород в нём не представлены, поэтому результат сопровождается допущениями.
/// </summary>
public static class StabilityKinetics
{
    /// <summary>Температура хранения, для которой считается срок годности (°C).</summary>
    public const double ReferenceTemperatureCelsius = 25.0;

    private const double ReferenceTemperatureKelvin = 298.15;
    private const double AbsoluteZeroCelsius = 273.15;

    /// <summary>Универсальная газовая постоянная, Дж/(моль·К).</summary>
    private const double GasConstant = 8.314;

    /// <summary>Предел годности: 90% от начального содержания.</summary>
    private const double ShelfLifeLimitPercent = 90.0;

    // Типичный диапазон энергии активации деградации лекарственных веществ, кДж/моль.
    private const double MinPlausibleActivationEnergy = 10.0;
    private const double MaxPlausibleActivationEnergy = 200.0;

    // Качество линейной аппроксимации первого порядка.
    private const double GoodFitRSquared = 0.95;
    private const double AcceptableFitRSquared = 0.80;

    /// <summary>Насколько глубоко допустимо экстраполировать ниже изученных температур, °C.</summary>
    private const double MaxExtrapolationCelsius = 15.0;

    public static StabilityAssessmentDto Assess(Guid versionId, IReadOnlyList<StabilityMeasurement> measurements)
    {
        var warnings = new List<string>();

        var rates = measurements
            .GroupBy(m => Math.Round(m.TemperatureCelsius, 2))
            .OrderBy(g => g.Key)
            .Select(g => EstimateRate(g.Key, g.ToList(), warnings))
            .ToList();

        // Для переноса скорости на другую температуру нужны точки минимум при двух температурах.
        var measurable = rates.Where(r => r.RateConstantPerDay > 0).ToList();
        double? activationEnergy = null;
        double? shelfLifeDays = null;
        var extrapolation = 0.0;

        if (measurable.Count >= 2)
        {
            var fit = FitArrhenius(measurable);
            if (fit is null)
            {
                warnings.Add("Rate constants cannot be fitted against temperature: the studied temperatures are identical.");
            }
            else
            {
                activationEnergy = fit.Value.ActivationEnergyKjPerMol;
                var rateAtReference = Math.Exp(fit.Value.Intercept + fit.Value.Slope / ReferenceTemperatureKelvin);
                shelfLifeDays = rateAtReference > 0 ? ShelfLifeDaysFromRate(rateAtReference) : null;

                if (activationEnergy is < MinPlausibleActivationEnergy or > MaxPlausibleActivationEnergy)
                    warnings.Add(
                        $"Fitted activation energy {Num(activationEnergy.Value, "0.#")} kJ/mol is outside the typical " +
                        $"{Num(MinPlausibleActivationEnergy, "0")}–{Num(MaxPlausibleActivationEnergy, "0")} kJ/mol range; check the input data.");

                // Расстояние переноса до температуры хранения: и вверх, и вниз от изученного диапазона.
                extrapolation = Math.Abs(ReferenceTemperatureCelsius - measurable.Min(r => r.TemperatureCelsius));
                if (extrapolation > MaxExtrapolationCelsius)
                    warnings.Add(
                        $"Shelf life is extrapolated {Num(extrapolation, "0.#")} °C from the studied range " +
                        $"({Num(measurable.Min(r => r.TemperatureCelsius), "0.#")}–{Num(measurable.Max(r => r.TemperatureCelsius), "0.#")} °C); " +
                        "confirm with a long-term study.");
            }
        }
        else
        {
            warnings.Add("The Arrhenius transfer to storage temperature needs measurements at two or more temperatures.");
        }

        var confidence = Confidence(rates, activationEnergy, shelfLifeDays, extrapolation);

        return new StabilityAssessmentDto(
            versionId,
            activationEnergy is null ? null : Math.Round(activationEnergy.Value, 1),
            shelfLifeDays is null ? null : Math.Round(shelfLifeDays.Value, 1),
            Summary(activationEnergy, shelfLifeDays, rates),
            confidence,
            rates.Select(r => new StabilityRateDto(
                r.TemperatureCelsius,
                r.Measurements,
                Math.Round(r.RateConstantPerDay, 6),
                r.RSquared is null ? null : Math.Round(r.RSquared.Value, 4),
                r.ShelfLifeDays is null ? null : Math.Round(r.ShelfLifeDays.Value, 1))).ToList(),
            warnings,
            Assumptions);
    }

    /// <summary>
    /// Константа скорости при одной температуре: наклон ln(содержание) от времени.
    /// Одна точка тоже даёт скорость — при допущении 100% в начальный момент,
    /// но тогда линейность не проверена, о чём сообщается предупреждением.
    /// </summary>
    private static StabilityRateDto EstimateRate(
        double temperature, List<StabilityMeasurement> points, List<string> warnings)
    {
        var ordered = points.OrderBy(p => p.TimeDays).ToList();
        var usable = ordered.Where(p => p.TimeDays > 0).ToList();

        if (usable.Count == 0)
        {
            warnings.Add($"All measurements at {Num(temperature, "0.#")} °C are at time zero, so no rate can be fitted.");
            return new StabilityRateDto(temperature, ordered.Count, 0, null, null);
        }

        if (usable.Count == 1)
        {
            var single = usable[0];
            var rate = -Math.Log(single.AssayPercent / 100.0) / single.TimeDays;
            warnings.Add(
                $"Only one measurement at {Num(temperature, "0.#")} °C: the rate assumes 100% at the start and first-order kinetics, " +
                "so linearity is not verified.");
            return rate > 0
                ? new StabilityRateDto(temperature, ordered.Count, rate, null, ShelfLifeDaysFromRate(rate))
                : NoDegradation(temperature, ordered.Count, warnings);
        }

        var fit = LinearRegression(
            usable.Select(p => p.TimeDays).ToList(),
            usable.Select(p => Math.Log(p.AssayPercent / 100.0)).ToList());
        if (fit is null)
        {
            warnings.Add($"Measurements at {Num(temperature, "0.#")} °C share the same time point, so the rate cannot be fitted.");
            return new StabilityRateDto(temperature, ordered.Count, 0, null, null);
        }

        var (slope, _, rSquared) = fit.Value;
        var fittedRate = -slope;
        if (fittedRate <= 0)
            return NoDegradation(temperature, ordered.Count, warnings) with { RSquared = rSquared };

        if (rSquared is { } r2 && r2 < AcceptableFitRSquared)
            warnings.Add(
                $"Poor first-order fit at {Num(temperature, "0.#")} °C (R² = {Num(r2, "0.###")}): degradation may follow " +
                "different kinetics or the data may be inconsistent.");

        return new StabilityRateDto(temperature, ordered.Count, fittedRate, rSquared, ShelfLifeDaysFromRate(fittedRate));
    }

    private static StabilityRateDto NoDegradation(double temperature, int measurements, List<string> warnings)
    {
        warnings.Add($"No measurable degradation at {Num(temperature, "0.#")} °C: the fitted rate is not positive.");
        return new StabilityRateDto(temperature, measurements, 0, null, null);
    }

    /// <summary>Прямая по (1/T, ln k): наклон даёт энергию активации, отрезок — перенос на 25 °C.</summary>
    private static ArrheniusFit? FitArrhenius(IReadOnlyList<StabilityRateDto> rates)
    {
        var fit = LinearRegression(
            rates.Select(r => 1.0 / (r.TemperatureCelsius + AbsoluteZeroCelsius)).ToList(),
            rates.Select(r => Math.Log(r.RateConstantPerDay)).ToList());
        if (fit is null) return null;

        var (slope, intercept, _) = fit.Value;
        return new ArrheniusFit(slope, intercept, -slope * GasConstant / 1000.0);
    }

    /// <summary>Метод наименьших квадратов; null, если по оси X нет разброса и наклон неопределён.</summary>
    private static (double Slope, double Intercept, double? RSquared)? LinearRegression(
        IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        if (xs.Count < 2) return null;

        var meanX = xs.Average();
        var meanY = ys.Average();
        double sxx = 0, sxy = 0;
        for (var i = 0; i < xs.Count; i++)
        {
            var dx = xs[i] - meanX;
            sxx += dx * dx;
            sxy += dx * (ys[i] - meanY);
        }
        // Порог нужен из-за масштаба 1/T: разброс температур даёт sxx порядка 1e-7.
        if (sxx <= 1e-12) return null;

        var slope = sxy / sxx;
        var intercept = meanY - slope * meanX;

        double ssRes = 0, ssTot = 0;
        for (var i = 0; i < xs.Count; i++)
        {
            var predicted = slope * xs[i] + intercept;
            ssRes += Math.Pow(ys[i] - predicted, 2);
            ssTot += Math.Pow(ys[i] - meanY, 2);
        }

        // При нулевом разбросе по Y коэффициент детерминации не определён.
        var rSquared = ssTot <= 1e-12 ? (double?)null : 1 - ssRes / ssTot;
        return (slope, intercept, rSquared);
    }

    private static double ShelfLifeDaysFromRate(double ratePerDay)
        => Math.Log(100.0 / ShelfLifeLimitPercent) / ratePerDay;

    private static StabilityConfidence Confidence(
        IReadOnlyList<StabilityRateDto> rates, double? activationEnergy, double? shelfLifeDays, double extrapolation)
    {
        if (activationEnergy is not { } energy || shelfLifeDays is null) return StabilityConfidence.Low;
        if (energy is < MinPlausibleActivationEnergy or > MaxPlausibleActivationEnergy) return StabilityConfidence.Low;
        if (rates.Any(r => r.RSquared is { } r2 && r2 < AcceptableFitRSquared)) return StabilityConfidence.Low;

        var wellFitted = rates.All(r => r.Measurements >= 3 && r.RSquared is { } r2 && r2 >= GoodFitRSquared);
        return wellFitted && extrapolation <= MaxExtrapolationCelsius
            ? StabilityConfidence.High
            : StabilityConfidence.Medium;
    }

    private static string Summary(
        double? activationEnergy, double? shelfLifeDays, IReadOnlyList<StabilityRateDto> rates)
    {
        if (shelfLifeDays is null)
            return "Not enough data to project a shelf life at 25 °C.";

        var months = shelfLifeDays.Value / 30.4375;
        var basis = rates.Count == 1
            ? "a single storage temperature"
            : $"{rates.Count} storage temperatures";

        return $"Projected shelf life (t90) is {Num(months, "0.#")} months at " +
               $"{Num(ReferenceTemperatureCelsius, "0")} °C from a first-order fit over {basis}" +
               (activationEnergy is { } energy ? $" (activation energy {Num(energy, "0.#")} kJ/mol)." : ".");
    }

    private static readonly IReadOnlyList<string> Assumptions = new[]
    {
        "Degradation follows first-order kinetics at every temperature.",
        "Assay values are percentages of the initial content, so a measurement at time zero is 100%.",
        "Shelf life is the time to 90% of the initial content (t90).",
        "The temperature dependence follows the Arrhenius equation with a single activation energy.",
        "Light, moisture, oxygen and packaging are not represented in these data.",
    };

    private static string Num(double value, string format)
        => value.ToString(format, CultureInfo.InvariantCulture);

    private readonly record struct ArrheniusFit(double Slope, double Intercept, double ActivationEnergyKjPerMol);
}
