using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Servidor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aprobaciones_login",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Codigo = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    SecretoHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Creada = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Caduca = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Estado = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Agente = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Lugar = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Intentos = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aprobaciones_login", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ejecuciones_runbook",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunbookId = table.Column<int>(type: "integer", nullable: true),
                    Nombre = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Origen = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Fin = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Estado = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Pasos = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ejecuciones_runbook", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "eventos_servidor",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Hora = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Titulo = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Detalle = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Gravedad = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_eventos_servidor", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "runbooks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Descripcion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Pasos = table.Column<string>(type: "text", nullable: false),
                    Guardia = table.Column<bool>(type: "boolean", nullable: false),
                    Creado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runbooks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ejecuciones_runbook_Inicio",
                table: "ejecuciones_runbook",
                column: "Inicio");

            migrationBuilder.CreateIndex(
                name: "IX_eventos_servidor_Hora",
                table: "eventos_servidor",
                column: "Hora");

            migrationBuilder.CreateIndex(
                name: "IX_runbooks_Nombre",
                table: "runbooks",
                column: "Nombre",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aprobaciones_login");

            migrationBuilder.DropTable(
                name: "ejecuciones_runbook");

            migrationBuilder.DropTable(
                name: "eventos_servidor");

            migrationBuilder.DropTable(
                name: "runbooks");
        }
    }
}
