using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class SaludBateria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "salud_bateria",
                columns: table => new
                {
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    Diseno = table.Column<long>(type: "bigint", nullable: false),
                    Completa = table.Column<long>(type: "bigint", nullable: false),
                    Ciclos = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_salud_bateria", x => x.Fecha);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "salud_bateria");
        }
    }
}
