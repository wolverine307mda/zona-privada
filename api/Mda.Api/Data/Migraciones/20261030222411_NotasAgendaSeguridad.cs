using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class NotasAgendaSeguridad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeudaId",
                table: "movimientos",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "accesos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Agente = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Exito = table.Column<bool>(type: "boolean", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accesos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ajustes",
                columns: table => new
                {
                    Clave = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Valor = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ajustes", x => x.Clave);
                });

            migrationBuilder.CreateTable(
                name: "conexiones",
                columns: table => new
                {
                    Proveedor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RefreshToken = table.Column<string>(type: "text", nullable: false),
                    AccessToken = table.Column<string>(type: "text", nullable: true),
                    Caduca = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Cuenta = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Conectado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conexiones", x => x.Proveedor);
                });

            migrationBuilder.CreateTable(
                name: "deudas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Categoria = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    Notas = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Creado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deudas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "eventos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Tipo = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    Titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Notas = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Lugar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Fin = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TodoElDia = table.Column<bool>(type: "boolean", nullable: false),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Repetir = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    RepetirHasta = table.Column<DateOnly>(type: "date", nullable: true),
                    AvisoMinutos = table.Column<int>(type: "integer", nullable: true),
                    PorCorreo = table.Column<bool>(type: "boolean", nullable: false),
                    PorWhatsApp = table.Column<bool>(type: "boolean", nullable: false),
                    ProximoAviso = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AvisoDe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UltimoAviso = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Hecho = table.Column<bool>(type: "boolean", nullable: false),
                    EnGoogle = table.Column<bool>(type: "boolean", nullable: false),
                    GoogleId = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Creado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_eventos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Contenido = table.Column<string>(type: "text", nullable: false),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Etiquetas = table.Column<string>(type: "character varying(900)", maxLength: 900, nullable: false),
                    Fijada = table.Column<bool>(type: "boolean", nullable: false),
                    Archivada = table.Column<bool>(type: "boolean", nullable: false),
                    Creado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Modificado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sesiones",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UsuarioId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Creada = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UltimaVez = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Agente = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    App = table.Column<bool>(type: "boolean", nullable: false),
                    Cerrada = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sesiones", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_movimientos_DeudaId",
                table: "movimientos",
                column: "DeudaId");

            migrationBuilder.CreateIndex(
                name: "IX_eventos_Inicio",
                table: "eventos",
                column: "Inicio");

            migrationBuilder.CreateIndex(
                name: "IX_eventos_ProximoAviso",
                table: "eventos",
                column: "ProximoAviso");

            migrationBuilder.CreateIndex(
                name: "IX_notas_Archivada_Fijada_Modificado",
                table: "notas",
                columns: new[] { "Archivada", "Fijada", "Modificado" });

            migrationBuilder.CreateIndex(
                name: "IX_sesiones_UltimaVez",
                table: "sesiones",
                column: "UltimaVez");

            migrationBuilder.AddForeignKey(
                name: "FK_movimientos_deudas_DeudaId",
                table: "movimientos",
                column: "DeudaId",
                principalTable: "deudas",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_movimientos_deudas_DeudaId",
                table: "movimientos");

            migrationBuilder.DropTable(
                name: "accesos");

            migrationBuilder.DropTable(
                name: "ajustes");

            migrationBuilder.DropTable(
                name: "conexiones");

            migrationBuilder.DropTable(
                name: "deudas");

            migrationBuilder.DropTable(
                name: "eventos");

            migrationBuilder.DropTable(
                name: "notas");

            migrationBuilder.DropTable(
                name: "sesiones");

            migrationBuilder.DropIndex(
                name: "IX_movimientos_DeudaId",
                table: "movimientos");

            migrationBuilder.DropColumn(
                name: "DeudaId",
                table: "movimientos");
        }
    }
}
