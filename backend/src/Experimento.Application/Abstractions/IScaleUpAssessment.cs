using System.Text.Json.Serialization;

namespace Experimento.Application.Abstractions;

/// <summary>Серьёзность фактора риска при переносе процесса на укрупнённый масштаб.</summary>
public enum ScaleUpSeverity
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Один фактор риска масштабирования: что обнаружено и что с этим делать.
/// </summary>
public record ScaleUpFindingDto(
    string Factor,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ScaleUpSeverity Severity,
    string Observation,
    string Recommendation);

/// <summary>
/// Оценка готовности версии формуляции к масштабированию.
/// ReadinessScore: 100 — процесс переносится без изменений, 0 — требуется переработка.
/// </summary>
public record ScaleUpAssessmentDto(
    Guid VersionId,
    int VersionNumber,
    double LabReferenceVolumeLitres,
    double TargetVolumeLitres,
    double ScaleFactor,
    int ReadinessScore,
    string Verdict,
    IReadOnlyList<ScaleUpFindingDto> Findings);

/// <summary>
/// Детерминированная оценка масштабируемости версии: теплоотвод, газовыделение,
/// класс растворителя, pH процесса и полнота данных. Расчёт не обращается к БД,
/// поэтому правила проверяются точными тестами. Это скрининговый инструмент для
/// планирования пилотной партии, а не замена термической калориметрии.
/// </summary>
public interface IScaleUpAssessment
{
    ScaleUpAssessmentDto Assess(
        Guid versionId,
        int versionNumber,
        IReadOnlyList<FormulationComponent> components,
        FormulationConditions conditions,
        double targetVolumeLitres);
}
