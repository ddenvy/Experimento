using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Experimento.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StabilityStudies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StabilityStudies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StabilityStudies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StabilityStudies_FormulationVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "FormulationVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StabilityPoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemperatureCelsius = table.Column<double>(type: "double precision", nullable: false),
                    TimeDays = table.Column<double>(type: "double precision", nullable: false),
                    AssayPercent = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StabilityPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StabilityPoints_StabilityStudies_StudyId",
                        column: x => x.StudyId,
                        principalTable: "StabilityStudies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StabilityPoints_StudyId",
                table: "StabilityPoints",
                column: "StudyId");

            migrationBuilder.CreateIndex(
                name: "IX_StabilityStudies_VersionId",
                table: "StabilityStudies",
                column: "VersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StabilityPoints");

            migrationBuilder.DropTable(
                name: "StabilityStudies");
        }
    }
}
