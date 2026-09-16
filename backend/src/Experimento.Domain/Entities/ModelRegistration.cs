namespace Experimento.Domain.Entities;

/// <summary>
/// A registered prediction model with its declared context of use (FDA credibility assessment).
/// </summary>
public class ModelRegistration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ContextOfUse { get; set; } = string.Empty;
    public DateTime RegisteredAtUtc { get; set; } = DateTime.UtcNow;

    public string DisplayName => $"{Name} ({Version})";
}
