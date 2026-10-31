using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class BloqueoSesion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Bloqueada",
                table: "sesiones",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "BloquearTrasSegundos",
                table: "sesiones",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SegundoPlano",
                table: "sesiones",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bloqueada",
                table: "sesiones");

            migrationBuilder.DropColumn(
                name: "BloquearTrasSegundos",
                table: "sesiones");

            migrationBuilder.DropColumn(
                name: "SegundoPlano",
                table: "sesiones");
        }
    }
}
