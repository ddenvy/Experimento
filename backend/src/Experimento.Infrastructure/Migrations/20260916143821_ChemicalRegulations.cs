using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Experimento.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChemicalRegulations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChemicalRegulations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChemicalCatalogEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Authority = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    SourceUrl = table.Column<string>(type: "text", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChemicalRegulations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChemicalRegulations_ChemicalCatalog_ChemicalCatalogEntryId",
                        column: x => x.ChemicalCatalogEntryId,
                        principalTable: "ChemicalCatalog",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChemicalRegulations_Authority",
                table: "ChemicalRegulations",
                column: "Authority");

            migrationBuilder.CreateIndex(
                name: "IX_ChemicalRegulations_ChemicalCatalogEntryId_Authority",
                table: "ChemicalRegulations",
                columns: new[] { "ChemicalCatalogEntryId", "Authority" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChemicalRegulations");
        }
    }
}
