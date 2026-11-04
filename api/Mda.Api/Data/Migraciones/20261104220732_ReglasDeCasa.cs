using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class ReglasDeCasa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "casa_registro",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReglaId = table.Column<int>(type: "integer", nullable: true),
                    Fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Texto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Ok = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_casa_registro", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "casa_reglas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Activa = table.Column<bool>(type: "boolean", nullable: false),
                    SensorId = table.Column<int>(type: "integer", nullable: false),
                    SensorClave = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Operador = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    Umbral = table.Column<double>(type: "double precision", nullable: false),
                    Minutos = table.Column<int>(type: "integer", nullable: false),
                    DestinoId = table.Column<int>(type: "integer", nullable: false),
                    Accion = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Objetivo = table.Column<double>(type: "double precision", nullable: true),
                    Modo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Revertir = table.Column<bool>(type: "boolean", nullable: false),
                    Histeresis = table.Column<double>(type: "double precision", nullable: false),
                    EsperaMinutos = table.Column<int>(type: "integer", nullable: false),
                    SoloAvisar = table.Column<bool>(type: "boolean", nullable: false),
                    Disparada = table.Column<bool>(type: "boolean", nullable: false),
                    Desde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Ultima = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_casa_reglas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_casa_registro_Fecha",
                table: "casa_registro",
                column: "Fecha");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "casa_registro");

            migrationBuilder.DropTable(
                name: "casa_reglas");
        }
    }
}
