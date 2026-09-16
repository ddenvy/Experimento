using FluentValidation;
using MediatR;

namespace Experimento.Application.Features.Auth;

public record RegisterCommand(string Email, string Password, string DisplayName) : IRequest<(UserDto User, AuthTokensDto Tokens)>;

public class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public RegisterValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(120);
    }
}

public class RegisterHandler : IRequestHandler<RegisterCommand, (UserDto User, AuthTokensDto Tokens)>
{
    private readonly IAuthService _authService;
    public RegisterHandler(IAuthService authService) => _authService = authService;

    public async Task<(UserDto User, AuthTokensDto Tokens)> Handle(RegisterCommand request, CancellationToken ct)
    {
        var (user, access, refresh) = await _authService.RegisterAsync(request.Email, request.Password, request.DisplayName, ct);
        return (new UserDto(user.Id, user.Email, user.DisplayName, user.Role.ToString()), new AuthTokensDto(access, refresh));
    }
}
