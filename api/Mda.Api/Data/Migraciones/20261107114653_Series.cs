using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Series : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "titulos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Tipo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    IdExterno = table.Column<int>(type: "integer", nullable: true),
                    Nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Imagen = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Estado = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Temporada = table.Column<int>(type: "integer", nullable: false),
                    Episodio = table.Column<int>(type: "integer", nullable: false),
                    Nota = table.Column<int>(type: "integer", nullable: true),
                    Plataforma = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Generos = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Minutos = table.Column<int>(type: "integer", nullable: false),
                    Terminada = table.Column<bool>(type: "boolean", nullable: false),
                    Proximo = table.Column<DateOnly>(type: "date", nullable: true),
                    ProximaTemporada = table.Column<int>(type: "integer", nullable: false),
                    ProximoEpisodio = table.Column<int>(type: "integer", nullable: false),
                    AvisadoEstreno = table.Column<DateOnly>(type: "date", nullable: true),
                    AvisadoNivel = table.Column<int>(type: "integer", nullable: false),
                    Terminado = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_titulos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_titulos_IdExterno",
                table: "titulos",
                column: "IdExterno");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "titulos");
        }
    }
}
