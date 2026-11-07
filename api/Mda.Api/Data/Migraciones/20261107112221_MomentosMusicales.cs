using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class MomentosMusicales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "momentos_musicales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Icono = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    PlaylistUri = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    PlaylistNombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Desde = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    Hasta = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    Volumen = table.Column<int>(type: "integer", nullable: true),
                    Mezclar = table.Column<bool>(type: "boolean", nullable: false),
                    Orden = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_momentos_musicales", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "momentos_musicales");
        }
    }
}
