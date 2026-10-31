using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class CarteraPerfilAvisos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TieneTicket",
                table: "movimientos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PorPush",
                table: "eventos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "adjuntos",
                columns: table => new
                {
                    Clave = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Datos = table.Column<byte[]>(type: "bytea", nullable: false),
                    Creado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_adjuntos", x => x.Clave);
                });

            migrationBuilder.CreateTable(
                name: "huchas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Objetivo = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Ahorrado = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Emoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: true),
                    Creado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_huchas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mantenimientos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CadaKm = table.Column<int>(type: "integer", nullable: true),
                    CadaMeses = table.Column<int>(type: "integer", nullable: true),
                    UltimaFecha = table.Column<DateOnly>(type: "date", nullable: true),
                    UltimoKm = table.Column<int>(type: "integer", nullable: true),
                    Notas = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Avisado = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mantenimientos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "presupuestos",
                columns: table => new
                {
                    Categoria = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Limite = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    AvisadoMes = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    AvisadoNivel = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_presupuestos", x => x.Categoria);
                });

            migrationBuilder.CreateTable(
                name: "push",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Endpoint = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    P256dh = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Auth = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Dispositivo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Creado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_push", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_push_Endpoint",
                table: "push",
                column: "Endpoint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "adjuntos");

            migrationBuilder.DropTable(
                name: "huchas");

            migrationBuilder.DropTable(
                name: "mantenimientos");

            migrationBuilder.DropTable(
                name: "presupuestos");

            migrationBuilder.DropTable(
                name: "push");

            migrationBuilder.DropColumn(
                name: "TieneTicket",
                table: "movimientos");

            migrationBuilder.DropColumn(
                name: "PorPush",
                table: "eventos");
        }
    }
}
