using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class ReglasCategoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reglas_categoria",
                columns: table => new
                {
                    Comercio = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Categoria = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Veces = table.Column<int>(type: "integer", nullable: false),
                    Actualizado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reglas_categoria", x => x.Comercio);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reglas_categoria");
        }
    }
}
