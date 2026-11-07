using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class ResumenClimaCasa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "casa_resumen_dia",
                columns: table => new
                {
                    EntidadId = table.Column<int>(type: "integer", nullable: false),
                    Dia = table.Column<DateOnly>(type: "date", nullable: false),
                    Min = table.Column<double>(type: "double precision", nullable: false),
                    Media = table.Column<double>(type: "double precision", nullable: false),
                    Max = table.Column<double>(type: "double precision", nullable: false),
                    Muestras = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_casa_resumen_dia", x => new { x.EntidadId, x.Dia });
                    table.ForeignKey(
                        name: "FK_casa_resumen_dia_casa_entidades_EntidadId",
                        column: x => x.EntidadId,
                        principalTable: "casa_entidades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "casa_resumen_dia");
        }
    }
}
