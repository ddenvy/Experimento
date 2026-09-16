namespace Experimento.Application.DTOs;

public record UserDto(Guid Id, string Email, string DisplayName, string Role);
public record AuthTokensDto(string AccessToken, string RefreshToken);
public record ProjectDto(Guid Id, string Name, string? Description, DateTime CreatedAtUtc);
public record FormulationDto(Guid Id, Guid ProjectId, string Name, string TargetPurpose, int CurrentVersionNumber);
public record ComponentDto(Guid Id, string ChemicalName, string? CasNumber, string? Formula, double MolarMass, double Proportion, string? Role, int? PubChemCid);
public record ConditionsDto(double TemperatureCelsius, double? PressureKPa, double? PhTarget, string? Solvent, string? DeliveryTarget);
public record FormulationVersionDto(
    Guid Id, Guid FormulationId, int VersionNumber, string Status, string? Notes,
    DateTime CreatedAtUtc, IReadOnlyList<ComponentDto> Components, ConditionsDto Conditions);
public record ModelRegistrationDto(Guid Id, string Name, string Version, string Description, string ContextOfUse, DateTime RegisteredAtUtc);
public record PredictionJobDto(Guid Id, Guid VersionId, string Status, int Progress, string? Stage, DateTime CreatedAtUtc);
public record RationaleSourceDto(string Title, string Reference, string Type, double Similarity);
public record RationaleItemDto(Guid Id, string Category, string Claim, string Explanation, double Confidence, IReadOnlyList<RationaleSourceDto> Sources);
public record PredictionResultDto(
    Guid Id, Guid JobId, Guid ModelRegistrationId, string ModelDisplayName,
    double SuccessProbability, double ToxicityScore, double StabilityScore, string SideRiskLevel,
    string Summary, IReadOnlyList<RationaleItemDto> RationaleItems);
public record ReviewDto(Guid Id, string Decision, string? Comment, DateTime CreatedAtUtc);
public record OutcomeDto(Guid Id, bool ActualSuccess, string ActualMetricsJson, string? Notes, DateTime RecordedAtUtc);
public record SimulationJobDto(Guid Id, Guid VersionId, string Status, int Progress, DateTime CreatedAtUtc);
public record SimulationCandidateDto(Guid Id, int Rank, double SuccessProbability, double Score, string ParametersJson);
public record SimulationResultDto(Guid Id, Guid JobId, int IterationsExecuted, string Summary,
    SimulationCandidateDto? BestCandidate, IReadOnlyList<SimulationCandidateDto> TopCandidates);
public record KnowledgeDocumentDto(Guid Id, string Title, string SourceType, string Reference, string Status, DateTime UploadedAtUtc);
public record SearchResultDto(Guid ChunkId, string DocumentTitle, string Reference, string SourceType, string Content, double Similarity);
public record AuditEntryDto(long Id, DateTime TimestampUtc, Guid? ActorUserId, string Action, string EntityType, string? EntityId);
public record CalibrationStatsDto(int Total, int WithOutcome, double MeanError, double MeanBias);

// Сводки истории прогонов по версии формуляции (без тяжёлых rationale/кандидатов).
public record PredictionRunSummaryDto(
    Guid JobId, Guid? ResultId, string Status, string ModelDisplayName,
    double SuccessProbability, double ToxicityScore, double StabilityScore,
    string SideRiskLevel, bool HasOutcome, DateTime CreatedAtUtc);
public record SimulationRunSummaryDto(
    Guid JobId, Guid? ResultId, string Status, int IterationsExecuted,
    double? BestSuccessProbability, double? BestScore, DateTime CreatedAtUtc);

// Сводка последних прогонов пользователя для командного центра (дашборда).
public record RecentRunDto(
    string Kind, Guid JobId, string Status,
    Guid ProjectId, string ProjectName,
    Guid FormulationId, string FormulationName, int VersionNumber,
    double? Metric, DateTime CreatedAtUtc);
