using MediatR;

namespace Experimento.Application.Features.Auth;

public record RefreshCommand(string RefreshToken) : IRequest<AuthTokensDto>;
public record LogoutCommand(string RefreshToken) : IRequest<Unit>;

public class RefreshHandler : IRequestHandler<RefreshCommand, AuthTokensDto>
{
    private readonly IAuthService _authService;
    public RefreshHandler(IAuthService authService) => _authService = authService;
    public async Task<AuthTokensDto> Handle(RefreshCommand request, CancellationToken ct)
    {
        var (access, newRefresh) = await _authService.RefreshAsync(request.RefreshToken, ct);
        return new AuthTokensDto(access, newRefresh);
    }
}

public class LogoutHandler : IRequestHandler<LogoutCommand, Unit>
{
    private readonly IAuthService _authService;
    public LogoutHandler(IAuthService authService) => _authService = authService;
    public async Task<Unit> Handle(LogoutCommand request, CancellationToken ct)
    {
        await _authService.LogoutAsync(request.RefreshToken, ct);
        return Unit.Value;
    }
}
