using Experimento.Domain.Enums;

namespace Experimento.Domain.Entities;

/// <summary>
/// A scientist user of the platform.
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Scientist;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
