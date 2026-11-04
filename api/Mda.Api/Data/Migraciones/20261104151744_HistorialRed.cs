using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class HistorialRed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Historial",
                table: "dispositivos_red",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "red_horas",
                columns: table => new
                {
                    DispositivoId = table.Column<int>(type: "integer", nullable: false),
                    Hora = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Muestras = table.Column<int>(type: "integer", nullable: false),
                    Caidas = table.Column<int>(type: "integer", nullable: false),
                    MediaMs = table.Column<int>(type: "integer", nullable: true),
                    MaxMs = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_red_horas", x => new { x.DispositivoId, x.Hora });
                });

            migrationBuilder.CreateTable(
                name: "red_lecturas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DispositivoId = table.Column<int>(type: "integer", nullable: false),
                    Hora = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EnLinea = table.Column<bool>(type: "boolean", nullable: false),
                    Ms = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_red_lecturas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_red_lecturas_DispositivoId_Hora",
                table: "red_lecturas",
                columns: new[] { "DispositivoId", "Hora" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "red_horas");

            migrationBuilder.DropTable(
                name: "red_lecturas");

            migrationBuilder.DropColumn(
                name: "Historial",
                table: "dispositivos_red");
        }
    }
}
