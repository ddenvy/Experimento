using Experimento.Infrastructure.Chemicals;
using Experimento.Infrastructure.Data;
using Experimento.Infrastructure.Knowledge;
using Experimento.Infrastructure.Predictions;
using Experimento.Infrastructure.Security;
using Experimento.Infrastructure.Simulations;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Experimento.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        // Data
        var connectionString = config.GetConnectionString("Default")
            ?? "Host=localhost;Port=5432;Database=experimento;Username=experimento;Password=experimento";
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, o => o.UseVector()));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // Audit
        services.AddScoped<IAuditTrail, AuditTrailRepository>();

        // Auth
        services.AddScoped<IAuthService, AuthService>();

        // Predictions
        services.AddScoped<IPropertyPredictor, RuleBasedPropertyPredictor>();

        // Simulations
        services.AddScoped<SimulationEngine>();

        // Knowledge
        services.AddScoped<ChunkingService>();
        services.AddScoped<IVectorSearchService, VectorSearchService>();
        services.AddSingleton<IDocumentTextExtractor, Knowledge.Extraction.DocumentTextExtractor>();

        // Chemical catalog (PubChem): in-memory cache for suggestions, DB cache for resolved substances.
        services.AddMemoryCache();
        services.AddHttpClient("pubchem", client =>
        {
            client.BaseAddress = new Uri("https://pubchem.ncbi.nlm.nih.gov");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddScoped<IChemicalCatalogService, PubChemCatalogService>();
        services.AddScoped<ISubstituteFinder, SubstituteFinder>();

        // MassTransit + RabbitMQ
        var rabbitHost = config["RabbitMq:Host"] ?? "localhost";
        var rabbitUser = config["RabbitMq:Username"] ?? "experimento";
        var rabbitPass = config["RabbitMq:Password"] ?? "experimento";
        services.AddMassTransit(x =>
        {
            x.AddConsumer<Messaging.PredictionConsumer>();
            x.AddConsumer<Messaging.SimulationConsumer>();
            x.AddConsumer<Messaging.DocumentIngestionConsumer>();
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(rabbitHost, "/", h =>
                {
                    h.Username(rabbitUser);
                    h.Password(rabbitPass);
                });
                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
