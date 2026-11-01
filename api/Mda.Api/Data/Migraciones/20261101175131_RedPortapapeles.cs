using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class RedPortapapeles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dispositivos_red",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Host = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Puerto = table.Column<int>(type: "integer", nullable: false),
                    Icono = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Avisar = table.Column<bool>(type: "boolean", nullable: false),
                    EnLinea = table.Column<bool>(type: "boolean", nullable: true),
                    LatenciaMs = table.Column<int>(type: "integer", nullable: true),
                    Comprobado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Cambio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dispositivos_red", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "portapapeles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Texto = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Origen = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Creado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_portapapeles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_portapapeles_Creado",
                table: "portapapeles",
                column: "Creado");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dispositivos_red");

            migrationBuilder.DropTable(
                name: "portapapeles");
        }
    }
}
