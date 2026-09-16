using FluentValidation;
using MediatR;

namespace Experimento.Application.Features.Auth;

public record LoginCommand(string Email, string Password) : IRequest<(UserDto User, AuthTokensDto Tokens)>;

public class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public class LoginHandler : IRequestHandler<LoginCommand, (UserDto User, AuthTokensDto Tokens)>
{
    private readonly IAuthService _authService;
    public LoginHandler(IAuthService authService) => _authService = authService;

    public async Task<(UserDto User, AuthTokensDto Tokens)> Handle(LoginCommand request, CancellationToken ct)
    {
        var (user, access, refresh) = await _authService.LoginAsync(request.Email, request.Password, ct);
        return (new UserDto(user.Id, user.Email, user.DisplayName, user.Role.ToString()), new AuthTokensDto(access, refresh));
    }
}
