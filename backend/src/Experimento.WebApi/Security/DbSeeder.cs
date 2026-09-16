using Experimento.Domain.Entities;
using Experimento.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Experimento.WebApi.Security;

/// <summary>
/// Seeds initial data: rule-based prediction model and (опционально, под флагом Seed:TestAdmin)
/// тестовый администратор для локальной разработки и e2e-тестов Playwright.
/// </summary>
public static class DbSeeder
{
    public const string TestAdminEmail = "apple@apple.com";
    public const string TestAdminPassword = "Test12345!";

    public static async Task SeedAsync(AppDbContext db, bool seedTestAdmin = false)
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

        // Тестовый админ создаётся только при явно включённом флаге (локальный compose/e2e),
        // в обычном продакшене учётная запись с известным паролем не появится.
        if (seedTestAdmin)
        {
            var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == TestAdminEmail);
            if (existing is null)
            {
                db.Users.Add(new User
                {
                    Email = TestAdminEmail,
                    DisplayName = "E2E Admin",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(TestAdminPassword),
                    Role = Domain.Enums.UserRole.Admin
                });
                await db.SaveChangesAsync();
            }
            else if (existing.Role != Domain.Enums.UserRole.Admin)
            {
                existing.Role = Domain.Enums.UserRole.Admin;
                await db.SaveChangesAsync();
            }
        }
    }
}
