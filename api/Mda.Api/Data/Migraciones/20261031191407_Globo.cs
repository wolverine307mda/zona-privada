using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Globo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnGlobo",
                table: "fotos_publicas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "Lat",
                table: "fotos_publicas",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Lon",
                table: "fotos_publicas",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Lugar",
                table: "fotos_publicas",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnGlobo",
                table: "fotos_publicas");

            migrationBuilder.DropColumn(
                name: "Lat",
                table: "fotos_publicas");

            migrationBuilder.DropColumn(
                name: "Lon",
                table: "fotos_publicas");

            migrationBuilder.DropColumn(
                name: "Lugar",
                table: "fotos_publicas");
        }
    }
}
