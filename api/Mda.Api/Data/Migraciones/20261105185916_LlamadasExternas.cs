using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class LlamadasExternas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "llamadas_externas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Metodo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Ruta = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Consulta = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Origen = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Agente = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    DuracionMs = table.Column<int>(type: "integer", nullable: false),
                    Peticion = table.Column<string>(type: "character varying(2100)", maxLength: 2100, nullable: false),
                    Respuesta = table.Column<string>(type: "character varying(2100)", maxLength: 2100, nullable: false),
                    Error = table.Column<string>(type: "character varying(2100)", maxLength: 2100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_llamadas_externas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_llamadas_externas_Fecha",
                table: "llamadas_externas",
                column: "Fecha");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "llamadas_externas");
        }
    }
}
