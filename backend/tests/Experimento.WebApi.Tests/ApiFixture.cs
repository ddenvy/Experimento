using Experimento.Domain.Entities;
using Experimento.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Experimento.WebApi.Tests;

/// <summary>
/// In-process test server for the Experimento Web API.
/// Uses the host Postgres (localhost:5432) and RabbitMQ (localhost:5672)
/// exposed by docker-compose so the full HTTP + message-broker flow is exercised.
/// </summary>
public class ApiFixture : WebApplicationFactory<Program>
{
    public const int AspirinCid = 2244;
    public const int SodiumChlorideCid = 5234;

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

    public ApiFixture()
    {
        // Сеем каталог достоверными записями PubChem, чтобы тесты версий не зависели
        // от доступности внешнего API и не тратили его лимит запросов.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        SeedCatalog(db);
    }

    private static void SeedCatalog(AppDbContext db)
    {
        var seeds = new[]
        {
            new ChemicalCatalogEntry
            {
                PubChemCid = AspirinCid, CanonicalName = "Aspirin", CasNumber = "50-78-2",
                Formula = "C9H8O4", MolarMass = 180.16
            },
            new ChemicalCatalogEntry
            {
                PubChemCid = SodiumChlorideCid, CanonicalName = "Sodium chloride", CasNumber = "7647-14-5",
                Formula = "ClNa", MolarMass = 58.44
            }
        };

        foreach (var seed in seeds)
        {
            if (!db.ChemicalCatalog.Any(e => e.PubChemCid == seed.PubChemCid))
                db.ChemicalCatalog.Add(seed);
        }
        db.SaveChanges();
    }
}
