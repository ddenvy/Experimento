using System;
using Experimento.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Experimento.Infrastructure.Migrations;

/// <summary>Добавляет журнал отправки задач и запрет изменений записей аудита.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261003000000_ReliableJobsAndAudit")]
public partial class ReliableJobsAndAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("AttemptCount", "PredictionJobs", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>("AttemptCount", "SimulationJobs", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>("AttemptCount", "KnowledgeDocuments", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<DateTime>("StartedAtUtc", "KnowledgeDocuments", type: "timestamp with time zone", nullable: true);

        migrationBuilder.CreateTable(
            name: "OutboxMessages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "text", nullable: false),
                EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                PayloadJson = table.Column<string>(type: "text", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                AvailableAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LeaseUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                AttemptCount = table.Column<int>(type: "integer", nullable: false),
                LastError = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_OutboxMessages", x => x.Id));

        migrationBuilder.CreateIndex("IX_OutboxMessages_PublishedAtUtc_AvailableAtUtc_LeaseUntilUtc",
            "OutboxMessages", new[] { "PublishedAtUtc", "AvailableAtUtc", "LeaseUntilUtc" });
        migrationBuilder.CreateIndex("IX_OutboxMessages_Kind_EntityId",
            "OutboxMessages", new[] { "Kind", "EntityId" });
        migrationBuilder.CreateIndex("IX_PredictionJobs_Status_StartedAtUtc",
            "PredictionJobs", new[] { "Status", "StartedAtUtc" });
        migrationBuilder.CreateIndex("IX_SimulationJobs_Status_StartedAtUtc",
            "SimulationJobs", new[] { "Status", "StartedAtUtc" });
        migrationBuilder.CreateIndex("IX_KnowledgeDocuments_Status_StartedAtUtc",
            "KnowledgeDocuments", new[] { "Status", "StartedAtUtc" });

        // Уникальный предшественник не позволяет создать две ветки одной цепочки.
        migrationBuilder.CreateIndex("IX_AuditEntries_PreviousHash", "AuditEntries", "PreviousHash", unique: true);
        migrationBuilder.Sql(@"
            CREATE FUNCTION reject_audit_change() RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'Audit entries are immutable';
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER audit_no_update_delete
                BEFORE UPDATE OR DELETE ON ""AuditEntries""
                FOR EACH ROW EXECUTE FUNCTION reject_audit_change();
            CREATE TRIGGER audit_no_truncate
                BEFORE TRUNCATE ON ""AuditEntries""
                FOR EACH STATEMENT EXECUTE FUNCTION reject_audit_change();");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            DROP TRIGGER audit_no_truncate ON ""AuditEntries"";
            DROP TRIGGER audit_no_update_delete ON ""AuditEntries"";
            DROP FUNCTION reject_audit_change();");
        migrationBuilder.DropIndex("IX_AuditEntries_PreviousHash", "AuditEntries");
        migrationBuilder.DropIndex("IX_PredictionJobs_Status_StartedAtUtc", "PredictionJobs");
        migrationBuilder.DropIndex("IX_SimulationJobs_Status_StartedAtUtc", "SimulationJobs");
        migrationBuilder.DropIndex("IX_KnowledgeDocuments_Status_StartedAtUtc", "KnowledgeDocuments");
        migrationBuilder.DropTable("OutboxMessages");
        migrationBuilder.DropColumn("AttemptCount", "PredictionJobs");
        migrationBuilder.DropColumn("AttemptCount", "SimulationJobs");
        migrationBuilder.DropColumn("AttemptCount", "KnowledgeDocuments");
        migrationBuilder.DropColumn("StartedAtUtc", "KnowledgeDocuments");
    }
}
