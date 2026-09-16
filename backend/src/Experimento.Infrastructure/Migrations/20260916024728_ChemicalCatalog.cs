using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Experimento.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChemicalCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PubChemCid",
                table: "FormulationComponents",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChemicalCatalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PubChemCid = table.Column<int>(type: "integer", nullable: false),
                    CanonicalName = table.Column<string>(type: "text", nullable: false),
                    CasNumber = table.Column<string>(type: "text", nullable: true),
                    Formula = table.Column<string>(type: "text", nullable: true),
                    MolarMass = table.Column<double>(type: "double precision", nullable: false),
                    CachedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChemicalCatalog", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChemicalCatalog_CanonicalName",
                table: "ChemicalCatalog",
                column: "CanonicalName");

            migrationBuilder.CreateIndex(
                name: "IX_ChemicalCatalog_PubChemCid",
                table: "ChemicalCatalog",
                column: "PubChemCid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChemicalCatalog");

            migrationBuilder.DropColumn(
                name: "PubChemCid",
                table: "FormulationComponents");
        }
    }
}
