using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class PresupuestosSemanales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "presupuestos_semanales",
                columns: table => new
                {
                    Categoria = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Limite = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Arrastrar = table.Column<bool>(type: "boolean", nullable: false),
                    AvisadoSemana = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    AvisadoNivel = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_presupuestos_semanales", x => x.Categoria);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "presupuestos_semanales");
        }
    }
}
