using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class EquiposConocidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "equipos_conocidos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Huella = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Pais = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Ciudad = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    UltimaIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Primera = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Ultima = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Entradas = table.Column<int>(type: "integer", nullable: false),
                    Confianza = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_equipos_conocidos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_equipos_conocidos_Huella",
                table: "equipos_conocidos",
                column: "Huella",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "equipos_conocidos");
        }
    }
}
