using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Automatizaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "automatizaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Activa = table.Column<bool>(type: "boolean", nullable: false),
                    Disparador = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Umbral = table.Column<double>(type: "double precision", nullable: false),
                    Accion = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Texto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EsperaHoras = table.Column<int>(type: "integer", nullable: false),
                    Ultima = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UltimoValor = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automatizaciones", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "automatizaciones");
        }
    }
}
