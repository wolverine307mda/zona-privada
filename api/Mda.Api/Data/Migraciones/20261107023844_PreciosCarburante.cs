using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class PreciosCarburante : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "precios_carburante",
                columns: table => new
                {
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    Combustible = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Media = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    MinimoCerca = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_precios_carburante", x => new { x.Fecha, x.Combustible });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "precios_carburante");
        }
    }
}
