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
        // Регистрируем конкретную версию модели; старые регистрации (v1) в существующих БД
        // не мешают добавить новую — консьюмер ищет модель по имени и версии.
        if (!await db.ModelRegistrations.AnyAsync(m => m.Name == "rule-based" && m.Version == "v2"))
        {
            db.ModelRegistrations.Add(new ModelRegistration
            {
                Name = "rule-based",
                Version = "v2",
                Description = "Deterministic rule-based property predictor using molar balance, proportion uniformity, " +
                              "temperature stability, PubChem-based structural hazard screening (formula + SMILES), and stabilizer checks.",
                ContextOfUse = "Initial screening of formulation success probability, toxicity, stability, and side risks. " +
                               "For hypothesis generation only; not for standalone regulatory decisions."
            });
            await db.SaveChangesAsync();
        }

        // Тестовый админ создаётся только при явно включённом флаге (локальный compose/e2e),
        // в обычном продакшене учётная запись с известным паролем не появится.
        if (seedTestAdmin)
        {
            // Создаём только отсутствующую учётку. Если email уже занят реальным пользователем —
            // не трогаем его и тем более не повышаем роль до Admin.
            if (!await db.Users.AnyAsync(u => u.Email == TestAdminEmail))
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
        }
    }
}
