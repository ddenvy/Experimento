using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Experimento.WebApi.Tests;

/// <summary>
/// In-process test server for the Experimento Web API.
/// Uses the host Postgres (localhost:5432) and RabbitMQ (localhost:5672)
/// exposed by docker-compose so the full HTTP + message-broker flow is exercised.
/// </summary>
public class ApiFixture : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Ensure we hit the host Postgres + RabbitMQ exposed by docker-compose,
            // regardless of any environment variables set in the shell.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] =
                    "Host=localhost;Port=5432;Database=experimento;Username=experimento;Password=experimento",
                ["RabbitMq:Host"] = "localhost",
                ["RabbitMq:Username"] = "experimento",
                ["RabbitMq:Password"] = "experimento"
            });
        });
    }
}
