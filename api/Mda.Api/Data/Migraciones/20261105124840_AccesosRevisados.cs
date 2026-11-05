using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class AccesosRevisados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Revisado",
                table: "accesos",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Revisado",
                table: "accesos");
        }
    }
}
