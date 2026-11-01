using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class PasskeysInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "passkeys_info",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    UsuarioId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    AltaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AltaIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Dispositivo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Idioma = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Zona = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Pantalla = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UltimoUso = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UltimaIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UltimoDispositivo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    UltimoMotivo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Usos = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_passkeys_info", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "passkeys_info");
        }
    }
}
