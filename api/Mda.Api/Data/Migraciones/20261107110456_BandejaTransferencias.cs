using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class BandejaTransferencias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bandeja_transferencias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Direccion = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Tamano = table.Column<long>(type: "bigint", nullable: false),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Creada = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Caduca = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Archivo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Mensaje = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bandeja_transferencias", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bandeja_transferencias_Caduca",
                table: "bandeja_transferencias",
                column: "Caduca");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bandeja_transferencias");
        }
    }
}
