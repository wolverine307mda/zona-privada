using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Atajos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PorConfirmar",
                table: "movimientos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "tokens_atajo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Alcances = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Creado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UltimoUso = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UltimaIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Redes = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Revocado = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tokens_atajo", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tokens_atajo_Hash",
                table: "tokens_atajo",
                column: "Hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tokens_atajo");

            migrationBuilder.DropColumn(
                name: "PorConfirmar",
                table: "movimientos");
        }
    }
}
