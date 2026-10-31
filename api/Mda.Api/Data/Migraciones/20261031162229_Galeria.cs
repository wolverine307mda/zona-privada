using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Galeria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fotos_publicas",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Raiz = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Ruta = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Titulo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Descripcion = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Alt = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Categoria = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Orden = table.Column<int>(type: "integer", nullable: false),
                    Ancho = table.Column<int>(type: "integer", nullable: false),
                    Alto = table.Column<int>(type: "integer", nullable: false),
                    Tono = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    Hash = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Publicada = table.Column<bool>(type: "boolean", nullable: false),
                    Creada = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fotos_publicas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fotos_publicas_Orden",
                table: "fotos_publicas",
                column: "Orden");

            migrationBuilder.CreateIndex(
                name: "IX_fotos_publicas_Ruta",
                table: "fotos_publicas",
                column: "Ruta",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fotos_publicas");
        }
    }
}
