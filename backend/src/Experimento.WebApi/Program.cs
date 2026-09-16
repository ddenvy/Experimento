using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Experimento.Ai;
using Experimento.Application.Abstractions;
using Experimento.Application.Behaviors;
using Experimento.Application.Exceptions;
using Experimento.Infrastructure;
using Experimento.Infrastructure.Data;
using Experimento.WebApi.Hubs;
using Experimento.WebApi.Security;
using FluentValidation;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// MediatR + FluentValidation.
// AddOpenBehavior включает ValidationBehavior для всех запросов — без этого
// зарегистрированные валидаторы никогда не вызывались.
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Experimento.Application.DTOs.UserDto).Assembly);
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});
builder.Services.AddValidatorsFromAssembly(typeof(Experimento.Application.DTOs.UserDto).Assembly);

// Infrastructure + Ai
builder.Services.AddInfrastructure(config);
builder.Services.AddAiServices(config);

// Controllers
builder.Services.AddControllers();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((doc, _, _) =>
{
    doc.Info.Title = "Experimento API";
    doc.Info.Version = "v1";
    return Task.CompletedTask;
}));

// JWT auth.
// Ключ берётся ТОЛЬКО из окружения/конфигурации. В Production отсутствие ключа или
// ключа короче 256 бит — не запускаемся. Для Development допустим явно небезопасный dev-ключ.
var jwtKey = config["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
{
    if (builder.Environment.IsDevelopment())
    {
        jwtKey = "experimento-development-only-key-do-not-use-in-production-0123456789";
        // Пишем фолбэк обратно в конфигурацию, чтобы AuthService (IConfiguration["Jwt:Key"])
        // и JWT Bearer использовали один и тот же ключ.
        config["Jwt:Key"] = jwtKey;
    }
    else
    {
        throw new InvalidOperationException(
            "Jwt:Key is required in production. Set the Jwt__Key environment variable.");
    }
}

if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException("Jwt:Key must be at least 32 bytes (256 bits) for HMAC SHA-256.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = config["Jwt:Issuer"] ?? "Experimento",
            ValidAudience = config["Jwt:Audience"] ?? "Experimento",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };

        // SignalR не может передавать Authorization-заголовок из браузерного WebSocket —
        // токен принимается из query string access_token только на /hubs.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// SignalR for live job progress
builder.Services.AddSignalR();

// Current user accessor
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUserAccessor>();

// Проверки горизонтальных прав (владение ресурсами).
builder.Services.AddScoped<ResourceAuthorization>();

// Job notifier (SignalR-based)
builder.Services.AddScoped<IJobNotifier, SignalRJobNotifier>();

// CORS: список доверенных origin из конфигурации (Cors:Origins), без хардкода.
var corsOrigins = config.GetSection("Cors:Origins").Get<string[]>()
                  ?? new[] { "http://localhost:3000" };
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
        policy.WithOrigins(corsOrigins)
            .AllowAnyHeader().AllowAnyMethod().AllowCredentials());
});

// Rate limiting: строгая политика для auth-эндпоинтов (брутфорс паролей) и
// общий лимит для остального API. Раздел по IP для анонимов, по userId для авторизованных.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Лимит auth-эндпоинтов конфигурируем (тестовое окружение делает много логинов подряд).
    var authPermitLimit = builder.Configuration.GetValue("RateLimits:AuthPermitPerMinute", 20);

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var path = httpContext.Request.Path;
        var identity = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var partitionKey = identity is not null ? $"user:{identity}" : $"ip:{ip}";

        var isAuthEndpoint =
            path.StartsWithSegments("/api/auth/login") ||
            path.StartsWithSegments("/api/auth/register") ||
            path.StartsWithSegments("/api/auth/refresh");

        return isAuthEndpoint
            ? RateLimitPartition.GetFixedWindowLimiter(
                $"auth:{partitionKey}",
                _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = authPermitLimit,
                    QueueLimit = 0
                })
            : RateLimitPartition.GetTokenBucketLimiter(
                partitionKey,
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 300,
                    TokensPerPeriod = 100,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                    QueueLimit = 0
                });
    });
});

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

// Применение миграций EF и сидов (в любом окружении схема обновляется через миграции,
// без EnsureCreated — он несовместим с миграциями и пропускает __EFMigrationsHistory).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    // В Development тестовый админ создаётся всегда; в Production — только по флагу Seed:TestAdmin.
    var seedTestAdmin = app.Environment.IsDevelopment() || config.GetValue<bool>("Seed:TestAdmin");
    await DbSeeder.SeedAsync(db, seedTestAdmin);
}

// Pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors("frontend");

// Global exception handler — converts domain exceptions to clean HTTP responses.
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
        .CreateLogger("ExceptionHandler");
    try
    {
        await next();
    }
    catch (NotFoundException ex)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (ConflictException ex)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (ForbiddenException ex)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (BadRequestException ex)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        // Доменные EnsureValid-проверки сообщают о невалидном запросе клиента.
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (UnauthorizedAccessException ex)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (ValidationException ex)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Validation failed",
            details = ex.Errors.Select(e => new { e.PropertyName, e.ErrorMessage })
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "Internal server error" });
    }
});

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHub<JobsHub>("/hubs/jobs").RequireAuthorization();
app.MapHealthChecks("/health");

app.Run();

// Make the Program class accessible to the integration test project (WebApplicationFactory).
public partial class Program { }
