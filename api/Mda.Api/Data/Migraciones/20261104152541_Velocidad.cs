using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Velocidad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "velocidades",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Hora = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BajadaMbps = table.Column<double>(type: "double precision", nullable: false),
                    SubidaMbps = table.Column<double>(type: "double precision", nullable: false),
                    PingMs = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_velocidades", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_velocidades_Hora",
                table: "velocidades",
                column: "Hora");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "velocidades");
        }
    }
}
