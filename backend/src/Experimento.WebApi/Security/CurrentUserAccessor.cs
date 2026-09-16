using System.Security.Claims;

namespace Experimento.WebApi.Security;

/// <summary>
/// Provides the authenticated user's ID from the current HTTP context.
/// </summary>
public interface ICurrentUser
{
    Guid UserId { get; }
    string? Email { get; }
}

public class CurrentUserAccessor : ICurrentUser
{
    private readonly IHttpContextAccessor _http;
    public CurrentUserAccessor(IHttpContextAccessor http) => _http = http;

    public Guid UserId
    {
        get
        {
            var sub = _http.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? _http.HttpContext?.User.FindFirst("sub")?.Value;
            return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
        }
    }

    public string? Email => _http.HttpContext?.User.FindFirst(ClaimTypes.Email)?.Value
                            ?? _http.HttpContext?.User.FindFirst("email")?.Value;
}
