using Experimento.Application.Features.Formulations;

namespace Experimento.Application.Tests;

/// <summary>
/// Тесты расчёта срока годности: восстановление энергии активации и t90 на синтетических
/// данных с известными параметрами, а также поведение на неполных и противоречивых данных.
/// </summary>
public class StabilityKineticsTests
{
    private static readonly Guid VersionId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private const double GasConstant = 8.314;
    private const double ReferenceKelvin = 298.15;
    private const double AbsoluteZeroCelsius = 273.15;

    /// <summary>Синтетические данные: реакция первого порядка с заданными k(25 °C) и энергией активации.</summary>
    private static List<StabilityMeasurement> FirstOrderData(
        double rateAt25CelsiusPerDay, double activationEnergyJPerMol, params double[] temperatures)
    {
        var points = new List<StabilityMeasurement>();
        foreach (var temperature in temperatures)
        {
            var kelvin = temperature + AbsoluteZeroCelsius;
            var rate = rateAt25CelsiusPerDay
                       * Math.Exp(-activationEnergyJPerMol / GasConstant * (1 / kelvin - 1 / ReferenceKelvin));
            foreach (var time in new[] { 0.0, 30.0, 60.0, 90.0 })
                points.Add(new StabilityMeasurement(temperature, time, 100.0 * Math.Exp(-rate * time)));
        }
        return points;
    }

    /// <summary>Измерения при одной температуре с заданной константой скорости.</summary>
    private static IEnumerable<StabilityMeasurement> DataAt(double temperature, double ratePerDay)
        => new[] { 0.0, 30.0, 60.0, 90.0 }
            .Select(time => new StabilityMeasurement(temperature, time, 100.0 * Math.Exp(-ratePerDay * time)));

    [Fact(DisplayName = "Exact first-order data recovers activation energy and shelf life")]
    public void ExactData_RecoversParameters()
    {
        // t90 = ln(100/90) / 0.0005 = 210.7 суток.
        var assessment = StabilityKinetics.Assess(VersionId, FirstOrderData(0.0005, 80_000, 25, 40, 50));

        Assert.Equal(80.0, assessment.ActivationEnergyKjPerMol!.Value, 1);
        Assert.Equal(210.7, assessment.ShelfLifeDaysAt25C!.Value, 1);
        Assert.Equal(StabilityConfidence.High, assessment.Confidence);
        Assert.Empty(assessment.Warnings);

        Assert.Equal(3, assessment.Rates.Count);
        Assert.All(assessment.Rates, rate =>
        {
            Assert.Equal(4, rate.Measurements);
            Assert.Equal(1.0, rate.RSquared!.Value, 3);
        });
        Assert.Contains("months at 25 °C", assessment.Summary);
        // Допущения всегда на виду: свет, влага и упаковка в расчёт не входят.
        Assert.Contains(assessment.Assumptions, a => a.Contains("Light, moisture, oxygen"));
    }

    [Fact(DisplayName = "Rates are reported per temperature with the shelf life at that temperature")]
    public void Rates_AreReportedPerTemperature()
    {
        var assessment = StabilityKinetics.Assess(VersionId, FirstOrderData(0.0005, 80_000, 25, 50));

        var at25 = assessment.Rates.Single(r => r.TemperatureCelsius == 25);
        var at50 = assessment.Rates.Single(r => r.TemperatureCelsius == 50);

        Assert.Equal(210.7, at25.ShelfLifeDays!.Value, 0);
        // При 50 °C деградация идёт быстрее, поэтому срок годности короче.
        Assert.True(at50.ShelfLifeDays!.Value < at25.ShelfLifeDays.Value);
        Assert.True(at50.RateConstantPerDay > at25.RateConstantPerDay);
    }

    [Fact(DisplayName = "A single storage temperature yields no Arrhenius transfer")]
    public void SingleTemperature_HasNoArrheniusTransfer()
    {
        var assessment = StabilityKinetics.Assess(VersionId, FirstOrderData(0.0005, 80_000, 40));

        Assert.Null(assessment.ActivationEnergyKjPerMol);
        Assert.Null(assessment.ShelfLifeDaysAt25C);
        Assert.Equal(StabilityConfidence.Low, assessment.Confidence);
        Assert.Contains(assessment.Warnings, w => w.Contains("two or more temperatures"));
        Assert.Contains("Not enough data", assessment.Summary);

        // Скорость и срок годности при изученной температуре всё равно считаются.
        Assert.Single(assessment.Rates);
        Assert.NotNull(assessment.Rates[0].ShelfLifeDays);
    }

