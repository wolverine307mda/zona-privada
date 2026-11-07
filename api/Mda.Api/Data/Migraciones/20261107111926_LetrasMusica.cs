using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class LetrasMusica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "letras",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CancionId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Sincronizada = table.Column<string>(type: "text", nullable: false),
                    Plana = table.Column<string>(type: "text", nullable: false),
                    Encontrada = table.Column<bool>(type: "boolean", nullable: false),
                    Fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_letras", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_letras_CancionId",
                table: "letras",
                column: "CancionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "letras");
        }
    }
}
