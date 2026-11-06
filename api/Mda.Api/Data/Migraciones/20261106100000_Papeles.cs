using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Papeles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "papeles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Categoria = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Vence = table.Column<DateOnly>(type: "date", nullable: false),
                    AvisarDias = table.Column<int>(type: "integer", nullable: false),
                    Notas = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AvisadoNivel = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_papeles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_papeles_Vence",
                table: "papeles",
                column: "Vence");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "papeles");
        }
    }
}
