using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Tareas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tareas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Titulo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Notas = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Prioridad = table.Column<int>(type: "integer", nullable: false),
                    Vence = table.Column<DateOnly>(type: "date", nullable: true),
                    Hecha = table.Column<bool>(type: "boolean", nullable: false),
                    Creada = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    HechaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AvisadoFecha = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tareas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tareas_Hecha_Vence",
                table: "tareas",
                columns: new[] { "Hecha", "Vence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tareas");
        }
    }
}
