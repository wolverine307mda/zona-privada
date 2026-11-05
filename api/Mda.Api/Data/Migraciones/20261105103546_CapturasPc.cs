using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class CapturasPc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "capturas_pc",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Origen = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Motivo = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Ancho = table.Column<int>(type: "integer", nullable: false),
                    Alto = table.Column<int>(type: "integer", nullable: false),
                    Tamano = table.Column<int>(type: "integer", nullable: false),
                    Datos = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capturas_pc", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_capturas_pc_Fecha",
                table: "capturas_pc",
                column: "Fecha");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "capturas_pc");
        }
    }
}
