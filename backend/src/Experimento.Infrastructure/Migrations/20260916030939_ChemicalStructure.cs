using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Experimento.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChemicalStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Smiles",
                table: "FormulationComponents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Smiles",
                table: "ChemicalCatalog",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Smiles",
                table: "FormulationComponents");

            migrationBuilder.DropColumn(
                name: "Smiles",
                table: "ChemicalCatalog");
        }
    }
}
