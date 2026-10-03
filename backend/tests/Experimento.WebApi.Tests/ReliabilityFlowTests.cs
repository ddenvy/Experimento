using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Experimento.Application.Abstractions;
using Experimento.Application.Messaging;
using Experimento.Domain.Entities;
using Experimento.Domain.Enums;
using Experimento.Infrastructure.Data;
using Experimento.Infrastructure.Messaging;
using Experimento.WebApi.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Experimento.WebApi.Tests;

/// <summary>Проверяет атомарность операций, защиту журнала и восстановление задач.</summary>
public class ReliabilityFlowTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _factory;
    public ReliabilityFlowTests(ApiFixture factory) => _factory = factory;

    [Fact]
    public async Task AuditEntry_RejectsUpdateDeleteAndTruncate()
    {
        using var scope = _factory.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditTrail>();
        var entry = await audit.AppendAsync(null, "Reliability.Check", "Test", null, "{}");
        Assert.Null(await audit.VerifyChainAsync());

        await AssertAuditMutationRejectedAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"AuditEntries\" SET \"Action\" = {'x'} WHERE \"Id\" = {entry.Id}"));
        await AssertAuditMutationRejectedAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"AuditEntries\" WHERE \"Id\" = {entry.Id}"));
        await AssertAuditMutationRejectedAsync(db => db.Database.ExecuteSqlRawAsync("TRUNCATE \"AuditEntries\""));

        Assert.Null(await audit.VerifyChainAsync());
    }

    [Fact]
    public async Task ConcurrentAuditAppends_PreserveOneChain()
    {
        var tasks = Enumerable.Range(0, 8).Select(async index =>
        {
            using var scope = _factory.Services.CreateScope();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditTrail>();
            return await audit.AppendAsync(null, "Reliability.Concurrent", "Test", index.ToString(), "{}");
        });
        var entries = await Task.WhenAll(tasks);
        Assert.Equal(8, entries.Select(e => e.Id).Distinct().Count());

        using var verifyScope = _factory.Services.CreateScope();
        var verifier = verifyScope.ServiceProvider.GetRequiredService<IAuditTrail>();
        Assert.Null(await verifier.VerifyChainAsync());
    }

    [Fact]
    public async Task FailedAudit_RollsBackProjectCreation()
    {
        using var factory = new FailingAuditFixture();
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = DbSeeder.TestAdminEmail, password = DbSeeder.TestAdminPassword });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", body.GetProperty("accessToken").GetString());

        var name = $"Rollback-{Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync("/api/projects", new { name, description = "rollback check" });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Projects.AnyAsync(p => p.Name == name));
    }

    [Fact]
    public async Task StalledDocument_IsRequeuedWithItsOriginalContent()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Where(u => u.Email == DbSeeder.TestAdminEmail)
            .Select(u => u.Id).SingleAsync();
        var document = new KnowledgeDocument
        {
            Title = $"Recovery-{Guid.NewGuid():N}",
            SourceType = SourceType.InternalExperiment,
            Reference = "test",
            UploadedBy = userId,
            Status = KnowledgeStatus.Processing,
            StartedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        var payload = JsonSerializer.Serialize(new IngestDocumentCommand(document.Id, "Recovered content"));
        db.KnowledgeDocuments.Add(document);
        db.OutboxMessages.Add(new OutboxMessage
        {
            Kind = OutboxKinds.Document,
            EntityId = document.Id,
            PayloadJson = payload,
            PublishedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var recovery = new StalledJobRecovery(_factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StalledJobRecovery>.Instance);
        await recovery.RecoverAsync();

        var refreshed = await db.KnowledgeDocuments.AsNoTracking().SingleAsync(d => d.Id == document.Id);
        Assert.Contains(refreshed.Status, new[]
        {
            KnowledgeStatus.Pending, KnowledgeStatus.Processing, KnowledgeStatus.Ready
        });
        Assert.Equal(1, refreshed.AttemptCount);
        var messages = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.Kind == OutboxKinds.Document && m.EntityId == document.Id).ToListAsync();
        Assert.Equal(2, messages.Count);
        Assert.All(messages, m => Assert.Equal(payload, m.PayloadJson));
    }

    [Fact]
    public async Task PendingDocument_WithOldPublishedMessage_IsRequeuedOnce()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Where(u => u.Email == DbSeeder.TestAdminEmail)
            .Select(u => u.Id).SingleAsync();
        var oldTime = DateTime.UtcNow.AddHours(-1);
        var document = new KnowledgeDocument
        {
            Title = $"Undelivered-{Guid.NewGuid():N}",
            SourceType = SourceType.InternalExperiment,
            Reference = "test",
            UploadedBy = userId,
            UploadedAtUtc = oldTime,
            Status = KnowledgeStatus.Pending
        };
        var message = OutboxMessage.Create(OutboxKinds.Document, document.Id,
            JsonSerializer.Serialize(new IngestDocumentCommand(document.Id, "Original content")));
        message.CreatedAtUtc = oldTime;
        message.PublishedAtUtc = oldTime;
        db.KnowledgeDocuments.Add(document);
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var recovery = new StalledJobRecovery(_factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StalledJobRecovery>.Instance);
        await recovery.RecoverAsync();
        await recovery.RecoverAsync();

        var refreshed = await db.KnowledgeDocuments.AsNoTracking().SingleAsync(d => d.Id == document.Id);
        Assert.Contains(refreshed.Status, new[]
        {
            KnowledgeStatus.Pending, KnowledgeStatus.Processing, KnowledgeStatus.Ready
        });
        Assert.Equal(0, refreshed.AttemptCount);
        Assert.Equal(2, await db.OutboxMessages.CountAsync(m =>
            m.Kind == OutboxKinds.Document && m.EntityId == document.Id));
    }

    private async Task AssertAuditMutationRejectedAsync(Func<AppDbContext, Task<int>> mutate)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<PostgresException>(() => mutate(db));
        await tx.RollbackAsync();
    }

    private sealed class FailingAuditFixture : ApiFixture
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuditTrail>();
                services.AddScoped<IAuditTrail, FailingAuditTrail>();
            });
        }
    }

    private sealed class FailingAuditTrail : IAuditTrail
    {
        public Task<AuditEntry> AppendAsync(Guid? actorUserId, string action, string entityType,
            string? entityId, string payloadJson, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Audit write failed for test.");

        public Task<long?> VerifyChainAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<long?>(null);

        public Task<IReadOnlyList<AuditEntry>> GetTrailAsync(string? entityType, string? entityId,
            int skip, int take, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AuditEntry>>([]);
    }
}
