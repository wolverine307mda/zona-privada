using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Recordatorios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CanalesEnviados",
                table: "eventos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CanalesRendidos",
                table: "eventos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Intentos",
                table: "eventos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReintentoDesde",
                table: "eventos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UltimaOcurrenciaAvisada",
                table: "eventos",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanalesEnviados",
                table: "eventos");

            migrationBuilder.DropColumn(
                name: "CanalesRendidos",
                table: "eventos");

            migrationBuilder.DropColumn(
                name: "Intentos",
                table: "eventos");

            migrationBuilder.DropColumn(
                name: "ReintentoDesde",
                table: "eventos");

            migrationBuilder.DropColumn(
                name: "UltimaOcurrenciaAvisada",
                table: "eventos");
        }
    }
}
