using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Contexto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reglas_contexto",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Activa = table.Column<bool>(type: "boolean", nullable: false),
                    Evento = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Desde = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    Hasta = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    Dias = table.Column<int>(type: "integer", nullable: false),
                    Tiempo = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    SoloEnCasa = table.Column<bool>(type: "boolean", nullable: false),
                    EscenaCasaId = table.Column<int>(type: "integer", nullable: true),
                    EscenaPcId = table.Column<int>(type: "integer", nullable: true),
                    Aviso = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EsperaMinutos = table.Column<int>(type: "integer", nullable: false),
                    Ultima = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Resultado = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reglas_contexto", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reglas_contexto");
        }
    }
}
