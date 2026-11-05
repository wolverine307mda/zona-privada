using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Habitos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "habitos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Icono = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Dias = table.Column<int>(type: "integer", nullable: false),
                    Objetivo = table.Column<int>(type: "integer", nullable: false),
                    Unidad = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    HoraAviso = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    AvisadoFecha = table.Column<DateOnly>(type: "date", nullable: true),
                    Archivado = table.Column<bool>(type: "boolean", nullable: false),
                    Creado = table.Column<DateOnly>(type: "date", nullable: false),
                    Orden = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_habitos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "registros_habito",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HabitoId = table.Column<int>(type: "integer", nullable: false),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    Cantidad = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registros_habito", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_registros_habito_Fecha",
                table: "registros_habito",
                column: "Fecha");

            migrationBuilder.CreateIndex(
                name: "IX_registros_habito_HabitoId_Fecha",
                table: "registros_habito",
                columns: new[] { "HabitoId", "Fecha" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "habitos");

            migrationBuilder.DropTable(
                name: "registros_habito");
        }
    }
}
