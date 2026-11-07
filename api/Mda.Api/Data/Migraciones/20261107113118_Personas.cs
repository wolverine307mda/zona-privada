using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Personas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "personas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Apodo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Relacion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Telefonos = table.Column<string>(type: "text", nullable: false),
                    Correos = table.Column<string>(type: "text", nullable: false),
                    Direccion = table.Column<string>(type: "text", nullable: false),
                    Notas = table.Column<string>(type: "text", nullable: false),
                    CumpleDia = table.Column<int>(type: "integer", nullable: true),
                    CumpleMes = table.Column<int>(type: "integer", nullable: true),
                    CumpleAnio = table.Column<int>(type: "integer", nullable: true),
                    Gustos = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Avisar = table.Column<bool>(type: "boolean", nullable: false),
                    UltimaVez = table.Column<DateOnly>(type: "date", nullable: true),
                    AvisadoAnio = table.Column<int>(type: "integer", nullable: false),
                    AvisadoNivel = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "regalos_persona",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PersonaId = table.Column<int>(type: "integer", nullable: false),
                    Idea = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Precio = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    Ocasion = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: true),
                    Estado = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Sento = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regalos_persona", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_regalos_persona_PersonaId",
                table: "regalos_persona",
                column: "PersonaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "personas");

            migrationBuilder.DropTable(
                name: "regalos_persona");
        }
    }
}
