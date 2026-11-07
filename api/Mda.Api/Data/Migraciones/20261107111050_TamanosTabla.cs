using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class TamanosTabla : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tamanos_tabla",
                columns: table => new
                {
                    Dia = table.Column<DateOnly>(type: "date", nullable: false),
                    Tabla = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tamanos_tabla", x => new { x.Dia, x.Tabla });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tamanos_tabla");
        }
    }
}
