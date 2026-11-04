using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Secretos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "secretos",
                columns: table => new
                {
                    Servicio = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Nombre = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Valor = table.Column<string>(type: "text", nullable: false),
                    Actualizado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_secretos", x => new { x.Servicio, x.Nombre });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "secretos");
        }
    }
}
