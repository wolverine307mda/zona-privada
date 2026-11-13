using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class GoogleVariasCuentas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoogleCalendario",
                table: "eventos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleCuenta",
                table: "eventos",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoogleCalendario",
                table: "eventos");

            migrationBuilder.DropColumn(
                name: "GoogleCuenta",
                table: "eventos");
        }
    }
}
