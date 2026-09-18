using Experimento.Domain.Entities;

namespace Experimento.Domain.Tests;

/// <summary>
/// Инварианты исследования стабильности: без точек исследование бессмысленно,
/// а содержание вне диапазона (0, 100] — это ошибка ввода, а не данные.
/// </summary>
public class StabilityStudyTests
{
    private static StabilityStudy Study(params StabilityPoint[] points)
        => new() { VersionId = Guid.NewGuid(), Points = points.ToList() };

    private static StabilityPoint Point(double timeDays, double assayPercent, double temperature = 40)
        => new() { TemperatureCelsius = temperature, TimeDays = timeDays, AssayPercent = assayPercent };

    [Fact]
    public void ValidStudy_PassesValidation()
    {
        var study = Study(Point(0, 100), Point(90, 88.5), Point(90, 91.2, temperature: 50));

        study.EnsureValid();
    }

    [Fact]
    public void StudyWithoutPoints_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Study().EnsureValid());

        Assert.Contains("at least one measurement point", exception.Message);
    }

    [Fact]
    public void NegativeTime_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Study(Point(-1, 99)).EnsureValid());

        Assert.Contains("must not be negative", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100.5)]
    [InlineData(140)]
    public void AssayOutsideRange_Throws(double assay)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Study(Point(30, assay)).EnsureValid());

        Assert.Contains("outside the (0, 100] range", exception.Message);
    }

    [Fact]
    public void BoundaryAssays_AreAccepted()
    {
        // 100% — начальная точка, малые значения — глубокая деградация; обе границы допустимы.
        Study(Point(0, 100), Point(365, 0.5)).EnsureValid();
    }
}
