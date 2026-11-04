using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class UbicacionMovimientos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Lat",
                table: "movimientos",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Lon",
                table: "movimientos",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Lat",
                table: "movimientos");

            migrationBuilder.DropColumn(
                name: "Lon",
                table: "movimientos");
        }
    }
}
