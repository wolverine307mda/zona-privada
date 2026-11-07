using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Cocina : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ingredientes_receta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecetaId = table.Column<int>(type: "integer", nullable: false),
                    Nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Cantidad = table.Column<double>(type: "double precision", nullable: true),
                    Unidad = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Texto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingredientes_receta", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "menu_dias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    Momento = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    RecetaId = table.Column<int>(type: "integer", nullable: true),
                    Texto = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Sobras = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_dias", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "recetas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Raciones = table.Column<int>(type: "integer", nullable: false),
                    Minutos = table.Column<int>(type: "integer", nullable: false),
                    Etiquetas = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Pasos = table.Column<string>(type: "text", nullable: false),
                    Origen = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recetas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ingredientes_receta_RecetaId",
                table: "ingredientes_receta",
                column: "RecetaId");

            migrationBuilder.CreateIndex(
                name: "IX_menu_dias_Fecha_Momento",
                table: "menu_dias",
                columns: new[] { "Fecha", "Momento" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ingredientes_receta");

            migrationBuilder.DropTable(
                name: "menu_dias");

            migrationBuilder.DropTable(
                name: "recetas");
        }
    }
}
