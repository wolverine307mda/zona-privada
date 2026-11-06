using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Compra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "compra",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Cantidad = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Clave = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Comprado = table.Column<bool>(type: "boolean", nullable: false),
                    Creado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompradoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_compra", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "productos_habituales",
                columns: table => new
                {
                    Clave = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Nombre = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Veces = table.Column<int>(type: "integer", nullable: false),
                    Ultima = table.Column<DateOnly>(type: "date", nullable: false),
                    IntervaloDias = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_productos_habituales", x => x.Clave);
                });

            migrationBuilder.CreateIndex(
                name: "IX_compra_Comprado_Clave",
                table: "compra",
                columns: new[] { "Comprado", "Clave" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "compra");

            migrationBuilder.DropTable(
                name: "productos_habituales");
        }
    }
}
