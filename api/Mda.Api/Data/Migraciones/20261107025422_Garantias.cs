using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Garantias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "garantias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MovimientoId = table.Column<int>(type: "integer", nullable: true),
                    Producto = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Comercio = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    FechaCompra = table.Column<DateOnly>(type: "date", nullable: false),
                    GarantiaHasta = table.Column<DateOnly>(type: "date", nullable: false),
                    DevolucionHasta = table.Column<DateOnly>(type: "date", nullable: true),
                    Estado = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Notas = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AvisadoGarantia = table.Column<int>(type: "integer", nullable: false),
                    AvisadoDevolucion = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_garantias", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_garantias_GarantiaHasta",
                table: "garantias",
                column: "GarantiaHasta");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "garantias");
        }
    }
}
