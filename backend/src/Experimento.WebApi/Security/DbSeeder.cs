using Experimento.Domain.Entities;
using Experimento.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Experimento.WebApi.Security;

/// <summary>
/// Seeds initial data: registers the rule-based prediction model.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (!await db.ModelRegistrations.AnyAsync())
        {
            db.ModelRegistrations.Add(new ModelRegistration
            {
                Name = "rule-based",
                Version = "v1",
                Description = "Deterministic rule-based property predictor using molar balance, proportion uniformity, " +
                              "temperature stability, toxicophore detection, and stabilizer checks.",
                ContextOfUse = "Initial screening of formulation success probability, toxicity, stability, and side risks. " +
                               "For hypothesis generation only; not for standalone regulatory decisions."
            });
            await db.SaveChangesAsync();
        }
    }
}
