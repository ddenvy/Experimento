using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Experimento.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SecurityAndIntegrityIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Защита от дублей проекта у одного пользователя (двойной клик онбординга).
            migrationBuilder.CreateIndex(
                name: "IX_Projects_CreatedBy_Name",
                table: "Projects",
                columns: new[] { "CreatedBy", "Name" },
                unique: true);

            // Индекс под регистронезависимый поиск по каноническому имени в каталоге:
            // запрос сравнивает LOWER("CanonicalName") с нижним регистром строки.
            migrationBuilder.Sql(@"CREATE INDEX ""IX_ChemicalCatalog_Lower_CanonicalName""
                                   ON ""ChemicalCatalog"" (LOWER(""CanonicalName""))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Projects_CreatedBy_Name",
                table: "Projects");

            migrationBuilder.Sql(@"DROP INDEX ""IX_ChemicalCatalog_Lower_CanonicalName""");
        }
    }
}
