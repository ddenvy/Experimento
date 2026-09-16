namespace Experimento.Domain.Entities;

/// <summary>
/// A project workspace; access boundary for trade-secret data.
/// </summary>
public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Formulation> Formulations { get; set; } = new List<Formulation>();
}
