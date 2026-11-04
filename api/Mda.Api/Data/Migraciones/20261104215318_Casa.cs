using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Casa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "casa_estancias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Icono = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Orden = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_casa_estancias", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "casa_paneles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Clave = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_casa_paneles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "casa_aparatos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Driver = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EstanciaId = table.Column<int>(type: "integer", nullable: true),
                    Config = table.Column<string>(type: "text", nullable: false),
                    Creado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_casa_aparatos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_casa_aparatos_casa_estancias_EstanciaId",
                        column: x => x.EstanciaId,
                        principalTable: "casa_estancias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "casa_entidades",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AparatoId = table.Column<int>(type: "integer", nullable: false),
                    Clave = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Valor = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Actualizado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_casa_entidades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_casa_entidades_casa_aparatos_AparatoId",
                        column: x => x.AparatoId,
                        principalTable: "casa_aparatos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "casa_lecturas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EntidadId = table.Column<int>(type: "integer", nullable: false),
                    Valor = table.Column<double>(type: "double precision", nullable: false),
                    Fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_casa_lecturas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_casa_lecturas_casa_entidades_EntidadId",
                        column: x => x.EntidadId,
                        principalTable: "casa_entidades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_casa_aparatos_EstanciaId",
                table: "casa_aparatos",
                column: "EstanciaId");

            migrationBuilder.CreateIndex(
                name: "IX_casa_entidades_AparatoId_Clave",
                table: "casa_entidades",
                columns: new[] { "AparatoId", "Clave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_casa_lecturas_EntidadId_Fecha",
                table: "casa_lecturas",
                columns: new[] { "EntidadId", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_casa_paneles_Clave",
                table: "casa_paneles",
                column: "Clave",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "casa_lecturas");

            migrationBuilder.DropTable(
                name: "casa_paneles");

            migrationBuilder.DropTable(
                name: "casa_entidades");

            migrationBuilder.DropTable(
                name: "casa_aparatos");

            migrationBuilder.DropTable(
                name: "casa_estancias");
        }
    }
}
