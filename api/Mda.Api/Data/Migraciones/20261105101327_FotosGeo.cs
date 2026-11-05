using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class FotosGeo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fotos_geo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Ruta = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Tamano = table.Column<long>(type: "bigint", nullable: false),
                    ModificadoTicks = table.Column<long>(type: "bigint", nullable: false),
                    Hash = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Lat = table.Column<double>(type: "double precision", nullable: true),
                    Lon = table.Column<double>(type: "double precision", nullable: true),
                    Captura = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fotos_geo", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fotos_geo_Ruta",
                table: "fotos_geo",
                column: "Ruta",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fotos_geo");
        }
    }
}