    [Fact(DisplayName = "A single measurement gives a rate but warns that linearity is unverified")]
    public void SingleMeasurement_WarnsAboutLinearity()
    {
        var assessment = StabilityKinetics.Assess(VersionId, [new StabilityMeasurement(40, 90, 82)]);

        var rate = Assert.Single(assessment.Rates);
        Assert.Equal(1, rate.Measurements);
        Assert.Null(rate.RSquared);
        // k = -ln(0.82) / 90 = 0.002205 в сутки.
        Assert.Equal(0.002205, rate.RateConstantPerDay, 6);
        Assert.Contains(assessment.Warnings, w => w.Contains("Only one measurement"));
        Assert.Equal(StabilityConfidence.Low, assessment.Confidence);
    }

    [Fact(DisplayName = "An implausible activation energy is flagged and lowers confidence")]
    public void ImplausibleActivationEnergy_IsFlagged()
    {
        // Деградация быстрее при низкой температуре: наклон Аррениуса даёт отрицательную энергию.
        var assessment = StabilityKinetics.Assess(
            VersionId, DataAt(40, 0.001).Concat(DataAt(50, 0.0005)).ToList());

        Assert.True(assessment.ActivationEnergyKjPerMol is < 0);
        Assert.Contains(assessment.Warnings, w => w.Contains("outside the typical"));
        Assert.Equal(StabilityConfidence.Low, assessment.Confidence);
        // Число всё равно возвращается — решение о доверии остаётся за химиком.
        Assert.NotNull(assessment.ShelfLifeDaysAt25C);
    }

    [Fact(DisplayName = "A poor first-order fit is flagged")]
    public void PoorFit_IsFlagged()
    {
        var noisy = new List<StabilityMeasurement>
        {
            new(40, 0, 100),
            new(40, 30, 96),
            new(40, 60, 99),
            new(40, 90, 90),
        };

        var assessment = StabilityKinetics.Assess(VersionId, noisy.Concat(DataAt(50, 0.002)).ToList());

        var at40 = assessment.Rates.Single(r => r.TemperatureCelsius == 40);
        Assert.True(at40.RSquared < 0.8);
        Assert.Contains(assessment.Warnings, w => w.Contains("Poor first-order fit"));
        Assert.Equal(StabilityConfidence.Low, assessment.Confidence);
    }

    [Fact(DisplayName = "Content that does not fall has no measurable rate")]
    public void NoDegradation_IsReported()
    {
        // Разброс аналитики без падения содержания: наклон не даёт положительной скорости.
        var flat = new List<StabilityMeasurement>
        {
            new(40, 0, 100),
            new(40, 30, 99.5),
            new(40, 60, 99.8),
            new(40, 90, 99.9),
        };

        var assessment = StabilityKinetics.Assess(VersionId, flat);

        Assert.Equal(0, assessment.Rates[0].RateConstantPerDay);
        Assert.Null(assessment.Rates[0].ShelfLifeDays);
        Assert.Contains(assessment.Warnings, w => w.Contains("No measurable degradation"));
    }

    [Fact(DisplayName = "Extrapolation far below the studied range is flagged")]
    public void DeepExtrapolation_IsFlagged()
    {
        var assessment = StabilityKinetics.Assess(VersionId, FirstOrderData(0.002, 80_000, 50, 60));

        Assert.Contains(assessment.Warnings, w => w.Contains("extrapolated"));
        // Экстраполяция на 25 °C от 50 °C — это 25 °C вниз, дальше принятого предела.
        Assert.Equal(StabilityConfidence.Medium, assessment.Confidence);
    }

    [Fact(DisplayName = "Reference temperature inside the studied range is interpolation, not extrapolation")]
    public void ReferenceInsideRange_IsNotExtrapolation()
    {
        // Точки при 5 и 40 °C охватывают референсные 25 °C: перенос интерполяционный.
        var assessment = StabilityKinetics.Assess(VersionId, FirstOrderData(0.0005, 80_000, 5, 40));

        Assert.DoesNotContain(assessment.Warnings, w => w.Contains("extrapolated"));
        Assert.Equal(StabilityConfidence.High, assessment.Confidence);
        Assert.Equal(210.7, assessment.ShelfLifeDaysAt25C!.Value, 1);
    }
}
