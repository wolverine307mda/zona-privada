using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Escenas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "escenas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Icono = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Energia = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Brillo = table.Column<int>(type: "integer", nullable: true),
                    Volumen = table.Column<int>(type: "integer", nullable: true),
                    Concentracion = table.Column<bool>(type: "boolean", nullable: true),
                    Despierto = table.Column<bool>(type: "boolean", nullable: true),
                    ApagarMinutos = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escenas", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "escenas");
        }
    }
}
