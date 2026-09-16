using Experimento.Application.Features.Auth;
using Experimento.WebApi.Security;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Experimento.WebApi.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;
    public AuthController(IMediator mediator, ICurrentUser currentUser) => (_mediator, _currentUser) = (mediator, currentUser);

    private const string RefreshCookie = "experimento_refresh";

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterCommand command)
    {
        var (user, tokens) = await _mediator.Send(command);
        SetRefreshCookie(tokens.RefreshToken);
        return Ok(new { user, accessToken = tokens.AccessToken });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginCommand command)
    {
        var (user, tokens) = await _mediator.Send(command);
        SetRefreshCookie(tokens.RefreshToken);
        return Ok(new { user, accessToken = tokens.AccessToken });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        var refresh = Request.Cookies[RefreshCookie];
        if (string.IsNullOrEmpty(refresh))
            return Unauthorized(new { error = "No refresh token" });
        var tokens = await _mediator.Send(new RefreshCommand(refresh));
        SetRefreshCookie(tokens.RefreshToken);
        return Ok(new { accessToken = tokens.AccessToken });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var refresh = Request.Cookies[RefreshCookie];
        if (!string.IsNullOrEmpty(refresh))
            await _mediator.Send(new LogoutCommand(refresh));
        Response.Cookies.Delete(RefreshCookie);
        return Ok();
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        if (_currentUser.UserId == Guid.Empty)
            return Unauthorized();
        return Ok(new { id = _currentUser.UserId, email = _currentUser.Email });
    }

    private void SetRefreshCookie(string token)
    {
        Response.Cookies.Append(RefreshCookie, token, new CookieOptions
        {
            HttpOnly = true,
            // Cookie отправляется только по HTTPS. Локальная разработка по http
            // работает (Request.IsHttps == false), в проде за TLS флаг автоматически включится.
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Expires = DateTime.UtcNow.AddDays(7)
        });
    }
}
