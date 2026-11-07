using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Bienestar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bienestar_dias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    Datos = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bienestar_dias", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "bienestar_entrenos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    Datos = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bienestar_entrenos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "bienestar_medidas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    Datos = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bienestar_medidas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "bienestar_perfil",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Datos = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bienestar_perfil", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "bienestar_planes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false),
                    Datos = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bienestar_planes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bienestar_dias_Fecha",
                table: "bienestar_dias",
                column: "Fecha",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bienestar_entrenos_Fecha",
                table: "bienestar_entrenos",
                column: "Fecha");

            migrationBuilder.CreateIndex(
                name: "IX_bienestar_medidas_Fecha",
                table: "bienestar_medidas",
                column: "Fecha",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bienestar_dias");

            migrationBuilder.DropTable(
                name: "bienestar_entrenos");

            migrationBuilder.DropTable(
                name: "bienestar_medidas");

            migrationBuilder.DropTable(
                name: "bienestar_perfil");

            migrationBuilder.DropTable(
                name: "bienestar_planes");
        }
    }
}
