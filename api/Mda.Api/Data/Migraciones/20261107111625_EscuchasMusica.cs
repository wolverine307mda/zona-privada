using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class EscuchasMusica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "escuchas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CancionId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Artistas = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Album = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Imagen = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Duracion = table.Column<int>(type: "integer", nullable: false),
                    Fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escuchas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_escuchas_CancionId_Fecha",
                table: "escuchas",
                columns: new[] { "CancionId", "Fecha" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escuchas_Fecha",
                table: "escuchas",
                column: "Fecha");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "escuchas");
        }
    }
}
